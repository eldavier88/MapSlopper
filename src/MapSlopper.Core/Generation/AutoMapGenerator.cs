using System;
using System.Collections.Generic;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;
using MapSlopper.Core.Project;

namespace MapSlopper.Core.Generation;

/// <summary>
/// Procedurally builds a complete <see cref="MapSlopperProject"/> (outline +
/// painted heightmap + optional trigger paint), then validates it through the
/// normal <see cref="GeometryGenerator"/> pipeline to guarantee exportable
/// output.
/// </summary>
public static class AutoMapGenerator
{
	public sealed class Options
	{
		public int WidthCells { get; set; } = 96;
		public int HeightCells { get; set; } = 96;
		public double CellSize { get; set; } = 32.0;
		public int Complexity { get; set; } = 3; // 1..5
		public ushort Relief { get; set; } = 192;
		public int? Seed { get; set; }
	}

	public sealed class Result
	{
		public MapSlopperProject Project { get; set; } = new();
		public int SeedUsed { get; set; }
		public int Attempts { get; set; }
	}

	public static Result Generate(MapSlopperProject template, Options? options = null)
	{
		if (template is null) throw new ArgumentNullException(nameof(template));
		options ??= new Options();

		var width = Math.Clamp(options.WidthCells, 24, 512);
		var height = Math.Clamp(options.HeightCells, 24, 512);
		var cellSize = Math.Max(4.0, options.CellSize);
		var complexity = Math.Clamp(options.Complexity, 1, 5);
		var relief = options.Relief < 32 ? (ushort)32 : options.Relief;
		var baseSeed = options.Seed ?? Environment.TickCount;

		for (var attempt = 0; attempt < 24; attempt++)
		{
			var seed = unchecked(baseSeed + attempt * 1013);
			var rng = new Random(seed);
			var p = CloneTemplateScalars(template);

			p.Heightmap = new Heightmap16(width, height, cellSize, Vec2.Zero);
			p.TriggerLayer = new Heightmap16(width, height, cellSize, Vec2.Zero);
			p.Outline.Clear();

			var poly = BuildConvexPolygon(width, height, cellSize, complexity, rng);
			if (poly.Count < 3 || !poly.IsSimple()) continue;

			var ids = new Guid[poly.Count];
			for (var i = 0; i < ids.Length; i++) ids[i] = Guid.NewGuid();
			for (var i = 0; i < poly.Count; i++)
				p.Outline.AddPoint(ids[i], poly[i]);
			for (var i = 0; i < poly.Count; i++)
				p.Outline.AddEdge(ids[i], ids[(i + 1) % ids.Length]);

			PaintHeightmap(p.Heightmap, poly, relief, complexity, rng);
			PaintTriggerPatches(p, poly, complexity, rng);

			var validate = GeometryGenerator.Generate(p);
			if (!validate.Ok)
				continue;

			return new Result { Project = p, SeedUsed = seed, Attempts = attempt + 1 };
		}

		throw new InvalidOperationException("Automatic map generation failed after multiple valid-shape attempts.");
	}

	private static MapSlopperProject CloneTemplateScalars(MapSlopperProject t)
	{
		var p = new MapSlopperProject
		{
			FormatVersion = t.FormatVersion,
			CeilingHeight = t.CeilingHeight,
			WallThickness = t.WallThickness,
			FloorTexture = t.FloorTexture,
			WallTexture = t.WallTexture,
			CeilingTexture = t.CeilingTexture,
			WindowTexture = t.WindowTexture,
			WallSplitHeight = t.WallSplitHeight,
			PlayerStartOverride = null,
			LightSpacing = t.LightSpacing,
			LightIntensity = t.LightIntensity,
			LightInsetFromCeiling = t.LightInsetFromCeiling,
			CeilingThickness = t.CeilingThickness,
			FloorBaseThickness = t.FloorBaseThickness,
			TriggerOverrides = t.TriggerOverrides,
			AssetRoots = new System.Collections.Generic.List<string>(t.AssetRoots),
		};
		return p;
	}

