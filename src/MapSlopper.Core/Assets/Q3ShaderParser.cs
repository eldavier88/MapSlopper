using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MapSlopper.Core.Assets;

/// <summary>
/// Tiny Q3 .shader parser. Extracts the bare minimum for previewing:
/// for each top-level shader block, the first stage <c>map &lt;path&gt;</c>
/// directive whose path is a real texture (not <c>$whiteimage</c>,
/// <c>$lightmap</c>, etc.); failing that, the <c>qer_editorimage</c> path,
/// which exists in nearly every Q3 shader and is what GtkRadiant /
/// NetRadiant show in the texture browser. This is good enough for an
/// in-game-style preview because it's the same fallback strategy the Q3
/// editors use.
///
/// As a third-tier fallback, we capture the first stage's <c>rgbGen const</c>
/// color when the stage references <c>$whiteimage</c> — this is how
/// MapSlopper's bundled <c>mapslopper.shader</c> defines its visual
/// identity (no .tga ships, the color *is* the texture). Resolved as a
/// 1×1 RGBA texture by <see cref="AssetLibrary.Resolve"/>.
///
/// The parser is line-oriented but tolerant of stray <c>{</c> / <c>}</c>
/// on their own lines (which is how every q3 shader file is written) and
/// ignores everything else (blendFunc, surfaceparm, sky stages,
/// animation maps, etc.). Comments start with <c>//</c>.
/// </summary>
public static class Q3ShaderParser
{
    public sealed class ShaderInfo
    {
        public string Name { get; init; } = string.Empty;
        /// <summary>Resolved primary texture path (relative to asset root) or null.</summary>
        public string? PrimaryMap { get; init; }
        /// <summary>True if any stage uses additive/screen-style blending — preview can use it as an emissive hint.</summary>
        public bool IsEmissive { get; init; }
        /// <summary>
        /// Color from the first stage's <c>rgbGen const ( r g b )</c>, when
        /// the stage references <c>$whiteimage</c>. Each component is in
        /// [0..1] linear-ish space (Q3 treats it as the multiplier on a
        /// white texel). Null when the shader doesn't use this idiom.
        /// </summary>
        public (double R, double G, double B)? StageColor { get; init; }
    }

    public static IEnumerable<ShaderInfo> Parse(string source)
    {
        var tokens = Tokenize(source);
        var i = 0;
        while (i < tokens.Count)
        {
            // Expect a shader name token followed by '{'.
            var name = tokens[i++];
            if (name == "{" || name == "}") continue; // stray brace
            if (i >= tokens.Count || tokens[i] != "{")
            {
                // Skip orphan tokens until we find an opening brace.
                while (i < tokens.Count && tokens[i] != "{") i++;
                if (i >= tokens.Count) yield break;
            }
            i++; // consume '{'
            string? primaryMap = null;
            string? editorImage = null;
            (double R, double G, double B)? stageColor = null;
            // Track the current stage's "map" sentinel so we only attach
            // an rgbGen const to a stage that uses $whiteimage (otherwise
            // the const would tint a real texture; we shouldn't replace
            // that texture with a flat color).
            var currentStageIsWhite = false;
            var currentStageHasMap = false;
            var emissive = false;
            var depth = 1;
            while (i < tokens.Count && depth > 0)
            {
                var tok = tokens[i++];
                if (tok == "{")
                {
                    depth++;
                    if (depth == 2)
                    {
                        currentStageIsWhite = false;
                        currentStageHasMap = false;
                    }
                    continue;
                }
                if (tok == "}") { depth--; continue; }
                var lower = tok.ToLowerInvariant();
                if (lower == "qer_editorimage" && i < tokens.Count)
                {
                    editorImage ??= tokens[i++];
                }
                else if (lower == "map" && i < tokens.Count)
                {
                    var m = tokens[i++];
                    currentStageHasMap = true;
                    currentStageIsWhite = m.StartsWith("$whiteimage", StringComparison.OrdinalIgnoreCase);
                    if (primaryMap is null
                        && !m.StartsWith("$", StringComparison.Ordinal)
                        && !m.Equals("*lightmap", StringComparison.OrdinalIgnoreCase))
                    {
                        primaryMap = m;
                    }
                }
                else if (lower == "animmap" && i < tokens.Count)
                {
                    // animmap <freq> <path> [<path>...]
                    if (i < tokens.Count) i++; // freq
                    if (primaryMap is null && i < tokens.Count) primaryMap = tokens[i];
                    while (i < tokens.Count && tokens[i] != "}" && tokens[i] != "{") i++;
                }
                else if (lower == "rgbgen" && i < tokens.Count)
                {
                    var mode = tokens[i++].ToLowerInvariant();
                    if (mode == "const" && stageColor is null
                        && currentStageHasMap && currentStageIsWhite)
                    {
                        // rgbGen const ( r g b )  — parens are tokenized
                        // as their own tokens by Tokenize().
                        var color = TryReadConstColor(tokens, ref i);
                        if (color is { } c) stageColor = c;
                    }
                }
                else if (lower == "blendfunc" && i < tokens.Count)
                {
                    var bf = tokens[i++].ToLowerInvariant();
                    if (bf == "add" || bf == "gl_one" || bf.Contains("gl_one"))
                        emissive = true;
                    // Some forms take 2 args; consume one more if it looks like a blend mode.
                    if (i < tokens.Count && tokens[i].StartsWith("gl_", StringComparison.OrdinalIgnoreCase))
                        i++;
                }
            }
            yield return new ShaderInfo
            {
                Name = name,
                PrimaryMap = primaryMap ?? editorImage,
                IsEmissive = emissive,
                StageColor = stageColor,
            };
        }
    }

