using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using MapSlopper.Core.Export;
using MapSlopper.Core.Generation;
using MapSlopper.Core.Geometry;
using MapSlopper.Core.Heightmap;
using MapSlopper.Core.Project;
using MapSlopper.Core.Undo;
using MapSlopper.Gui.Tools;

namespace MapSlopper.Gui;

/// <summary>
/// Central editor state: holds the active project, undo stack, current tool,
/// file path, dirty flag, and viewport-shared parameters that tools need
/// (pixels-per-world-unit, cursor world position, brush size, paint value).
/// All project mutations should go through <see cref="Undo"/>.
/// </summary>
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
        };
        _activeTool = Tools[0];
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

    /// <summary>Raised whenever the underlying <see cref="Project"/> reference changes.</summary>
    public event Action? ProjectReplaced;

    /// <summary>Raised whenever any sub-graph of the project mutates or undo state changes.</summary>
    public event Action? RepaintRequested;

    public IEditorTool ActiveTool
    {
        get => _activeTool;
        set
        {
            if (ReferenceEquals(_activeTool, value)) return;
            // Reset previous tool's chained / pending state on switch.
            _activeTool.Reset();
            _activeTool = value;
            OnPropertyChanged();
            RepaintRequested?.Invoke();
        }
    }

    public string? CurrentFilePath
    {
        get => _currentFilePath;
        private set { if (_currentFilePath != value) { _currentFilePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(WindowTitle)); } }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set { if (_isDirty != value) { _isDirty = value; OnPropertyChanged(); OnPropertyChanged(nameof(WindowTitle)); } }
    }

    public string WindowTitle =>
        $"MapSlopper — {(CurrentFilePath ?? "untitled")}{(IsDirty ? " *" : string.Empty)}";

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

    public double PixelsPerWorldUnit
    {
        get => _pixelsPerWorldUnit;
        set
        {
            var v = Math.Max(1e-6, value);
            if (Math.Abs(_pixelsPerWorldUnit - v) > 1e-9) { _pixelsPerWorldUnit = v; OnPropertyChanged(); }
        }
    }

    /// <summary>
    /// When true (toggle: 'G' key, or the Snap checkbox), the Move tool snaps
    /// vertices to whole heightmap-cell multiples relative to the heightmap
    /// origin. Holding Shift while dragging acts as a temporary toggle.
    /// </summary>
    public bool SnapToGrid
    {
        get => _snapToGrid;
        set { if (_snapToGrid != value) { _snapToGrid = value; OnPropertyChanged(); } }
    }

    /// <summary>Transient: true while Shift is held during a pointer event.</summary>
    public bool ShiftDown { get; set; }

    /// <summary>True when grid snap should currently apply (toggle OR Shift).</summary>
    public bool ShouldSnapToGrid => _snapToGrid || ShiftDown;

    public bool IsClosedPolygon =>
        _project.Outline.TryGetClosedPolygon(out _);

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

    public async Task OpenAsync(Window owner)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open project",
            AllowMultiple = false,
            Filters = new List<FileDialogFilter>
            {
                new FileDialogFilter { Name = "MapSlopper project", Extensions = { "json" } },
                new FileDialogFilter { Name = "All files", Extensions = { "*" } },
            },
        };
        var result = await dlg.ShowAsync(owner).ConfigureAwait(true);
        if (result is null || result.Length == 0) return;
        try
        {
            var loaded = ProjectJsonIo.Load(result[0]);
            Project = loaded;
            Undo.Clear();
            CurrentFilePath = result[0];
            IsDirty = false;
            SelectedPointId = null;
            _activeTool.Reset();
            StatusMessage = $"Opened {Path.GetFileName(result[0])}.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Open failed: " + ex.Message;
        }
    }

    public async Task<bool> SaveAsync(Window owner)
    {
        if (string.IsNullOrEmpty(CurrentFilePath))
            return await SaveAsAsync(owner).ConfigureAwait(true);
        try
        {
            ProjectJsonIo.Save(_project, CurrentFilePath!);
            IsDirty = false;
            StatusMessage = $"Saved {Path.GetFileName(CurrentFilePath)}.";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = "Save failed: " + ex.Message;
            return false;
        }
    }

    public async Task<bool> SaveAsAsync(Window owner)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save project as",
            DefaultExtension = "mapsproj.json",
            InitialFileName = Path.GetFileName(CurrentFilePath) ?? "untitled.mapsproj.json",
            Filters = new List<FileDialogFilter>
            {
                new FileDialogFilter { Name = "MapSlopper project", Extensions = { "json" } },
            },
        };
        var path = await dlg.ShowAsync(owner).ConfigureAwait(true);
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            ProjectJsonIo.Save(_project, path!);
            CurrentFilePath = path;
            IsDirty = false;
            StatusMessage = $"Saved {Path.GetFileName(path)}.";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = "Save failed: " + ex.Message;
            return false;
        }
    }

    public async Task ExportMapAsync(Window owner)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export Quake .map",
            DefaultExtension = "map",
            InitialFileName = Path.GetFileNameWithoutExtension(CurrentFilePath ?? "untitled") + ".map",
            Filters = new List<FileDialogFilter> { new FileDialogFilter { Name = "Quake .map", Extensions = { "map" } } },
        };
        var path = await dlg.ShowAsync(owner).ConfigureAwait(true);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var result = GeometryGenerator.Generate(_project);
            if (!result.Ok || result.Document is null)
            {
                var msg = result.Issues.Count > 0 ? result.Issues[0].Message : "unknown error";
                StatusMessage = "Export failed: " + msg;
                return;
            }
            MapWriter.WriteToFile(result.Document, path!);
            if (result.Issues.Count > 0)
            {
                StatusMessage = $"Exported {Path.GetFileName(path)} - WARNING: {result.Issues[0].Message}";
            }
            else
            {
                StatusMessage = $"Exported {Path.GetFileName(path)} ({result.Document.Worldspawn.Brushes.Count} brushes).";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Export failed: " + ex.Message;
        }
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

    /// <summary>
    /// Snap a world position to either an existing nearby outline point or to
    /// a quarter-cell-size grid. Used by point-creation tools.
    /// </summary>
    public Vec2 SnapWorld(Vec2 worldPos)
    {
        var radius = 8.0 / Math.Max(_pixelsPerWorldUnit, 1e-6);
        var hit = _project.Outline.PickPoint(worldPos, radius);
        if (hit is not null) return hit.Position;
        var step = _project.Heightmap.CellSize / 4.0;
        if (step <= 0) return worldPos;
        var sx = Math.Round(worldPos.X / step) * step;
        var sy = Math.Round(worldPos.Y / step) * step;
        return new Vec2(sx, sy);
    }

    /// <summary>
    /// Ensure the heightmap covers <paramref name="worldPos"/>. Grows the
    /// heightmap outward (never shrinks) when needed. Call before adding or
    /// moving a vertex so the polygon never lives outside the height grid.
    /// </summary>
    public void EnsureHeightmapCovers(Vec2 worldPos)
    {
        _project.Heightmap.GrowToInclude(worldPos, worldPos);
    }

    /// <summary>
    /// Snap a world position to the nearest whole-heightmap-cell location
    /// (origin-aligned). Used by the Move tool when SnapToGrid is on or
    /// Shift is held.
    /// </summary>
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
