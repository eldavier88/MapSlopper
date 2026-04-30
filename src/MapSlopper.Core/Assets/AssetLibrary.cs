using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace MapSlopper.Core.Assets;

/// <summary>
/// Resolves Q3 texture/shader names to actual image file bytes by scanning a
/// list of "asset roots" — each root is either a directory tree (mounted
/// like <c>fs_basepath/baseq3</c>) or a <c>.pk3</c> zip file. Mirrors the
/// Q3 file system: shader scripts in <c>scripts/*.shader</c> are parsed for
/// shader-name → first-stage <c>map</c> path; texture lookups try those
/// resolved paths first, then fall back to <c>textures/&lt;name&gt;.{tga,jpg,png}</c>.
///
/// This is the data model only — image decoding is split: TGA is decoded
/// directly here (small format), PNG/JPG raw bytes are returned for the GUI
/// to decode via Avalonia/Skia (Core has no graphics dependency). Returns
/// <c>null</c> for missing assets so the preview can fall back to a flat
/// color.
/// </summary>
public sealed class AssetLibrary
{
    private readonly List<IAssetRoot> _roots = new();
    private readonly Dictionary<string, Q3ShaderParser.ShaderInfo> _shaders =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> RootDescriptions { get; }

    /// <summary>Number of shader definitions parsed across all roots.</summary>
    public int ShaderCount => _shaders.Count;

    private AssetLibrary(List<IAssetRoot> roots, IReadOnlyList<string> rootDescs)
    {
        _roots = roots;
        RootDescriptions = rootDescs;
    }

    /// <summary>
    /// Build a library from a list of asset-root paths (directories or
    /// .pk3 files). Missing or unreadable roots are silently skipped; the
    /// library is always returned (possibly empty), so the preview never
    /// dies because a path was wrong.
    /// </summary>
    public static AssetLibrary Load(IEnumerable<string> roots)
    {
        var loaded = new List<IAssetRoot>();
        var descs = new List<string>();
        foreach (var root in roots ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            try
            {
                if (Directory.Exists(root))
                {
                    loaded.Add(new DirectoryRoot(root));
                    descs.Add($"dir: {root}");
                    // Also auto-mount any .pk3 files at the root (Q3
                    // convention: pk3s sit alongside scripts/ and
                    // textures/ inside baseq3).
                    foreach (var pk3 in Directory.EnumerateFiles(root, "*.pk3", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            loaded.Add(new ZipRoot(pk3));
                            descs.Add($"pk3: {Path.GetFileName(pk3)}");
                        }
                        catch { /* corrupt pk3 -> skip silently */ }
                    }
                }
                else if (File.Exists(root) && root.EndsWith(".pk3", StringComparison.OrdinalIgnoreCase))
                {
                    loaded.Add(new ZipRoot(root));
                    descs.Add($"pk3: {Path.GetFileName(root)}");
                }
            }
            catch { /* skip bad root */ }
        }

        var lib = new AssetLibrary(loaded, descs);
        lib.IndexShaders();
        return lib;
    }