	private static Polygon2D BuildConvexPolygon(int w, int h, double cs, int complexity, Random rng)
	{
		var minWorld = Math.Min(w, h) * cs;
		var cx = w * cs * 0.5;
		var cy = h * cs * 0.5;
		var margin = 4.0 * cs;
		var baseR = Math.Max(cs * 6.0, minWorld * (0.30 + 0.03 * complexity));
		var jitter = baseR * (0.10 + 0.02 * complexity);
		var verts = 6 + complexity * 2 + rng.Next(0, 2); // ~8..17

		var pts = new List<Vec2>(verts);
		var angle = rng.NextDouble() * Math.PI * 2.0;
		for (var i = 0; i < verts; i++)
		{
			angle += (Math.PI * 2.0) / verts;
			var r = baseR + (rng.NextDouble() * 2.0 - 1.0) * jitter;
			var x = cx + Math.Cos(angle) * r;
			var y = cy + Math.Sin(angle) * r;
			x = Math.Max(margin, Math.Min(w * cs - margin, x));
			y = Math.Max(margin, Math.Min(h * cs - margin, y));
			pts.Add(new Vec2(x, y));
		}
		return new Polygon2D(pts).ToCcw();
	}

	private static void PaintHeightmap(Heightmap16 hm, Polygon2D poly, ushort relief, int complexity, Random rng)
	{
		var center = poly.Centroid();
		var maxDist = 1.0;
		for (var i = 0; i < poly.Count; i++)
		{
			var d = Math.Sqrt(Vec2.DistanceSquared(poly[i], center));
			if (d > maxDist) maxDist = d;
		}

		var baseLevel = 48 + complexity * 16;
		var amp = Math.Max(24, relief - baseLevel);
		var freq = 0.035 / Math.Max(1.0, complexity * 0.7);
		var phaseA = rng.NextDouble() * 1000.0;
		var phaseB = rng.NextDouble() * 1000.0;

		for (var y = 0; y < hm.Height; y++)
		for (var x = 0; x < hm.Width; x++)
		{
			var wp = new Vec2(
				hm.Origin.X + (x + 0.5) * hm.CellSize,
				hm.Origin.Y + (y + 0.5) * hm.CellSize);
			if (!poly.ContainsPoint(wp))
			{
				hm.Data[y * hm.Width + x] = 0;
				continue;
			}

			var radial = 1.0 - Math.Min(1.0, Math.Sqrt(Vec2.DistanceSquared(wp, center)) / maxDist);
			var n1 = Math.Sin(wp.X * freq + phaseA) * 0.5 + 0.5;
			var n2 = Math.Cos(wp.Y * (freq * 1.37) + phaseB) * 0.5 + 0.5;
			var n3 = Math.Sin((wp.X + wp.Y) * (freq * 0.71) + phaseA * 0.3) * 0.5 + 0.5;
			var blend = 0.40 * n1 + 0.35 * n2 + 0.25 * n3;
			var h = baseLevel + amp * (0.40 * radial + 0.60 * blend);

			var q = (int)Math.Round(h / 8.0) * 8;
			hm.Data[y * hm.Width + x] = (ushort)Math.Clamp(q, 0, ushort.MaxValue);
		}
	}

	private static void PaintTriggerPatches(MapSlopperProject p, Polygon2D poly, int complexity, Random rng)
	{
		var tl = p.TriggerLayer;
		var types = GeometryGenerator.ResolveTriggerTypes(p).Types;
		if (types.Count == 0) return;

		var patches = Math.Max(1, complexity);
		for (var i = 0; i < patches; i++)
		{
			var type = types[i % types.Count];
			var rw = Math.Max(2, tl.Width / (10 + rng.Next(0, 6)));
			var rh = Math.Max(2, tl.Height / (10 + rng.Next(0, 6)));
			var ox = rng.Next(0, Math.Max(1, tl.Width - rw));
			var oy = rng.Next(0, Math.Max(1, tl.Height - rh));

			for (var y = oy; y < oy + rh; y++)
			for (var x = ox; x < ox + rw; x++)
			{
				var wp = new Vec2(
					tl.Origin.X + (x + 0.5) * tl.CellSize,
					tl.Origin.Y + (y + 0.5) * tl.CellSize);
				if (!poly.ContainsPoint(wp)) continue;
				tl.Data[y * tl.Width + x] = type.Id;
			}
		}
	}
}
