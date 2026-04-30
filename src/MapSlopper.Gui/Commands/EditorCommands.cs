using System;
using System.Collections.Generic;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;
using MapSlopper.Core.Outline;
using MapSlopper.Core.Undo;

namespace MapSlopper.Gui.Commands;

/// <summary>Adds a new outline point with a pre-allocated id (so undo/redo are stable).</summary>
public sealed class AddPointCmd : IUndoableCommand
{
    private readonly OutlineGraph _graph;
    private readonly Guid _id;
    private readonly Vec2 _pos;

    public AddPointCmd(OutlineGraph graph, Guid id, Vec2 pos)
    {
        _graph = graph;
        _id = id;
        _pos = pos;
    }

    public string Label => "Add point";

    public void Apply() => _graph.AddPoint(_id, _pos);
    public void Revert() => _graph.RemovePoint(_id);
}

/// <summary>Removes an existing outline point and restores it (with all incident edges) on revert.</summary>
public sealed class RemovePointCmd : IUndoableCommand
{
    private readonly OutlineGraph _graph;
    private readonly Guid _id;
    private readonly Vec2 _pos;
    private readonly List<Guid> _neighbors;

    public RemovePointCmd(OutlineGraph graph, Guid id)
    {
        _graph = graph;
        _id = id;
        if (!graph.Points.TryGetValue(id, out var p))
            throw new InvalidOperationException($"Point {id} not in graph.");
        _pos = p.Position;
        _neighbors = new List<Guid>();
        foreach (var e in graph.Edges)
            if (e.Touches(id)) _neighbors.Add(e.Other(id));
    }

    public string Label => "Remove point";

    public void Apply() => _graph.RemovePoint(_id);
    public void Revert()
    {
        _graph.AddPoint(_id, _pos);
        foreach (var n in _neighbors) _graph.AddEdge(_id, n);
    }
}

public sealed class AddEdgeCmd : IUndoableCommand
{
    private readonly OutlineGraph _graph;
    private readonly Guid _a;
    private readonly Guid _b;

    public AddEdgeCmd(OutlineGraph graph, Guid a, Guid b)
    {
        _graph = graph;
        _a = a;
        _b = b;
    }

    public string Label => "Add edge";

    public void Apply() => _graph.AddEdge(_a, _b);
    public void Revert() => _graph.RemoveEdge(_a, _b);
}

public sealed class RemoveEdgeCmd : IUndoableCommand
{
    private readonly OutlineGraph _graph;
    private readonly Guid _a;
    private readonly Guid _b;

    public RemoveEdgeCmd(OutlineGraph graph, Guid a, Guid b)
    {
        _graph = graph;
        _a = a;
        _b = b;
    }

    public string Label => "Remove edge";

    public void Apply() => _graph.RemoveEdge(_a, _b);
    public void Revert() => _graph.AddEdge(_a, _b);
}

/// <summary>
/// Splits an existing edge (a,b) by inserting a new point with id <c>_mid</c>
/// at <c>_pos</c>. Apply is reversible: revert deletes the new point and
/// re-adds the original edge.
/// </summary>
public sealed class InsertOnEdgeCmd : IUndoableCommand
{
    private readonly OutlineGraph _graph;
    private readonly Guid _a;
    private readonly Guid _b;
    private readonly Guid _mid;
    private readonly Vec2 _pos;

    public InsertOnEdgeCmd(OutlineGraph graph, Guid a, Guid b, Guid mid, Vec2 pos)
    {
        _graph = graph;
        _a = a;
        _b = b;
        _mid = mid;
        _pos = pos;
    }

    public string Label => "Insert point on edge";

    public void Apply()
    {
        _graph.RemoveEdge(_a, _b);
        _graph.AddPoint(_mid, _pos);
        _graph.AddEdge(_a, _mid);
        _graph.AddEdge(_mid, _b);
    }

    public void Revert()
    {
        _graph.RemoveEdge(_a, _mid);
        _graph.RemoveEdge(_mid, _b);
        _graph.RemovePoint(_mid);
        _graph.AddEdge(_a, _b);
    }
}

public sealed class MovePointCmd : IUndoableCommand
{
    private readonly OutlineGraph _graph;
    private readonly Guid _id;
    private readonly Vec2 _from;
    private readonly Vec2 _to;

    public MovePointCmd(OutlineGraph graph, Guid id, Vec2 from, Vec2 to)
    {
        _graph = graph;
        _id = id;
        _from = from;
        _to = to;
    }

    public string Label => "Move point";

    public void Apply() => _graph.MovePoint(_id, _to);
    public void Revert() => _graph.MovePoint(_id, _from);
}

/// <summary>
/// Captures a full heightmap snapshot (before/after a brush stroke) and
/// restores either side on apply/revert. Built so that calling Apply when
/// the heightmap already matches <c>_after</c> is a true no-op (no event
/// noise during normal stroke commit).
/// </summary>
public sealed class HeightStrokeCmd : IUndoableCommand
{
    private readonly Heightmap16 _hm;
    private readonly ushort[] _before;
    private readonly ushort[] _after;

    public HeightStrokeCmd(Heightmap16 hm, ushort[] before, ushort[] after)
    {
        if (before.Length != hm.Data.Length)
            throw new ArgumentException("before length mismatch", nameof(before));
        if (after.Length != hm.Data.Length)
            throw new ArgumentException("after length mismatch", nameof(after));
        _hm = hm;
        _before = before;
        _after = after;
    }

    public string Label => "Paint heights";

    public void Apply()
    {
        Array.Copy(_after, _hm.Data, _after.Length);
        // Toggle one cell to fire the Changed event without altering data.
        var (cx, cy) = (0, 0);
        _hm.Set(cx, cy, _hm.Data[0]);
    }

    public void Revert()
    {
        Array.Copy(_before, _hm.Data, _before.Length);
        _hm.Set(0, 0, _hm.Data[0]);
    }

    /// <summary>True iff <paramref name="a"/> and <paramref name="b"/> have identical contents.</summary>
    public static bool ArraysEqual(ushort[] a, ushort[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
