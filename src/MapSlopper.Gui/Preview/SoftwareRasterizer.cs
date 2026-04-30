using System;
using System.Runtime.CompilerServices;

namespace MapSlopper.Gui.Preview;

/// <summary>
/// Tiny software rasterizer for the 3D preview. Renders into a
/// caller-owned BGRA8888 framebuffer with a parallel float depth buffer
/// (1/Z so larger means CLOSER, simplifies near/far-plane culling).
/// Triangles are submitted in screen space already clipped against the
/// near plane (caller does that — easier to do in camera space). For each
/// triangle we use barycentric coords with perspective-correct UV
/// interpolation: divide UV and 1/Z by Z at vertices, lerp linearly,
/// reconstruct UV per-pixel by dividing by the lerp'd 1/Z. This is the
/// same trick a real GPU does in fixed-function pipelines and is what
/// makes textures stick to walls properly even at oblique camera angles.
///
/// Lighting is a single hard-coded directional Lambert applied as a
/// per-triangle multiplier so the inner pixel loop stays branch-light.
/// Emissive shader stages skip the Lambert and stay full bright (rough
/// approximation of <c>q3map_surfacelight</c>).
/// </summary>
internal sealed class SoftwareRasterizer
{
    private int _w, _h;
    // BGRA8888 little-endian: byte order in memory B,G,R,A. We write as
    // 32-bit little-endian uints (0xAARRGGBB) which gives the expected
    // byte sequence on x86/x64.
    private uint[] _color = Array.Empty<uint>();
    private float[] _depth = Array.Empty<float>();

    public int Width => _w;
    public int Height => _h;
    public uint[] Color => _color;

    public void Resize(int w, int h)
    {
        if (w == _w && h == _h) return;
        _w = w; _h = h;
        _color = new uint[w * h];
        _depth = new float[w * h];
    }

    public void Clear(uint colorBgra)
    {
        Array.Fill(_color, colorBgra);
        Array.Fill(_depth, 0f); // 1/Z = 0 -> infinitely far away
    }

    public readonly struct Vertex
    {
        public readonly float X, Y;     // screen pixel coords (sub-pixel ok)
        public readonly float InvZ;     // 1 / camera-space Z
        public readonly float UoverZ;   // U / Z
        public readonly float VoverZ;   // V / Z
        public Vertex(float x, float y, float invZ, float uOverZ, float vOverZ)
        { X = x; Y = y; InvZ = invZ; UoverZ = uOverZ; VoverZ = vOverZ; }
    }

    /// <summary>
    /// Rasterize a triangle. <paramref name="lambert"/> is the precomputed
    /// brightness multiplier in [0..1.5]. <paramref name="texPixels"/> may
    /// be null; in that case the texture sample is replaced by the
    /// fallback color (already mixed with lambert).
    /// </summary>
    public void DrawTriangle(
        Vertex a, Vertex b, Vertex c,
        byte[]? texPixels, int texW, int texH,
        float lambert,
        byte fallbackR, byte fallbackG, byte fallbackB)
    {
        // Bounding box.
        var minX = (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)));
        var maxX = (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X)));
        var minY = (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)));
        var maxY = (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y)));
        if (minX < 0) minX = 0; if (minY < 0) minY = 0;
        if (maxX >= _w) maxX = _w - 1; if (maxY >= _h) maxY = _h - 1;
        if (maxX < minX || maxY < minY) return;

        // Edge function setup. CCW in screen space (top-left origin, Y
        // down) means area > 0; flip the test if reversed (we draw both
        // sides because brush faces aren't reliably oriented after
        // BrushFaceBuilder triangulation).
        var area = EdgeFn(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        if (MathF.Abs(area) < 1e-4f) return;
        var invArea = 1f / area;

        // Premultiply lambert into fallback color once.
        var fbR = (byte)Math.Clamp((int)(fallbackR * lambert), 0, 255);
        var fbG = (byte)Math.Clamp((int)(fallbackG * lambert), 0, 255);
        var fbB = (byte)Math.Clamp((int)(fallbackB * lambert), 0, 255);
        var fbColor = (uint)((fbR << 16) | (fbG << 8) | fbB | (0xFFu << 24));

        var hasTex = texPixels is not null && texW > 0 && texH > 0;
        var twMinus1 = texW - 1;
        var thMinus1 = texH - 1;

        for (var y = minY; y <= maxY; y++)
        {
            var rowOff = y * _w;
            for (var x = minX; x <= maxX; x++)
            {
                // Sample at pixel center.
                var px = x + 0.5f;
                var py = y + 0.5f;
                var w0 = EdgeFn(b.X, b.Y, c.X, c.Y, px, py) * invArea;
                var w1 = EdgeFn(c.X, c.Y, a.X, a.Y, px, py) * invArea;
                var w2 = 1f - w0 - w1;
                // Inside test (allow either winding).
                if (area > 0 ? (w0 < 0 || w1 < 0 || w2 < 0)
                              : (w0 > 0 || w1 > 0 || w2 > 0)) continue;

                // Perspective-correct interpolation.
                var invZ = w0 * a.InvZ + w1 * b.InvZ + w2 * c.InvZ;
                if (invZ <= 0) continue;
                // Depth test: keep the pixel that is CLOSER (larger 1/Z).
                var idx = rowOff + x;
                if (invZ <= _depth[idx]) continue;
                _depth[idx] = invZ;

                uint pixel;
                if (hasTex)
                {
                    var u = (w0 * a.UoverZ + w1 * b.UoverZ + w2 * c.UoverZ) / invZ;
                    var v = (w0 * a.VoverZ + w1 * b.VoverZ + w2 * c.VoverZ) / invZ;
                    // Wrap (Q3-style: textures repeat).
                    var us = (int)(u * texW) % texW;
                    var vs = (int)(v * texH) % texH;
                    if (us < 0) us += texW;
                    if (vs < 0) vs += texH;
                    if (us > twMinus1) us = twMinus1;
                    if (vs > thMinus1) vs = thMinus1;
                    var tIdx = (vs * texW + us) * 4;
                    var tR = texPixels![tIdx + 0];
                    var tG = texPixels[tIdx + 1];
                    var tB = texPixels[tIdx + 2];
                    var lr = (byte)Math.Clamp((int)(tR * lambert), 0, 255);
                    var lg = (byte)Math.Clamp((int)(tG * lambert), 0, 255);
                    var lb = (byte)Math.Clamp((int)(tB * lambert), 0, 255);
                    pixel = (uint)((lr << 16) | (lg << 8) | lb | (0xFFu << 24));
                }
                else
                {
                    pixel = fbColor;
                }
                _color[idx] = pixel;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float EdgeFn(float ax, float ay, float bx, float by, float px, float py) =>
        (bx - ax) * (py - ay) - (by - ay) * (px - ax);
}