    private void IndexShaders()
    {
        foreach (var root in _roots)
        {
            foreach (var path in root.EnumerateFiles("scripts"))
            {
                if (!path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)) continue;
                var bytes = root.ReadAllBytes(path);
                if (bytes is null) continue;
                string text;
                try { text = System.Text.Encoding.UTF8.GetString(bytes); }
                catch { continue; }
                foreach (var info in Q3ShaderParser.Parse(text))
                {
                    if (string.IsNullOrEmpty(info.Name)) continue;
                    // First wins so earlier roots override later ones, like Q3 fs.
                    if (!_shaders.ContainsKey(info.Name)) _shaders[info.Name] = info;
                }
            }
        }
    }

    /// <summary>
    /// Decoded RGBA texture (top-left origin, 4 bpp). <see cref="EncodedBytes"/>
    /// is set instead when the file is PNG/JPG and Core can't decode (the
    /// GUI layer will).
    /// </summary>
    public sealed record ResolvedTexture
    {
        public string ResolvedPath { get; init; } = string.Empty;
        public bool IsEmissive { get; init; }
        // Either decoded RGBA OR encoded bytes; never both.
        public int Width { get; init; }
        public int Height { get; init; }
        public byte[]? Rgba { get; init; }
        public byte[]? EncodedBytes { get; init; }
        public string? EncodedExtension { get; init; } // ".png" / ".jpg"
    }

    /// <summary>
    /// Resolve a Q3 texture name (e.g. <c>random/floor</c> or
    /// <c>textures/random/floor</c>) to bytes. Returns null when nothing
    /// matches across all roots.
    /// </summary>
    public ResolvedTexture? Resolve(string textureName)
    {
        if (string.IsNullOrWhiteSpace(textureName)) return null;
        var normalized = textureName.Replace('\\', '/').Trim();
        if (normalized.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("textures/".Length);
        var shaderKey = "textures/" + normalized;

        var emissive = false;
        // 1) Try shader's primary map.
        if (_shaders.TryGetValue(shaderKey, out var sh) && sh.PrimaryMap is { } map)
        {
            emissive = sh.IsEmissive;
            var resolved = TryLoadFile(map);
            if (resolved is not null) return resolved with { IsEmissive = emissive };
        }
        // 2) Fall back to direct path probes.
        var basePath = "textures/" + normalized;
        foreach (var ext in new[] { ".tga", ".jpg", ".jpeg", ".png" })
        {
            var resolved = TryLoadFile(basePath + ext);
            if (resolved is not null) return resolved with { IsEmissive = emissive };
        }
        return null;
    }

    private ResolvedTexture? TryLoadFile(string relPath)
    {
        var p = relPath.Replace('\\', '/').Trim();
        // Strip extension to try alternates if the shader specifies .tga but
        // only .jpg exists (or vice versa) — common in Q3 shipped content.
        var dot = p.LastIndexOf('.');
        var stem = dot >= 0 ? p.Substring(0, dot) : p;
        var preferredExt = dot >= 0 ? p.Substring(dot) : null;
        var orderings = preferredExt is null
            ? new[] { ".tga", ".jpg", ".jpeg", ".png" }
            : new[] { preferredExt, ".tga", ".jpg", ".jpeg", ".png" };

        foreach (var ext in orderings)
        {
            foreach (var root in _roots)
            {
                var candidate = stem + ext;
                var bytes = root.ReadAllBytes(candidate);
                if (bytes is null) continue;
                if (string.Equals(ext, ".tga", StringComparison.OrdinalIgnoreCase))
                {
                    var img = TgaDecoder.TryDecode(bytes);
                    if (img is null) continue;
                    return new ResolvedTexture
                    {
                        ResolvedPath = candidate,
                        Width = img.Width,
                        Height = img.Height,
                        Rgba = img.Rgba,
                    };
                }
                return new ResolvedTexture
                {
                    ResolvedPath = candidate,
                    EncodedBytes = bytes,
                    EncodedExtension = ext,
                };
            }
        }
        return null;
    }

    // ---- root abstraction ---------------------------------------------------

    private interface IAssetRoot
    {
        IEnumerable<string> EnumerateFiles(string subdir);
        byte[]? ReadAllBytes(string relativePath);
    }

    private sealed class DirectoryRoot : IAssetRoot
    {
        private readonly string _root;
        public DirectoryRoot(string root) { _root = root; }
        public IEnumerable<string> EnumerateFiles(string subdir)
        {
            var dir = Path.Combine(_root, subdir);
            if (!Directory.Exists(dir)) yield break;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(_root, f).Replace('\\', '/');
                yield return rel;
            }
        }
        public byte[]? ReadAllBytes(string relativePath)
        {
            try
            {
                var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) return null;
                return File.ReadAllBytes(full);
            }
            catch { return null; }
        }
    }

    private sealed class ZipRoot : IAssetRoot, IDisposable
    {
        // Open lazily-per-call: ZipArchive isn't thread-safe and we don't
        // want a long-lived file handle in the editor.
        private readonly string _zipPath;
        private readonly Dictionary<string, string> _entriesByLowerKey =
            new(StringComparer.OrdinalIgnoreCase);
        public ZipRoot(string zipPath)
        {
            _zipPath = zipPath;
            using var z = ZipFile.OpenRead(zipPath);
            foreach (var e in z.Entries)
            {
                if (e.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                _entriesByLowerKey[e.FullName] = e.FullName;
            }
        }
        public IEnumerable<string> EnumerateFiles(string subdir)
        {
            var prefix = subdir.Replace('\\', '/').TrimEnd('/') + "/";
            return _entriesByLowerKey.Values.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
        public byte[]? ReadAllBytes(string relativePath)
        {
            if (!_entriesByLowerKey.TryGetValue(relativePath, out var fullName)) return null;
            try
            {
                using var z = ZipFile.OpenRead(_zipPath);
                var entry = z.GetEntry(fullName);
                if (entry is null) return null;
                using var s = entry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
            catch { return null; }
        }
        public void Dispose() { }
    }
}
