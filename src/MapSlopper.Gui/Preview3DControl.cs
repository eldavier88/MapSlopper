using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MapSlopper.Core.Assets;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Generation;
using MapSlopper.Core.Geometry;
using MapSlopper.Gui.Preview;
using CoreBrush = MapSlopper.Core.Brushes.Brush;

namespace MapSlopper.Gui;

/// <summary>
/// In-game-style 3D preview. Renders the generated map brushes via a tiny
/// software rasterizer with a real Z-buffer, near-plane clipping in
/// camera space, and perspective-correct texture mapping. Texture pixels
/// are loaded from the project's user-supplied asset roots
/// (directories or <c>.pk3</c> files), parsed via <see cref="AssetLibrary"/>
/// — so the user sees the actual game shaders/textures the same way they
/// would in spectator mode.
///
/// Camera is first-person (WASD strafes, QE elevates, mouse-look on
/// right-button drag, scroll adjusts move speed, F frames the level).
/// Geometry rebuilds 100 ms after edits; the visible framebuffer
/// re-renders every 16 ms regardless so the camera moves smoothly even
/// while edits are queued. When no asset roots are configured the preview
/// still works — faces fall back to a flat per-orientation tint, which
/// also kicks in for shaders the loaded roots don't provide.
/// </summary>
public sealed class Preview3DControl : Control
{
    private EditorViewModel? _vm;
    private readonly DispatcherTimer _rebuildTimer;
    private readonly DispatcherTimer _renderTimer;

    // Camera
    private Vec3 _camPos = new(-200, -200, 200);
    private Vec3 _camVel = Vec3.Zero;
    private double _yaw = 0.6;
    private double _pitch = -0.4;
    private double _moveSpeed = 200.0;
    private const double FovY = Math.PI / 3;
    private const double NearZ = 1.0;
    private const double Damping = 8.0;

    // Input state
    private readonly HashSet<Key> _keysDown = new();
    private bool _looking;
    private Point _lastPointer;
    private DateTime _lastTick = DateTime.UtcNow;

    // Cached triangulated faces from the most recent project rebuild.
    private readonly List<Face> _faces = new();
    private string _statusLine = "3D preview \u2014 close a polygon to see geometry.";
    private string _assetStatus = "no assets";
    private bool _hasAutoFramed;
    private (Vec3 min, Vec3 max)? _bounds;

    // Asset pipeline
    private AssetLibrary _assets = AssetLibrary.Load(Array.Empty<string>());
    private TextureCache _textures;
    private readonly List<string> _lastAssetRoots = new();

    // Rasterizer + framebuffer surface.
    private readonly SoftwareRasterizer _raster = new();
    // Internal render target. We render at the control's pixel size,
    // capped to keep CPU cost bounded; the framebuffer is then stretched
    // 1:1 (or up-scaled) by Avalonia when DrawImage is called.
    private const int MaxRenderWidth = 1280;
    private const int MaxRenderHeight = 800;
    private WriteableBitmap? _backbuffer;
    private int _bbW, _bbH;

