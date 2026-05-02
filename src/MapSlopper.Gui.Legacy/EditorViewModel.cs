using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;
using MapSlopper.Core.Project;
using MapSlopper.Core.Triggers;
using MapSlopper.Core.Undo;
using MapSlopper.Core.Generation;

namespace MapSlopper.Gui.Legacy;

public sealed class EditorViewModel : INotifyPropertyChanged
{
    private MapSlopperProject _project;
    private IEditorTool _activeTool;
    private string? _currentFilePath;
    private bool _isDirty;
    private Guid? _selectedPointId;
    private string _statusMessage = "Ready.";
    private int _brushSizeCells = 1;
    private ushort _paintValue = 64;
    private double _pixelsPerWorldUnit = 1.0;
    private bool _snapToGrid;

    public EditorViewModel()
    {
        _project = new MapSlopperProject();
        Undo = new UndoStack();
        Levels = new HeightmapLevels();
        Tools = new IEditorTool[]
        {
            new AddPointTool(),
            new InsertOnEdgeTool(),
            new MovePointTool(),
            new EraseEdgeTool(),
            new ConnectPointsTool(),
            new RemovePointTool(),
            new HeightBrushTool(),
            new TriggerBrushTool(),
        };
        _activeTool = Tools[0];
        TriggerTypes = ResolveTriggerTypes();
        WireProject();
        Undo.Changed += OnUndoChanged;
    }

    public IEditorTool[] Tools { get; }
    public UndoStack Undo { get; }
    public HeightmapLevels Levels { get; }

    public MapSlopperProject Project
    {
        get => _project;
        private set
        {
            UnwireProject();
            _project = value;
            WireProject();
            OnPropertyChanged();
            ProjectReplaced?.Invoke();
        }
    }

    public event Action? ProjectReplaced;
    public event Action? RepaintRequested;

    public IEditorTool ActiveTool
    {
        get => _activeTool;
        set
        {
            if (ReferenceEquals(_activeTool, value)) return;
            _activeTool.Reset();
            _activeTool = value;
            OnPropertyChanged();
            RepaintRequested?.Invoke();
        }
    }

    public string? CurrentFilePath
    {
        get => _currentFilePath;
        set { if (_currentFilePath != value) { _currentFilePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(WindowTitle)); } }
    }

    public bool IsDirty
    {
        get => _isDirty;
        set { if (_isDirty != value) { _isDirty = value; OnPropertyChanged(); OnPropertyChanged(nameof(WindowTitle)); } }
    }

    public string WindowTitle =>
        $"MapSlopper Legacy \u2014 {(CurrentFilePath ?? "untitled")}{(IsDirty ? " *" : string.Empty)}";

    public Guid? SelectedPointId
    {
        get => _selectedPointId;
        set { if (_selectedPointId != value) { _selectedPointId = value; OnPropertyChanged(); RepaintRequested?.Invoke(); } }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
    }

    public int BrushSizeCells
    {
        get => _brushSizeCells;
        set { var v = Math.Max(1, value); if (_brushSizeCells != v) { _brushSizeCells = v; OnPropertyChanged(); RepaintRequested?.Invoke(); } }
    }

    public ushort PaintValue
    {
        get => _paintValue;
        set { if (_paintValue != value) { _paintValue = value; OnPropertyChanged(); } }
    }

    public byte ActiveTriggerTypeId { get; set; } = 1;

    public TriggerTypeConfig TriggerTypes { get; private set; }

    private TriggerTypeConfig ResolveTriggerTypes() =>
        GeometryGenerator.ResolveTriggerTypes(_project);

    public double PixelsPerWorldUnit
    {
        get => _pixelsPerWorldUnit;
        set
        {
            var v = Math.Max(1e-6, value);
            if (Math.Abs(_pixelsPerWorldUnit - v) > 1e-9) { _pixelsPerWorldUnit = v; OnPropertyChanged(); }
        }
    }

    public bool SnapToGrid
    {
        get => _snapToGrid;
        set { if (_snapToGrid != value) { _snapToGrid = value; OnPropertyChanged(); } }
    }

    public bool ShiftDown { get; set; }
    public bool ShouldSnapToGrid => _snapToGrid || ShiftDown;

    public bool IsClosedPolygon => _project.Outline.TryGetClosedPolygon(out _);

    public void SetProject(MapSlopperProject project, string? path)
    {
        Project = project;
        Undo.Clear();
        CurrentFilePath = path;
        IsDirty = false;
        SelectedPointId = null;
        _activeTool.Reset();
        StatusMessage = path is not null ? $"Opened {System.IO.Path.GetFileName(path)}." : "New project.";
    }

    public void NewProject()
    {
        Project = new MapSlopperProject();
        Undo.Clear();
        CurrentFilePath = null;
        IsDirty = false;
        SelectedPointId = null;
        _activeTool.Reset();
        StatusMessage = "New project.";
    }

    public void UndoAction()
    {
        if (!Undo.CanUndo) return;
        Undo.Undo();
        StatusMessage = "Undo.";
    }

    public void RedoAction()
    {
        if (!Undo.CanRedo) return;
        Undo.Redo();
        StatusMessage = "Redo.";
    }

    public Vec2 SnapWorld(Vec2 worldPos)
    {
        var radius = 8.0 / Math.Max(_pixelsPerWorldUnit, 1e-6);
        var hit = _project.Outline.PickPoint(worldPos, radius);
        if (hit is not null) return hit.Position;
        if (ShouldSnapToGrid) return SnapWorldToCell(worldPos);
        var step = _project.Heightmap.CellSize / 4.0;
        if (step <= 0) return worldPos;
        return new Vec2(Math.Round(worldPos.X / step) * step, Math.Round(worldPos.Y / step) * step);
    }

    public void EnsureHeightmapCovers(Vec2 worldPos)
    {
        _project.Heightmap.GrowToInclude(worldPos, worldPos);
    }

    public Vec2 SnapWorldToCell(Vec2 worldPos)
    {
        var hm = _project.Heightmap;
        var sx = Math.Round((worldPos.X - hm.Origin.X) / hm.CellSize) * hm.CellSize + hm.Origin.X;
        var sy = Math.Round((worldPos.Y - hm.Origin.Y) / hm.CellSize) * hm.CellSize + hm.Origin.Y;
        return new Vec2(sx, sy);
    }

    private void WireProject()
    {
        _project.Outline.Changed += OnGraphChanged;
        _project.Heightmap.Changed += OnGraphChanged;
        TriggerTypes = ResolveTriggerTypes();
        OnPropertyChanged(nameof(TriggerTypes));
    }

    private void UnwireProject()
    {
        _project.Outline.Changed -= OnGraphChanged;
        _project.Heightmap.Changed -= OnGraphChanged;
    }

    private void OnGraphChanged()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(IsClosedPolygon));
        RepaintRequested?.Invoke();
    }

    private void OnUndoChanged()
    {
        OnPropertyChanged(nameof(Undo));
        RepaintRequested?.Invoke();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}
