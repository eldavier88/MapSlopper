using System;
using System.IO;

namespace MapSlopper.Core.Assets;

/// <summary>
/// Minimal Truevision TGA decoder for game-asset .tga files. Supports the
/// formats Quake 3 textures actually use:
///   * Type 2  — uncompressed true-color (24/32 bpp)
///   * Type 10 — RLE compressed true-color (24/32 bpp)
/// Output is always 32-bit RGBA, top-left origin (caller flips internally
/// based on the TGA image-descriptor byte). Returns <c>null</c> for
/// unsupported variants instead of throwing so the asset loader can fall
/// back to other extensions silently.
/// </summary>
public static class TgaDecoder
{
    public sealed class DecodedImage
    {
        public int Width { get; init; }
        public int Height { get; init; }
        /// <summary>RGBA bytes, row-major top-to-bottom (4 bytes per pixel).</summary>
        public byte[] Rgba { get; init; } = Array.Empty<byte>();
    }

    public static DecodedImage? TryDecode(byte[] data)
    {
        if (data.Length < 18) return null;
        try
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            var idLength = br.ReadByte();
            var colorMapType = br.ReadByte();
            var imageType = br.ReadByte();
            // Color map spec (skipped).
            br.ReadInt16(); br.ReadInt16(); br.ReadByte();
            // Image spec.
            br.ReadInt16(); br.ReadInt16(); // x/y origin
            int width = br.ReadInt16();
            int height = br.ReadInt16();
            var pixelDepth = br.ReadByte();
            var imageDescriptor = br.ReadByte();
            if (idLength > 0) br.ReadBytes(idLength);
            if (colorMapType != 0) return null; // palette unsupported
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192) return null;
            if (pixelDepth != 24 && pixelDepth != 32) return null;
            var hasAlpha = pixelDepth == 32;
            var bytesPerPixel = pixelDepth / 8;

            // imageDescriptor bit 5 = origin: 0 -> bottom-left, 1 -> top-left.
            var topDown = (imageDescriptor & 0x20) != 0;
            var rgba = new byte[width * height * 4];

            if (imageType == 2)
            {
                // Uncompressed BGR(A).
                var raw = br.ReadBytes(width * height * bytesPerPixel);
                if (raw.Length != width * height * bytesPerPixel) return null;
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var srcIdx = (y * width + x) * bytesPerPixel;
                    var dstY = topDown ? y : height - 1 - y;
                    var dstIdx = (dstY * width + x) * 4;
                    rgba[dstIdx + 0] = raw[srcIdx + 2]; // R
                    rgba[dstIdx + 1] = raw[srcIdx + 1]; // G
                    rgba[dstIdx + 2] = raw[srcIdx + 0]; // B
                    rgba[dstIdx + 3] = hasAlpha ? raw[srcIdx + 3] : (byte)255;
                }
                return new DecodedImage { Width = width, Height = height, Rgba = rgba };
            }
            if (imageType == 10)
            {
                // RLE: each packet starts with 1 byte: top bit = run, low 7 bits = count-1.
                var pixels = new byte[width * height * 4];
                var pi = 0;
                var total = width * height;
                var px = new byte[4]; px[3] = 255;
                while (pi < total)
                {
                    var hdr = br.ReadByte();
                    var count = (hdr & 0x7F) + 1;
                    if ((hdr & 0x80) != 0)
                    {
                        // RLE run: one pixel repeated.
                        px[2] = br.ReadByte();
                        px[1] = br.ReadByte();
                        px[0] = br.ReadByte();
                        if (hasAlpha) px[3] = br.ReadByte(); else px[3] = 255;
                        for (var k = 0; k < count && pi < total; k++, pi++)
                        {
                            var dst = pi * 4;
                            pixels[dst] = px[0];
                            pixels[dst + 1] = px[1];
                            pixels[dst + 2] = px[2];
                            pixels[dst + 3] = px[3];
                        }
                    }
                    else
                    {
                        // Raw packet.
                        for (var k = 0; k < count && pi < total; k++, pi++)
                        {
                            px[2] = br.ReadByte();
                            px[1] = br.ReadByte();
                            px[0] = br.ReadByte();
                            if (hasAlpha) px[3] = br.ReadByte(); else px[3] = 255;
                            var dst = pi * 4;
                            pixels[dst] = px[0];
                            pixels[dst + 1] = px[1];
                            pixels[dst + 2] = px[2];
                            pixels[dst + 3] = px[3];
                        }
                    }
                }
                if (!topDown)
                {
                    // Flip vertically.
                    var stride = width * 4;
                    var flipped = new byte[pixels.Length];
                    for (var y = 0; y < height; y++)
                        Buffer.BlockCopy(pixels, y * stride, flipped, (height - 1 - y) * stride, stride);
                    pixels = flipped;
                }
                return new DecodedImage { Width = width, Height = height, Rgba = pixels };
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
