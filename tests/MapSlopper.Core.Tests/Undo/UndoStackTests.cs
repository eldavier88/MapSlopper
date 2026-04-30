using MapSlopper.Core.Undo;
using Xunit;

namespace MapSlopper.Core.Tests.Undo;

public class UndoStackTests
{
    [Fact]
    public void Execute_Undo_Redo_RestoresState()
    {
        var stack = new UndoStack();
        var value = 0;
        stack.Execute(new RelayCommand("inc", () => value += 5, () => value -= 5));
        Assert.Equal(5, value);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);

        stack.Undo();
        Assert.Equal(0, value);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);

        stack.Redo();
        Assert.Equal(5, value);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void NewExecuteAfterUndo_ClearsRedo()
    {
        var stack = new UndoStack();
        var value = 0;
        stack.Execute(new RelayCommand("a", () => value += 1, () => value -= 1));
        stack.Execute(new RelayCommand("b", () => value += 10, () => value -= 10));
        stack.Undo();
        Assert.True(stack.CanRedo);
        stack.Execute(new RelayCommand("c", () => value += 100, () => value -= 100));
        Assert.False(stack.CanRedo);
        Assert.Equal(101, value);
    }

    [Fact]
    public void Capacity_DropsOldestUndoEntries()
    {
        var stack = new UndoStack(capacity: 2);
        var value = 0;
        stack.Execute(new RelayCommand("a", () => value += 1, () => value -= 1));
        stack.Execute(new RelayCommand("b", () => value += 2, () => value -= 2));
        stack.Execute(new RelayCommand("c", () => value += 4, () => value -= 4));
        Assert.Equal(2, stack.UndoCount);
        // Oldest "a" was dropped — undoing twice gets back to value=1, NOT 0.
        stack.Undo();
        stack.Undo();
        Assert.Equal(1, value);
    }
}
