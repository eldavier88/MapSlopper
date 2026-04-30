using System;
using System.Collections.Generic;
using System.Linq;
using MapSlopper.Core.Geometry;

namespace MapSlopper.Core.Outline;

/// <summary>A vertex in the outline editor graph.</summary>
public sealed class OutlinePoint
{
    public Guid Id { get; }
    public Vec2 Position { get; set; }

    public OutlinePoint(Vec2 position) : this(Guid.NewGuid(), position) { }
    public OutlinePoint(Guid id, Vec2 position) { Id = id; Position = position; }
}

/// <summary>An undirected edge between two <see cref="OutlinePoint"/>s.</summary>
public readonly struct OutlineEdge : IEquatable<OutlineEdge>
{
    public Guid A { get; }
    public Guid B { get; }

    public OutlineEdge(Guid a, Guid b)
    {
        // Canonicalize so equality is order-independent.
        if (a.CompareTo(b) <= 0) { A = a; B = b; }
        else { A = b; B = a; }
    }

    public bool Touches(Guid id) => A == id || B == id;
    public Guid Other(Guid id) => A == id ? B : A;

    public bool Equals(OutlineEdge other) => A == other.A && B == other.B;
    public override bool Equals(object? obj) => obj is OutlineEdge e && Equals(e);
    public override int GetHashCode() => HashCode.Combine(A, B);
    public static bool operator ==(OutlineEdge a, OutlineEdge b) => a.Equals(b);
    public static bool operator !=(OutlineEdge a, OutlineEdge b) => !a.Equals(b);
}

/// <summary>
/// Undirected graph of points and edges representing the in-progress floor
/// outline. Supports incremental editing (add/remove points, add/remove edges,
/// insert points on edges) and detection of a single simple closed cycle.
/// </summary>
public sealed class OutlineGraph
{
    private readonly Dictionary<Guid, OutlinePoint> _points = new();
    private readonly HashSet<OutlineEdge> _edges = new();

    public IReadOnlyDictionary<Guid, OutlinePoint> Points => _points;
    public IReadOnlyCollection<OutlineEdge> Edges => _edges;

    /// <summary>Raised whenever the graph is mutated.</summary>
    public event Action? Changed;

    public OutlinePoint AddPoint(Vec2 position)
    {
        var p = new OutlinePoint(position);
        _points[p.Id] = p;
        Changed?.Invoke();
        return p;
    }

    public OutlinePoint AddPoint(Guid id, Vec2 position)
    {
        if (_points.ContainsKey(id))
            throw new InvalidOperationException($"Point id {id} already exists.");
        var p = new OutlinePoint(id, position);
        _points[id] = p;
        Changed?.Invoke();
        return p;
    }

    public bool RemovePoint(Guid id)
    {
        if (!_points.Remove(id)) return false;
        _edges.RemoveWhere(e => e.Touches(id));
        Changed?.Invoke();
        return true;
    }

    public void MovePoint(Guid id, Vec2 to)
    {
        if (!_points.TryGetValue(id, out var p))
            throw new KeyNotFoundException($"Point {id}");
        p.Position = to;
        Changed?.Invoke();
    }

    public bool AddEdge(Guid a, Guid b)
    {
        if (a == b) return false;
        if (!_points.ContainsKey(a) || !_points.ContainsKey(b)) return false;
        var added = _edges.Add(new OutlineEdge(a, b));
        if (added) Changed?.Invoke();
        return added;
    }

    public bool RemoveEdge(Guid a, Guid b)
    {
        var removed = _edges.Remove(new OutlineEdge(a, b));
        if (removed) Changed?.Invoke();
        return removed;
    }

    /// <summary>Inserts a new point on an existing edge, splitting it.</summary>
    public OutlinePoint InsertPointOnEdge(Guid a, Guid b, Vec2 at)
    {
        var edge = new OutlineEdge(a, b);
        if (!_edges.Contains(edge))
            throw new InvalidOperationException("Edge does not exist.");
        _edges.Remove(edge);
        var mid = new OutlinePoint(at);
        _points[mid.Id] = mid;
        _edges.Add(new OutlineEdge(a, mid.Id));
        _edges.Add(new OutlineEdge(mid.Id, b));
        Changed?.Invoke();
        return mid;
    }