    public Preview3DControl()
    {
        Focusable = true;
        ClipToBounds = true;
        _textures = new TextureCache(_assets);
        _rebuildTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, OnRebuildTick);
        _rebuildTimer.Stop();
        _renderTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, OnRenderTick);
        _renderTimer.Start();
    }

    public void Bind(EditorViewModel vm)
    {
        if (_vm is not null)
        {
            _vm.Project.Outline.Changed -= ScheduleRebuild;
            _vm.Project.Heightmap.Changed -= ScheduleRebuild;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = vm;
        _vm.Project.Outline.Changed += ScheduleRebuild;
        _vm.Project.Heightmap.Changed += ScheduleRebuild;
        _vm.PropertyChanged += OnVmPropertyChanged;
        ReloadAssetsIfChanged();
        ScheduleRebuild();
    }

    /// <summary>
    /// Re-pull asset roots, rebuild geometry, and invalidate the visual.
    /// Called by <see cref="MainWindow"/> when the 3D Preview tab becomes
    /// selected, so a control that was hidden when the user added an
    /// asset root or edited the heightmap still wakes up cleanly.
    /// </summary>
    public void ForceRefresh()
    {
        if (_vm is null) return;
        ReloadAssetsIfChanged();
        ScheduleRebuild();
        InvalidateVisual();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.Project))
        {
            if (_vm is null) return;
            _vm.Project.Outline.Changed += ScheduleRebuild;
            _vm.Project.Heightmap.Changed += ScheduleRebuild;
            ReloadAssetsIfChanged();
            ScheduleRebuild();
        }
    }

    /// <summary>
    /// Force the preview to re-scan its asset roots. Call after the user
    /// adds/removes a root from the View menu. Always appends the
    /// bundled MapSlopper baseq3 (shipped next to the exe) as a fallback
    /// so the default <c>random/*</c> shaders resolve without any user
    /// configuration.
    /// </summary>
    public void ReloadAssets()
    {
        if (_vm is null) return;
        var combined = AssetRootHelper.WithBundledFallback(_vm.Project.AssetRoots);
        _assets = AssetLibrary.Load(combined);
        _textures = new TextureCache(_assets);
        _lastAssetRoots.Clear();
        _lastAssetRoots.AddRange(_vm.Project.AssetRoots);
        var userCount = _vm.Project.AssetRoots.Count;
        var totalCount = _assets.RootDescriptions.Count;
        _assetStatus = userCount == 0
            ? $"bundled only ({_assets.ShaderCount} shaders)"
            : $"{userCount} user + {Math.Max(0, totalCount - userCount)} bundled root(s), {_assets.ShaderCount} shaders";
        InvalidateVisual();
    }

    /// <summary>
    /// Returns the current asset library so callers (e.g. the asset-add
    /// success dialog in the main window) can probe per-shader resolution.
    /// </summary>
    public AssetLibrary Assets => _assets;

    private void ReloadAssetsIfChanged()
    {
        if (_vm is null) return;
        var cur = _vm.Project.AssetRoots;
        var changed = cur.Count != _lastAssetRoots.Count;
        if (!changed)
        {
            for (var i = 0; i < cur.Count; i++)
            {
                if (!string.Equals(cur[i], _lastAssetRoots[i], StringComparison.OrdinalIgnoreCase))
                { changed = true; break; }
            }
        }
        if (changed) ReloadAssets();
    }

    private void ScheduleRebuild()
    {
        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    private void OnRebuildTick(object? sender, EventArgs e)
    {
        _rebuildTimer.Stop();
        if (_vm is null) return;
        try
        {
            _faces.Clear();
            _bounds = null;
            var result = GeometryGenerator.Generate(_vm.Project);
            if (result.Document is null)
            {
                _statusLine = result.Issues.Count > 0
                    ? "3D preview: " + result.Issues[0].Message
                    : "3D preview \u2014 close a polygon to see geometry.";
                return;
            }
            var brushCount = 0;
            Vec3 bMin = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
            Vec3 bMax = new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
            foreach (var entity in CollectAllBrushOwners(result.Document))
            {
                foreach (var brush in entity.Brushes)
                {
                    AddBrushFaces(brush);
                    brushCount++;
                    foreach (var p in brush.Planes)
                    {
                        AccumulateBounds(p.P1, ref bMin, ref bMax);
                        AccumulateBounds(p.P2, ref bMin, ref bMax);
                        AccumulateBounds(p.P3, ref bMin, ref bMax);
                    }
                }
            }
            if (brushCount > 0 && bMax.X > bMin.X)
            {
                _bounds = (bMin, bMax);
                if (!_hasAutoFramed)
                {
                    SpawnAtPlayerStart(result.Document, bMin, bMax);
                    _hasAutoFramed = true;
                }
            }
            _statusLine = brushCount == 0
                ? "3D preview: polygon closed but no brushes generated. Paint the heightmap to add floors."
                : $"{brushCount} brushes \u2022 {_faces.Count} tris \u2022 {_assetStatus}";
        }
        catch (Exception ex)
        {
            _statusLine = "3D preview error: " + ex.Message;
            _faces.Clear();
        }
    }

    private static void AccumulateBounds(Vec3 p, ref Vec3 min, ref Vec3 max)
    {
        if (p.X < min.X) min = new Vec3(p.X, min.Y, min.Z);
        if (p.Y < min.Y) min = new Vec3(min.X, p.Y, min.Z);
        if (p.Z < min.Z) min = new Vec3(min.X, min.Y, p.Z);
        if (p.X > max.X) max = new Vec3(p.X, max.Y, max.Z);
        if (p.Y > max.Y) max = new Vec3(max.X, p.Y, max.Z);
        if (p.Z > max.Z) max = new Vec3(max.X, max.Y, p.Z);
    }

    private void FrameBounds(Vec3 min, Vec3 max)
    {
        var center = (min + max) * 0.5;
        var size = max - min;
        var radius = Math.Max(size.X, Math.Max(size.Y, size.Z));
        if (radius < 1) radius = 256;
        var dist = radius * 1.5;
        var dir = new Vec3(-1, -1, 0).Normalized;
        _camPos = new Vec3(center.X + dir.X * dist, center.Y + dir.Y * dist, center.Z + radius * 0.6);
        var look = (center - _camPos).Normalized;
        _yaw = Math.Atan2(look.Y, look.X);
        _pitch = Math.Asin(Math.Clamp(look.Z, -1, 1));
        _moveSpeed = Math.Max(50, radius * 0.5);
    }

    public void FrameNow()
    {
        if (_bounds is { } b) FrameBounds(b.min, b.max);
    }

    private void SpawnAtPlayerStart(MapDocument doc, Vec3 bMin, Vec3 bMax)
    {
        Vec3? origin = null;
        double yawDeg = 0;
        foreach (var ent in doc.Entities)
        {
            if (!ent.Properties.TryGetValue("classname", out var cn)
                || cn != "info_player_start") continue;
            if (ent.Properties.TryGetValue("origin", out var ostr))
            {
                var parts = ostr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var ox)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var oy)
                    && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var oz))
                {
                    origin = new Vec3(ox, oy, oz);
                }
            }
            if (ent.Properties.TryGetValue("angle", out var astr)
                && double.TryParse(astr, NumberStyles.Float, CultureInfo.InvariantCulture, out var ang))
            {
                yawDeg = ang;
            }
            break;
        }
        if (origin is null) { FrameBounds(bMin, bMax); return; }
        _camPos = new Vec3(origin.Value.X, origin.Value.Y, origin.Value.Z + 32);
        _camVel = Vec3.Zero;
        _yaw = yawDeg * Math.PI / 180.0;
        _pitch = 0;
        var size = bMax - bMin;
        _moveSpeed = Math.Max(160, Math.Max(size.X, size.Y) * 0.4);
    }

    private static IEnumerable<MapEntity> CollectAllBrushOwners(MapDocument doc)
    {
        yield return doc.Worldspawn;
        foreach (var e in doc.Entities) yield return e;
    }

    private void AddBrushFaces(CoreBrush brush)
    {
        var built = BrushFaceBuilder.BuildFaces(brush);
        foreach (var f in built)
        {
            var verts = f.Verts;
            var uvs = f.Uvs;
            if (verts.Count < 3) continue;
            // Fan-triangulate. Convex brush faces are always convex so a
            // simple fan from vertex 0 is correct.
            for (var i = 1; i < verts.Count - 1; i++)
            {
                _faces.Add(new Face(
                    verts[0], verts[i], verts[i + 1],
                    uvs[0], uvs[i], uvs[i + 1],
                    f.TextureName, f.FallbackR, f.FallbackG, f.FallbackB));
            }
        }
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        UpdateCamera(dt);
        InvalidateVisual();
    }

    private void UpdateCamera(double dt)
    {
        if (dt <= 0) return;
        var forward = new Vec3(
            Math.Cos(_yaw) * Math.Cos(_pitch),
            Math.Sin(_yaw) * Math.Cos(_pitch),
            Math.Sin(_pitch));
        var right = new Vec3(Math.Sin(_yaw), -Math.Cos(_yaw), 0);
        var up = new Vec3(0, 0, 1);

        var input = Vec3.Zero;
        if (_keysDown.Contains(Key.W)) input += forward;
        if (_keysDown.Contains(Key.S)) input -= forward;
        if (_keysDown.Contains(Key.D)) input += right;
        if (_keysDown.Contains(Key.A)) input -= right;
        if (_keysDown.Contains(Key.E) || _keysDown.Contains(Key.Space)) input += up;
        if (_keysDown.Contains(Key.Q) || _keysDown.Contains(Key.LeftCtrl)) input -= up;
        var len = input.Length;
        if (len > 1e-6) input /= len;
        var target = input * _moveSpeed;

        var alpha = 1 - Math.Exp(-Damping * dt);
        _camVel += (target - _camVel) * alpha;
        _camPos += _camVel * dt;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F)
        {
            FrameNow();
            e.Handled = true;
            return;
        }
        _keysDown.Add(e.Key);
        Focus();
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _keysDown.Remove(e.Key);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var p = e.GetCurrentPoint(this);
        if (p.Properties.IsRightButtonPressed)
        {
            _looking = true;
            _lastPointer = p.Position;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _looking = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_looking) return;
        var p = e.GetPosition(this);
        var dx = p.X - _lastPointer.X;
        var dy = p.Y - _lastPointer.Y;
        _lastPointer = p;
        const double sensitivity = 0.005;
        _yaw -= dx * sensitivity;
        _pitch -= dy * sensitivity;
        if (_pitch > 1.5) _pitch = 1.5;
        if (_pitch < -1.5) _pitch = -1.5;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var factor = Math.Pow(1.2, e.Delta.Y);
        _moveSpeed = Math.Clamp(_moveSpeed * factor, 10, 4000);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        // Pick framebuffer size: native pixel size of the control, capped
        // to a sane maximum so resizing the window doesn't grind the CPU.
        var targetW = Math.Max(1, Math.Min(MaxRenderWidth, (int)bounds.Width));
        var targetH = Math.Max(1, Math.Min(MaxRenderHeight, (int)bounds.Height));
        EnsureBackbuffer(targetW, targetH);

        if (_faces.Count == 0)
        {
            DrawEmptyState(context, bounds);
            return;
        }

        // Camera basis.
        var forwardVec = new Vec3(
            Math.Cos(_yaw) * Math.Cos(_pitch),
            Math.Sin(_yaw) * Math.Cos(_pitch),
            Math.Sin(_pitch));
        var rightVec = new Vec3(Math.Sin(_yaw), -Math.Cos(_yaw), 0);
        var upVec = Vec3.Cross(rightVec, forwardVec);

        // Clear framebuffer (sky-blue-tinted background).
        _raster.Resize(_bbW, _bbH);
        _raster.Clear(0xFF14181Eu);

        var fovScale = 0.5 * _bbH / Math.Tan(FovY * 0.5);
        var halfW = _bbW * 0.5;
        var halfH = _bbH * 0.5;

        // Single hard-coded directional light. Keep the same direction the
        // old preview used so visual identity is preserved when assets are
        // missing. Two-sided lighting because BrushFaceBuilder's winding
        // isn't reliably outward.
        var light = new Vec3(0.3, 0.5, 0.8).Normalized;
        var trianglesDrawn = 0;

        // Allocate small buffers reused by clipper to avoid GC churn.
        Span<CamVtx> inBuf = stackalloc CamVtx[4];
        Span<CamVtx> outBuf = stackalloc CamVtx[8];
        // Reuse one projected-vertex buffer per face. Maximum is 8 because
        // a triangle clipped against one plane in camera space can produce
        // at most 4 vertices, but downstream clipping (none here) could
        // push it higher. Allocated once outside the foreach to avoid a
        // CA2014 stack-overflow risk on huge brush counts.
        Span<SoftwareRasterizer.Vertex> proj = stackalloc SoftwareRasterizer.Vertex[8];

        foreach (var face in _faces)
        {
            // Camera space.
            var ca = ToCamera(face.A, rightVec, upVec, forwardVec);
            var cb = ToCamera(face.B, rightVec, upVec, forwardVec);
            var cc = ToCamera(face.C, rightVec, upVec, forwardVec);

            // Trivial reject: all behind near.
            if (ca.Z < NearZ && cb.Z < NearZ && cc.Z < NearZ) continue;

            // Lambert (two-sided so we don't need to know face winding).
            var n = Vec3.Cross(face.B - face.A, face.C - face.A);
            var nLen = n.Length;
            if (nLen < 1e-9) continue;
            n /= nLen;
            var lambert = (float)(0.25 + 0.75 * Math.Abs(Vec3.Dot(n, light)));

            // Texture lookup (cached).
            var tex = _textures.Get(face.TextureName);
            if (tex is { IsEmissive: true }) lambert = 1.25f; // emissive override

            inBuf[0] = new CamVtx(ca, face.UvA);
            inBuf[1] = new CamVtx(cb, face.UvB);
            inBuf[2] = new CamVtx(cc, face.UvC);
            var inCount = 3;

            // Near-plane clip.
            inCount = ClipNear(inBuf, inCount, outBuf);
            if (inCount < 3) continue;

            // Project clipped polygon, fan-triangulate, rasterize.
            for (var i = 0; i < inCount; i++)
            {
                var v = outBuf[i];
                var invZ = 1.0 / v.Pos.Z;
                var sx = (float)(halfW + v.Pos.X * invZ * fovScale);
                var sy = (float)(halfH - v.Pos.Y * invZ * fovScale);
                proj[i] = new SoftwareRasterizer.Vertex(
                    sx, sy,
                    (float)invZ,
                    (float)(v.U * invZ),
                    (float)(v.V * invZ));
            }
            for (var i = 1; i < inCount - 1; i++)
            {
                _raster.DrawTriangle(
                    proj[0], proj[i], proj[i + 1],
                    tex?.Rgba, tex?.Width ?? 0, tex?.Height ?? 0,
                    lambert,
                    face.FallbackR, face.FallbackG, face.FallbackB);
                trianglesDrawn++;
            }
        }

        // Blit framebuffer to bitmap.
        BlitToBitmap();
        if (_backbuffer is not null)
        {
            context.DrawImage(_backbuffer, new Rect(0, 0, _bbW, _bbH), new Rect(bounds.Size));
        }

        // HUD.
        var hud = FormattedTextCompat.Make(
            $"pos {_camPos.X:0}, {_camPos.Y:0}, {_camPos.Z:0}   yaw {(_yaw * 180 / Math.PI):0}°   "
            + $"pitch {(_pitch * 180 / Math.PI):0}°   speed {_moveSpeed:0}/s   tris {trianglesDrawn}   "
            + $"fb {_bbW}x{_bbH}   {_assetStatus}",
            Typeface.Default, 12, Brushes.LightGray);
        context.DrawText(hud, new Point(8, bounds.Height - 20));
    }

    private void EnsureBackbuffer(int w, int h)
    {
        if (_backbuffer is not null && _bbW == w && _bbH == h) return;
        _backbuffer?.Dispose();
        _backbuffer = new WriteableBitmap(
            new PixelSize(w, h), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        _bbW = w;
        _bbH = h;
    }

    /// <summary>
    /// Render the "nothing to show yet" diagnostic card. Replaces the
    /// previous bare top-left text dump with a structured, centred panel
    /// listing exactly which preconditions failed (outline missing /
    /// polygon not closed / heightmap empty) and what the user should
    /// do next. Always renders the asset-library status + camera primer
    /// so the user never feels stuck.
    /// </summary>
    private void DrawEmptyState(DrawingContext context, Rect bounds)
    {
        // Subtle vertical gradient background — a clean dark canvas that
        // visually distinguishes "preview is alive but empty" from the
        // 2D editor's grid.
        var bg = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x12, 0x14, 0x1B), 0),
                new GradientStop(Color.FromRgb(0x06, 0x07, 0x0B), 1),
            },
        };
        context.FillRectangle(bg, new Rect(bounds.Size));

        var headlineColor = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEE));
        var bodyColor = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xB3));
        var muteColor = new SolidColorBrush(Color.FromRgb(0x6F, 0x6F, 0x7A));
        var accentColor = new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xEF));
        var goodColor = new SolidColorBrush(Color.FromRgb(0x3F, 0xCB, 0x8E));

        // Decide the headline + steps based on actual project state.
        string headline;
        string body;
        var steps = new List<(string Bullet, string Text, IBrush Color)>();
        if (_vm is null)
        {
            headline = "3D preview not ready";
            body = "Initializing the editor view-model. If this persists, restart MapSlopper.";
        }
        else
        {
            var graph = _vm.Project.Outline;
            var pointCount = graph.Points.Count;
            var edgeCount = graph.Edges.Count;
            var closed = _vm.IsClosedPolygon;

            if (pointCount == 0)
            {
                headline = "Draw an outline to see your map in 3D";
                body = "Switch to the 2D Editor tab and place at least three points to define the floor plan, then close the polygon by linking the last point back to the first.";
                steps.Add(("1.", "Pick the Add Point tool (key 1) and click in the 2D editor.", bodyColor));
                steps.Add(("2.", "Place at least three points, then connect the last to the first.", bodyColor));
                steps.Add(("3.", "Pick the Height Brush (key 7) and paint floor cells.", bodyColor));
            }
            else if (!closed)
            {
                headline = $"Outline open ({pointCount} points, {edgeCount} edges)";
                body = "The polygon must be closed (every point on a single ring) before MapSlopper can generate brushes. Use the Connect tool (key 5) to link the last point back to the first.";
                steps.Add(("→", "Switch to the 2D Editor tab.", accentColor));
                steps.Add(("→", "Use the Connect tool (key 5) to close the loop.", accentColor));
            }
            else
            {
                headline = "Polygon closed — paint the heightmap";
                body = "The outline is leak-free, but no floor cells have been painted yet so no brushes were generated. Pick the Height Brush (key 7) and paint at least one cell inside the polygon.";
                steps.Add(("→", "Pick the Height Brush (key 7).", accentColor));
                steps.Add(("→", "Click and drag inside the polygon on the 2D Editor.", accentColor));
            }

            steps.Add(("✓", $"Asset library: {_assetStatus}",
                _assets.ShaderCount > 0 ? goodColor : muteColor));

            if (!string.IsNullOrEmpty(_statusLine)
                && !_statusLine.StartsWith("3D preview", StringComparison.OrdinalIgnoreCase))
            {
                steps.Add(("!", _statusLine, muteColor));
            }
        }

        // Layout the card centred horizontally, top-aligned with margin.
        const double cardW = 520;
        var cardX = Math.Max(20, (bounds.Width - cardW) / 2);
        var cardY = Math.Min(80, bounds.Height * 0.1);

        // Headline.
        var headlineFt = FormattedTextCompat.Make(
            headline,
            new Typeface("Segoe UI", FontStyle.Normal, FontWeight.SemiBold),
            22, headlineColor, TextAlignment.Left, cardW);
        context.DrawText(headlineFt, new Point(cardX, cardY));

        // Body paragraph.
        var bodyFt = FormattedTextCompat.Make(
            body, new Typeface("Segoe UI"),
            13, bodyColor, TextAlignment.Left, cardW);
        context.DrawText(bodyFt, new Point(cardX, cardY + 38));

        // Steps list.
        var stepY = cardY + 38 + bodyFt.Height + 18;
        foreach (var (bullet, text, color) in steps)
        {
            var bulletFt = FormattedTextCompat.Make(
                bullet,
                new Typeface("Segoe UI", FontStyle.Normal, FontWeight.Bold),
                13, color, TextAlignment.Center);
            context.DrawText(bulletFt, new Point(cardX, stepY));

            var textFt = FormattedTextCompat.Make(
                text, new Typeface("Segoe UI"),
                13, color, TextAlignment.Left, cardW - 28);
            context.DrawText(textFt, new Point(cardX + 24, stepY));
            stepY += Math.Max(20, textFt.Height + 6);
        }

        // Footer: camera controls primer (always shown, low contrast).
        var primer = "Camera: right-drag look • WASD move • QE up/down • F frame • scroll = speed";
        var primerFt = FormattedTextCompat.Make(
            primer, new Typeface("Segoe UI"),
            11, muteColor);
        context.DrawText(primerFt, new Point(12, bounds.Height - 22));
    }

    private void BlitToBitmap()
    {
        if (_backbuffer is null) return;
        using var fb = _backbuffer.Lock();
        var src = _raster.Color;
        var stride = fb.RowBytes;
        var addr = fb.Address;
        unsafe
        {
            // If row stride matches packed width*4, we can copy in one
            // shot; otherwise copy row by row to respect the bitmap's
            // stride. Marshal.Copy(uint[]) doesn't exist, so reinterpret
            // via fixed pointer.
            fixed (uint* srcPtr = src)
            {
                var srcBytes = (byte*)srcPtr;
                var dstBytes = (byte*)addr.ToPointer();
                var rowBytes = _bbW * 4;
                if (stride == rowBytes)
                {
                    Buffer.MemoryCopy(srcBytes, dstBytes, (long)stride * _bbH, (long)rowBytes * _bbH);
                }
                else
                {
                    for (var y = 0; y < _bbH; y++)
                    {
                        Buffer.MemoryCopy(
                            srcBytes + y * rowBytes,
                            dstBytes + y * stride,
                            stride,
                            rowBytes);
                    }
                }
            }
        }
    }

    private Vec3 ToCamera(Vec3 world, Vec3 rightVec, Vec3 upVec, Vec3 forwardVec)
    {
        var rel = world - _camPos;
        return new Vec3(
            Vec3.Dot(rel, rightVec),
            Vec3.Dot(rel, upVec),
            Vec3.Dot(rel, forwardVec));
    }

    /// <summary>
    /// Sutherland-Hodgman against the near-Z plane in camera space.
    /// Output polygon written into <paramref name="output"/>; returns the
    /// new vertex count. Input is replaced with output's contents (caller
    /// uses out polygon directly).
    /// </summary>
    private static int ClipNear(Span<CamVtx> input, int inCount, Span<CamVtx> output)
    {
        var n = 0;
        for (var i = 0; i < inCount; i++)
        {
            var curr = input[i];
            var prev = input[(i + inCount - 1) % inCount];
            var currInside = curr.Pos.Z >= NearZ;
            var prevInside = prev.Pos.Z >= NearZ;
            if (prevInside ^ currInside)
            {
                var t = (NearZ - prev.Pos.Z) / (curr.Pos.Z - prev.Pos.Z);
                output[n++] = Lerp(prev, curr, t);
            }
            if (currInside)
            {
                output[n++] = curr;
            }
        }
        // Caller reads from output; copy back into input to keep API
        // simple isn't necessary because the rasterize loop reads outBuf.
        return n;
    }

    private static CamVtx Lerp(CamVtx a, CamVtx b, double t) =>
        new(
            new Vec3(
                a.Pos.X + (b.Pos.X - a.Pos.X) * t,
                a.Pos.Y + (b.Pos.Y - a.Pos.Y) * t,
                a.Pos.Z + (b.Pos.Z - a.Pos.Z) * t),
            a.U + (b.U - a.U) * t,
            a.V + (b.V - a.V) * t);

    private readonly struct CamVtx
    {
        public readonly Vec3 Pos;
        public readonly double U, V;
        public CamVtx(Vec3 pos, double u, double v) { Pos = pos; U = u; V = v; }
        public CamVtx(Vec3 pos, (double U, double V) uv) { Pos = pos; U = uv.U; V = uv.V; }
    }

    private readonly struct Face
    {
        public readonly Vec3 A, B, C;
        public readonly (double U, double V) UvA, UvB, UvC;
        public readonly string TextureName;
        public readonly byte FallbackR, FallbackG, FallbackB;
        public Face(Vec3 a, Vec3 b, Vec3 c,
            (double U, double V) ua, (double U, double V) ub, (double U, double V) uc,
            string tex, byte r, byte g, byte bC)
        {
            A = a; B = b; C = c; UvA = ua; UvB = ub; UvC = uc;
            TextureName = tex; FallbackR = r; FallbackG = g; FallbackB = bC;
        }
    }
}
