using System;
using System.Collections.Generic;
using MapSlopper.Core.Assets;
using SkiaSharp;

namespace MapSlopper.Gui.Preview;

/// <summary>
/// Lazily resolves and decodes textures from an <see cref="AssetLibrary"/>
/// for the 3D preview. TGA bytes come back already decoded to RGBA from
/// Core; PNG/JPG come back as encoded bytes which we decode here via
/// SkiaSharp (already a transitive Avalonia dep, so no extra native binary
/// is needed). Caches are keyed by texture name and never evict — a Q3 map
/// has at most a couple dozen distinct shaders so memory isn't a concern.
/// </summary>
internal sealed class TextureCache
{
    public sealed class Texture
    {
        public int Width;
        public int Height;
        public byte[] Rgba = Array.Empty<byte>(); // top-left origin
        public bool IsEmissive;
    }

    private readonly AssetLibrary _lib;
    private readonly Dictionary<string, Texture?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public TextureCache(AssetLibrary lib) { _lib = lib; }

    /// <summary>
    /// Returns the decoded texture for the given Q3 shader/texture name,
    /// or null if it can't be resolved at all (caller should use the face
    /// fallback color). Successive calls for the same name reuse the
    /// cached entry.
    /// </summary>
    public Texture? Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_cache.TryGetValue(name, out var cached)) return cached;
        var resolved = _lib.Resolve(name);
        if (resolved is null)
        {
            _cache[name] = null;
            return null;
        }
        Texture? tex = null;
        if (resolved.Rgba is not null)
        {
            tex = new Texture
            {
                Width = resolved.Width,
                Height = resolved.Height,
                Rgba = resolved.Rgba,
                IsEmissive = resolved.IsEmissive,
            };
        }
        else if (resolved.EncodedBytes is not null)
        {
            try
            {
                using var data = SKData.CreateCopy(resolved.EncodedBytes);
                using var bmp = SKBitmap.Decode(data);
                if (bmp is not null)
                {
                    var rgba = ConvertSkiaToRgba(bmp);
                    tex = new Texture
                    {
                        Width = bmp.Width,
                        Height = bmp.Height,
                        Rgba = rgba,
                        IsEmissive = resolved.IsEmissive,
                    };
                }
            }
            catch { /* corrupt -> null entry, fallback color used */ }
        }
        _cache[name] = tex;
        return tex;
    }

    private static byte[] ConvertSkiaToRgba(SKBitmap bmp)
    {
        var w = bmp.Width;
        var h = bmp.Height;
        var rgba = new byte[w * h * 4];
        // Force a known format (BGRA8888 premul) and copy out as RGBA.
        SKBitmap working = bmp;
        if (bmp.ColorType != SKColorType.Bgra8888)
        {
            working = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            bmp.CopyTo(working, SKColorType.Bgra8888);
        }
        try
        {
            var pixels = working.Bytes; // BGRA, premul
            for (var i = 0; i < w * h; i++)
            {
                rgba[i * 4 + 0] = pixels[i * 4 + 2]; // R
                rgba[i * 4 + 1] = pixels[i * 4 + 1]; // G
                rgba[i * 4 + 2] = pixels[i * 4 + 0]; // B
                rgba[i * 4 + 3] = pixels[i * 4 + 3]; // A
            }
        }
        finally
        {
            if (!ReferenceEquals(working, bmp)) working.Dispose();
        }
        return rgba;
    }
}
