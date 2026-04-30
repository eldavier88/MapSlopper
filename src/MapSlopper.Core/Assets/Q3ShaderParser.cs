using System;
using System.Collections.Generic;
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
/// The parser is line-oriented but tolerant of stray <c>{</c> / <c>}</c>
/// on their own lines (which is how every q3 shader file is written) and
/// ignores everything else (rgbGen, blendFunc, surfaceparm, sky stages,
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
            var emissive = false;
            var depth = 1;
            while (i < tokens.Count && depth > 0)
            {
                var tok = tokens[i++];
                if (tok == "{") { depth++; continue; }
                if (tok == "}") { depth--; continue; }
                var lower = tok.ToLowerInvariant();
                if (lower == "qer_editorimage" && i < tokens.Count)
                {
                    editorImage ??= tokens[i++];
                }
                else if (lower == "map" && i < tokens.Count)
                {
                    var m = tokens[i++];
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
            };
        }
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