    private static (double R, double G, double B)? TryReadConstColor(List<string> tokens, ref int i)
    {
        // Accept either "( r g b )" or "r g b" or "( r,g,b )". Be tolerant
        // of stray parens and commas — Q3 shaders are notoriously sloppy.
        var nums = new List<double>(3);
        var consumed = 0;
        while (i < tokens.Count && nums.Count < 3 && consumed < 8)
        {
            var t = tokens[i];
            if (t == "(" || t == ")" || t == ",")
            {
                i++; consumed++; continue;
            }
            // Strip stray parens that got glued onto a number token.
            var stripped = t.Trim('(', ')', ',');
            if (stripped.Length == 0)
            {
                i++; consumed++; continue;
            }
            if (double.TryParse(stripped, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                nums.Add(d);
                i++; consumed++;
                continue;
            }
            // Non-numeric token before we got 3 numbers -> this isn't a
            // const-color we can use; bail without consuming the token.
            break;
        }
        if (nums.Count < 3) return null;
        var r = Math.Clamp(nums[0], 0.0, 4.0);
        var g = Math.Clamp(nums[1], 0.0, 4.0);
        var b = Math.Clamp(nums[2], 0.0, 4.0);
        return (r, g, b);
    }

    private static List<string> Tokenize(string source)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var n = source.Length;
        var i = 0;
        while (i < n)
        {
            var c = source[i];
            if (c == '/' && i + 1 < n && source[i + 1] == '/')
            {
                // line comment
                while (i < n && source[i] != '\n') i++;
                continue;
            }
            if (c == '{' || c == '}')
            {
                FlushToken(tokens, sb);
                tokens.Add(c.ToString());
                i++;
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                FlushToken(tokens, sb);
                i++;
                continue;
            }
            if (c == '"')
            {
                FlushToken(tokens, sb);
                i++;
                while (i < n && source[i] != '"')
                {
                    sb.Append(source[i]);
                    i++;
                }
                if (i < n) i++; // closing quote
                FlushToken(tokens, sb);
                continue;
            }
            sb.Append(c);
            i++;
        }
        FlushToken(tokens, sb);
        return tokens;
    }

    private static void FlushToken(List<string> tokens, StringBuilder sb)
    {
        if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
    }
}
