using System;
using System.Collections.Generic;

namespace MapSlopper.Core.Undo;

/// <summary>A reversible operation issued through <see cref="UndoStack"/>.</summary>
public interface IUndoableCommand
{
    string Label { get; }
    void Apply();
    void Revert();
}

/// <summary>
/// Bounded undo/redo stack. Executing a new command after undoing some clears
/// the redo stack. Capacity defaults to 256 — older entries are discarded
/// when the limit is reached.
/// </summary>
public sealed class UndoStack
{
    private readonly LinkedList<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();
    public int Capacity { get; }

    public UndoStack(int capacity = 256)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    public event Action? Changed;

    public void Execute(IUndoableCommand cmd)
    {
        cmd.Apply();
        _undo.AddLast(cmd);
        while (_undo.Count > Capacity)
            _undo.RemoveFirst();
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var cmd = _undo.Last!.Value;
        _undo.RemoveLast();
        cmd.Revert();
        _redo.Push(cmd);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var cmd = _redo.Pop();
        cmd.Apply();
        _undo.AddLast(cmd);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }
}

/// <summary>Inline command that takes apply/revert delegates.</summary>
public sealed class RelayCommand : IUndoableCommand
{
    private readonly Action _apply;
    private readonly Action _revert;
    public string Label { get; }

    public RelayCommand(string label, Action apply, Action revert)
    {
        Label = label;
        _apply = apply;
        _revert = revert;
    }

    public void Apply() => _apply();
    public void Revert() => _revert();
}