    /// <summary>Picks the point closest to <paramref name="at"/> within <paramref name="radius"/>, or null.</summary>
    public OutlinePoint? PickPoint(Vec2 at, double radius)
    {
        OutlinePoint? best = null;
        var bestDist = radius * radius;
        foreach (var p in _points.Values)
        {
            var d = Vec2.DistanceSquared(p.Position, at);
            if (d <= bestDist)
            {
                bestDist = d;
                best = p;
            }
        }
        return best;
    }

    /// <summary>Picks the edge whose closest point to <paramref name="at"/> is within <paramref name="radius"/>.</summary>
    public OutlineEdge? PickEdge(Vec2 at, double radius)
    {
        OutlineEdge? best = null;
        var bestDist = radius * radius;
        foreach (var e in _edges)
        {
            var pa = _points[e.A].Position;
            var pb = _points[e.B].Position;
            var d = PointSegmentDistanceSquared(at, pa, pb);
            if (d <= bestDist)
            {
                bestDist = d;
                best = e;
            }
        }
        return best;
    }

    public int Degree(Guid id)
    {
        var d = 0;
        foreach (var e in _edges)
            if (e.Touches(id)) d++;
        return d;
    }

    /// <summary>
    /// True iff the edges form exactly one simple cycle that uses every
    /// edge-incident point with degree 2 and the cycle is geometrically simple.
    /// Outputs the resulting CCW-oriented polygon.
    /// </summary>
    public bool TryGetClosedPolygon(out Polygon2D polygon)
    {
        polygon = new Polygon2D();
        if (_edges.Count < 3) return false;

        // Collect all vertices participating in edges; require degree exactly 2.
        var endpoints = new HashSet<Guid>();
        foreach (var e in _edges)
        {
            endpoints.Add(e.A);
            endpoints.Add(e.B);
        }
        foreach (var id in endpoints)
        {
            if (Degree(id) != 2) return false;
        }
        if (endpoints.Count != _edges.Count)
            return false; // simple cycle has |V| == |E|.

        // Walk the cycle from an arbitrary vertex.
        var adj = new Dictionary<Guid, List<Guid>>();
        foreach (var id in endpoints) adj[id] = new List<Guid>();
        foreach (var e in _edges)
        {
            adj[e.A].Add(e.B);
            adj[e.B].Add(e.A);
        }

        var start = endpoints.First();
        var path = new List<Guid> { start };
        var prev = Guid.Empty;
        var current = start;
        while (true)
        {
            var neighbors = adj[current];
            Guid next;
            if (neighbors[0] != prev) next = neighbors[0];
            else next = neighbors[1];
            if (next == start)
            {
                if (path.Count == endpoints.Count) break;
                return false;
            }
            if (path.Contains(next)) return false; // shouldn't happen if degree==2 and connected
            path.Add(next);
            prev = current;
            current = next;
            if (path.Count > endpoints.Count) return false;
        }

        var verts = path.Select(id => _points[id].Position).ToList();
        var poly = new Polygon2D(verts);
        if (!poly.IsSimple()) return false;
        polygon = poly.ToCcw();
        return true;
    }

    private static double PointSegmentDistanceSquared(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var lenSq = ab.LengthSquared;
        if (lenSq < 1e-18) return Vec2.DistanceSquared(p, a);
        var t = Vec2.Dot(p - a, ab) / lenSq;
        if (t < 0) t = 0; else if (t > 1) t = 1;
        var proj = a + ab * t;
        return Vec2.DistanceSquared(p, proj);
    }

    public void Clear()
    {
        _points.Clear();
        _edges.Clear();
        Changed?.Invoke();
    }
}
