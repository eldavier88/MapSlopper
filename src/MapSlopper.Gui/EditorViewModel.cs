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
using MapSlopper.Core.Triggers;
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
    public sealed class AutoMapOptions
    {
        public int WidthCells { get; set; } = 96;
        public int HeightCells { get; set; } = 96;
        public int Complexity { get; set; } = 3;
        public ushort Relief { get; set; } = 192;
        public int? Seed { get; set; }
    }

    private MapSlopperProject _project;
    private IEditorTool _activeTool;
    private string? _currentFilePath;
    private bool _isDirty;
    private Guid? _selectedPointId;
    private string _statusMessage = "Ready.";
    private int _brushSizeCells = 1;
    private ushort _paintValue = 64;
    private byte _activeTriggerTypeId = 1;
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

    /// <summary>
    /// Currently selected trigger type id (1..255). Used by
    /// <see cref="TriggerBrushTool"/> as the value painted into the
    /// project's trigger layer. Defaults to the first id in
    /// <see cref="TriggerTypes"/>.
    /// </summary>
    public byte ActiveTriggerTypeId
    {
        get => _activeTriggerTypeId;
        set { if (_activeTriggerTypeId != value) { _activeTriggerTypeId = value; OnPropertyChanged(); RepaintRequested?.Invoke(); } }
    }

    /// <summary>
    /// Effective (program-wide + project-override) trigger types. Refreshed
    /// when the project is replaced; the GUI palette binds to this list.
    /// </summary>
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

    public void GenerateAutoMap(AutoMapOptions? options = null)
    {
        var o = options ?? new AutoMapOptions();
        var result = AutoMapGenerator.Generate(_project, new AutoMapGenerator.Options
        {
            WidthCells = o.WidthCells,
            HeightCells = o.HeightCells,
            CellSize = _project.Heightmap.CellSize,
            Complexity = o.Complexity,
            Relief = o.Relief,
            Seed = o.Seed,
        });

        Project = result.Project;
        Undo.Clear();
        CurrentFilePath = null;
        IsDirty = true;
        SelectedPointId = null;
        _activeTool.Reset();
        StatusMessage = $"Auto-map generated (seed {result.SeedUsed}, attempt {result.Attempts}).";
    }

    /// <summary>
    /// Open a project via file picker, then route through
    /// <see cref="OpenPathAsync"/> so the path is recorded in the
    /// <see cref="RecentFiles"/> MRU list.
    /// </summary>
    public async Task OpenAsync(Window owner)
    {
        // Avalonia 11 deprecated OpenFileDialog / SaveFileDialog in favour
        // of the IStorageProvider API on TopLevel/Window. The new picker
        // returns IStorageFile objects whose Path is a file:// URI;
        // .LocalPath on that gives us back a real filesystem path.
        var files = await owner.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Open project",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("MapSlopper project")
                    {
                        Patterns = new[] { "*.json" },
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("All files")
                    {
                        Patterns = new[] { "*" },
                    },
                },
            }).ConfigureAwait(true);
        if (files is null || files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        if (string.IsNullOrEmpty(path)) return;
        OpenPath(path);
    }

    /// <summary>
    /// Load a project from <paramref name="path"/> directly (no picker).
    /// Used by drag-drop, the Welcome dialog's "Recent" buttons, and the
    /// <c>File &gt; Open Recent</c> menu. Logs the path to the MRU list
    /// on success and removes it on failure (so a deleted file doesn't
    /// linger in the menu).
    /// </summary>
    public void OpenPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var loaded = ProjectJsonIo.Load(path);
            Project = loaded;
            Undo.Clear();
            CurrentFilePath = path;
            IsDirty = false;
            SelectedPointId = null;
            _activeTool.Reset();
            StatusMessage = $"Opened {Path.GetFileName(path)}.";
            RecentFilesChanged?.Invoke(path, true);
        }
        catch (Exception ex)
        {
            StatusMessage = "Open failed: " + ex.Message;
            RecentFilesChanged?.Invoke(path, false);
        }
    }

    /// <summary>
    /// Raised whenever a path was opened (added=true) or failed to open
    /// (added=false). The main window subscribes and updates the
    /// <see cref="RecentFiles"/> store accordingly.
    /// </summary>
    public event Action<string, bool>? RecentFilesChanged;

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
        var file = await owner.StorageProvider.SaveFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save project as",
                DefaultExtension = "json",
                SuggestedFileName = Path.GetFileName(CurrentFilePath) ?? "untitled.mapsproj.json",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("MapSlopper project")
                    {
                        Patterns = new[] { "*.json" },
                    },
                },
            }).ConfigureAwait(true);
        if (file is null) return false;
        var path = file.Path.LocalPath;
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            ProjectJsonIo.Save(_project, path);
            CurrentFilePath = path;
            IsDirty = false;
            StatusMessage = $"Saved {Path.GetFileName(path)}.";
            RecentFilesChanged?.Invoke(path, true);
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
        var file = await owner.StorageProvider.SaveFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Export Quake .map",
                DefaultExtension = "map",
                SuggestedFileName = Path.GetFileNameWithoutExtension(CurrentFilePath ?? "untitled") + ".map",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Quake .map")
                    {
                        Patterns = new[] { "*.map" },
                    },
                },
            }).ConfigureAwait(true);
        if (file is null) return;
        var path = file.Path.LocalPath;
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
            MapWriter.WriteToFile(result.Document, path);
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

        if (ShouldSnapToGrid) return SnapWorldToCell(worldPos);

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
        SyncTriggerLayerToHeightmap();
        _project.Outline.Changed += OnGraphChanged;
        _project.Heightmap.Changed += OnGraphChanged;
        _project.Heightmap.Changed += OnHeightmapChangedSyncTriggers;
        _project.TriggerLayer.Changed += OnGraphChanged;
        TriggerTypes = ResolveTriggerTypes();
        OnPropertyChanged(nameof(TriggerTypes));
        // Keep ActiveTriggerTypeId valid for the freshly-resolved list.
        if (TriggerTypes.FindById(_activeTriggerTypeId) is null && TriggerTypes.Types.Count > 0)
        {
            _activeTriggerTypeId = TriggerTypes.Types[0].Id;
            OnPropertyChanged(nameof(ActiveTriggerTypeId));
        }
    }

    private void UnwireProject()
    {
        _project.Outline.Changed -= OnGraphChanged;
        _project.Heightmap.Changed -= OnGraphChanged;
        _project.Heightmap.Changed -= OnHeightmapChangedSyncTriggers;
        _project.TriggerLayer.Changed -= OnGraphChanged;
    }

    /// <summary>
    /// Trigger layer must share dimensions / origin / cell size with the
    /// heightmap so painting and generation use identical cell coordinates.
    /// Called on project replace and after the heightmap grows.
    /// </summary>
    private void SyncTriggerLayerToHeightmap()
    {
        var hm = _project.Heightmap;
        var tl = _project.TriggerLayer;
        if (tl.Width == hm.Width && tl.Height == hm.Height
            && Math.Abs(tl.CellSize - hm.CellSize) < 1e-9
            && Math.Abs(tl.Origin.X - hm.Origin.X) < 1e-9
            && Math.Abs(tl.Origin.Y - hm.Origin.Y) < 1e-9)
        {
            return;
        }
        // Build a fresh layer at the heightmap's geometry, copying any
        // overlapping painted cells across so existing trigger paint isn't
        // wiped when the heightmap auto-grows on outline edits.
        var fresh = new Heightmap16(hm.Width, hm.Height, hm.CellSize, hm.Origin);
        var dx = (int)Math.Round((tl.Origin.X - hm.Origin.X) / hm.CellSize);
        var dy = (int)Math.Round((tl.Origin.Y - hm.Origin.Y) / hm.CellSize);
        for (var y = 0; y < tl.Height; y++)
        {
            var ny = y + dy;
            if (ny < 0 || ny >= fresh.Height) continue;
            for (var x = 0; x < tl.Width; x++)
            {
                var nx = x + dx;
                if (nx < 0 || nx >= fresh.Width) continue;
                var v = tl.Data[y * tl.Width + x];
                if (v != 0) fresh.Data[ny * fresh.Width + nx] = v;
            }
        }
        _project.TriggerLayer = fresh;
    }

    private void OnHeightmapChangedSyncTriggers()
    {
        // Cheap geometry-equality check; SyncTriggerLayerToHeightmap is a
        // no-op if dimensions already match, so calling it on every
        // heightmap mutation is fine.
        var hm = _project.Heightmap;
        var tl = _project.TriggerLayer;
        if (tl.Width == hm.Width && tl.Height == hm.Height
            && Math.Abs(tl.Origin.X - hm.Origin.X) < 1e-9
            && Math.Abs(tl.Origin.Y - hm.Origin.Y) < 1e-9)
        {
            return;
        }
        // Subscribe newly-replaced trigger layer (the previous one's event
        // handler was attached to the old reference).
        _project.TriggerLayer.Changed -= OnGraphChanged;
        SyncTriggerLayerToHeightmap();
        _project.TriggerLayer.Changed += OnGraphChanged;
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
