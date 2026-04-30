using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using MapSlopper.Core.Brushes;
using MapSlopper.Core.Generation;
using MapSlopper.Core.Geometry;
using CoreBrush = MapSlopper.Core.Brushes.Brush;

namespace MapSlopper.Gui;

/// <summary>
/// Software-rasterized 3D preview of the generated brushes. Uses a perspective
/// projection with painter's-algorithm depth sort and per-face Lambertian
/// shading. Camera is first-person: WASD strafes, QE elevates, mouse-look on
/// right-button drag, scroll adjusts move speed. Camera state persists for the
/// lifetime of the control instance.
///
/// Rebuilds geometry from the project on a 100 ms debounce so editing in the
/// 2D pane updates the preview without thrashing the generator on every
/// stroke.
/// </summary>
public sealed class Preview3DControl : Control
{
    private EditorViewModel? _vm;
    private readonly DispatcherTimer _rebuildTimer;
    private readonly DispatcherTimer _renderTimer;

    // Camera
    private Vec3 _camPos = new(-200, -200, 200);
    private Vec3 _camVel = Vec3.Zero;
    private double _yaw = 0.6;     // radians, around world Z (up)
    private double _pitch = -0.4;  // radians, 0 = horizontal
    private double _moveSpeed = 200.0;
    private const double FovY = Math.PI / 3; // 60°
    private const double NearZ = 1.0;
    /// <summary>Acceleration toward target velocity (1/s). Larger = snappier.</summary>
    private const double Damping = 8.0;

    // Input state
    private readonly HashSet<Key> _keysDown = new();
    private bool _looking;
    private Point _lastPointer;
    private DateTime _lastTick = DateTime.UtcNow;

    // Cached triangulated faces from the most recent project rebuild.
    private readonly List<Face> _faces = new();
    private string _statusLine = "3D preview \u2014 close a polygon to see geometry.";
    private bool _hasAutoFramed;
    private (Vec3 min, Vec3 max)? _bounds;

    public Preview3DControl()
    {
        Focusable = true;
        ClipToBounds = true;
        _rebuildTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, OnRebuildTick);
        _rebuildTimer.Stop();
        _renderTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, OnRenderTick);
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
        ScheduleRebuild();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.Project))
        {
            // Project replaced (after Open/New). Re-subscribe.
            if (_vm is null) return;
            _vm.Project.Outline.Changed += ScheduleRebuild;
            _vm.Project.Heightmap.Changed += ScheduleRebuild;
            ScheduleRebuild();
        }
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
                ? "3D preview: polygon closed but no brushes generated. Paint the heightmap to add floors, or check warnings on Export."
                : $"{brushCount} brushes \u2022 {_faces.Count} tris";
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
        // Place camera back along -X-Y diagonal at ~1.5x radius, slightly elevated.
        var dist = radius * 1.5;
        var dir = new Vec3(-1, -1, 0).Normalized;
        _camPos = new Vec3(center.X + dir.X * dist, center.Y + dir.Y * dist, center.Z + radius * 0.6);
        // Look toward center.
        var look = (center - _camPos).Normalized;
        _yaw = Math.Atan2(look.Y, look.X);
        _pitch = Math.Asin(Math.Clamp(look.Z, -1, 1));
        _moveSpeed = Math.Max(50, radius * 0.5);
    }

    public void FrameNow()
    {
        if (_bounds is { } b) FrameBounds(b.min, b.max);
    }

    /// <summary>
    /// Spawn the camera *inside* the level at the info_player_start origin
    /// (eye height ~ 56) facing roughly toward the polygon centroid.
    /// Falls back to FrameBounds when no player_start is found.
    /// </summary>
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
                    && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ox)
                    && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var oy)
                    && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var oz))
                {
                    origin = new Vec3(ox, oy, oz);
                }
            }
            if (ent.Properties.TryGetValue("angle", out var astr)
                && double.TryParse(astr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ang))
            {
                yawDeg = ang;
            }
            break;
        }
        if (origin is null) { FrameBounds(bMin, bMax); return; }
        // Eye height: Q3 player_start is at feet level + 24 by our placement.
        // Add 32 more to roughly match a 56-unit eye height.
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
        // For preview we triangulate each plane's intersection polygon with the brush.
        // This is identical to BSP face generation: each face = the plane's polygon
        // bounded by all OTHER planes' half-spaces. We compute it here once at rebuild.
        var poly = BrushFaceBuilder.BuildFace(brush);
        foreach (var (verts, color) in poly)
        {
            if (verts.Count < 3) continue;
            for (var i = 1; i < verts.Count - 1; i++)
                _faces.Add(new Face(verts[0], verts[i], verts[i + 1], color));
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
        // Camera-relative basis: forward follows pitch so looking down + W
        // dives down. Right is purely horizontal so strafing stays level.
        var forward = new Vec3(
            Math.Cos(_yaw) * Math.Cos(_pitch),
            Math.Sin(_yaw) * Math.Cos(_pitch),
            Math.Sin(_pitch));
        var right = new Vec3(Math.Sin(_yaw), -Math.Cos(_yaw), 0);
        var up = new Vec3(0, 0, 1);

        // Build target velocity from input (accumulated, normalised so
        // diagonal isn't faster).
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

        // Exponential smoothing toward target velocity (spaceship drift feel).
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
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(20, 24, 32)), new Rect(bounds.Size));

        if (_faces.Count == 0)
        {
            var fmt = new FormattedText(
                _statusLine + "\nRight-drag = look, WASD = move, QE = up/down, F = frame, scroll = speed",
                Typeface.Default, 14, TextAlignment.Left, TextWrapping.Wrap, new Size(bounds.Width - 40, bounds.Height));
            context.DrawText(Brushes.LightGray, new Point(20, 20), fmt);
            return;
        }

        // World→camera transform.
        var cosYaw = Math.Cos(-_yaw);
        var sinYaw = Math.Sin(-_yaw);
        var cosPitch = Math.Cos(-_pitch);
        var sinPitch = Math.Sin(-_pitch);
        var fovScale = 0.5 * bounds.Height / Math.Tan(FovY * 0.5);

        // Project all faces, depth-sort back-to-front, draw filled with simple shading.
        var projected = new List<(Point P1, Point P2, Point P3, double Depth, Color Color)>(_faces.Count);
        foreach (var face in _faces)
        {
            if (!ProjectVertex(face.A, cosYaw, sinYaw, cosPitch, sinPitch, fovScale, bounds, out var a)) continue;
            if (!ProjectVertex(face.B, cosYaw, sinYaw, cosPitch, sinPitch, fovScale, bounds, out var b)) continue;
            if (!ProjectVertex(face.C, cosYaw, sinYaw, cosPitch, sinPitch, fovScale, bounds, out var c)) continue;

            // Back-face cull using projected winding (we want CCW = front-facing).
            var cross2 = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (cross2 <= 0) continue;

            // Average camera-space depth for sorting.
            var depthA = ToCameraSpace(face.A, cosYaw, sinYaw, cosPitch, sinPitch).X;
            var depthB = ToCameraSpace(face.B, cosYaw, sinYaw, cosPitch, sinPitch).X;
            var depthC = ToCameraSpace(face.C, cosYaw, sinYaw, cosPitch, sinPitch).X;
            var avgDepth = (depthA + depthB + depthC) / 3.0;

            // Lambert shading with a fixed light direction.
            var n = Vec3.Cross(face.B - face.A, face.C - face.A);
            var nLen = n.Length;
            if (nLen < 1e-9) continue;
            n /= nLen;
            var light = new Vec3(0.3, 0.5, 0.8);
            var lightLen = light.Length;
            light /= lightLen;
            var diffuse = Math.Max(0.15, Vec3.Dot(n, light));
            var shaded = Color.FromRgb(
                (byte)Math.Clamp(face.Color.R * diffuse, 0, 255),
                (byte)Math.Clamp(face.Color.G * diffuse, 0, 255),
                (byte)Math.Clamp(face.Color.B * diffuse, 0, 255));

            projected.Add((a, b, c, avgDepth, shaded));
        }

        projected.Sort((x, y) => y.Depth.CompareTo(x.Depth));
        var edgePen = new Pen(new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), 0.5);
        foreach (var (p1, p2, p3, _, color) in projected)
        {
            var fill = new SolidColorBrush(color);
            var fig = new PathFigure { StartPoint = p1, IsClosed = true };
            fig.Segments!.Add(new LineSegment { Point = p2 });
            fig.Segments!.Add(new LineSegment { Point = p3 });
            var geo = new PathGeometry();
            geo.Figures!.Add(fig);
            context.DrawGeometry(fill, edgePen, geo);
        }

        // HUD
        var hud = new FormattedText(
            $"pos {_camPos.X:0}, {_camPos.Y:0}, {_camPos.Z:0}   yaw {(_yaw * 180 / Math.PI):0}°   pitch {(_pitch * 180 / Math.PI):0}°   speed {_moveSpeed:0}/s   tris {projected.Count}",
            Typeface.Default, 12, TextAlignment.Left, TextWrapping.NoWrap, bounds.Size);
        context.DrawText(Brushes.LightGray, new Point(8, bounds.Height - 20), hud);
    }

    private bool ProjectVertex(
        Vec3 world, double cosYaw, double sinYaw, double cosPitch, double sinPitch,
        double fovScale, Rect bounds, out Point screen)
    {
        var cam = ToCameraSpace(world, cosYaw, sinYaw, cosPitch, sinPitch);
        // Camera looks down +X. Z is up. Y is right.
        if (cam.X < NearZ)
        {
            screen = default;
            return false;
        }
        var sx = bounds.Width * 0.5 + (cam.Y / cam.X) * fovScale;
        var sy = bounds.Height * 0.5 - (cam.Z / cam.X) * fovScale;
        screen = new Point(sx, sy);
        return true;
    }

    private Vec3 ToCameraSpace(Vec3 world, double cosYaw, double sinYaw, double cosPitch, double sinPitch)
    {
        // 1. Translate by -camPos.
        var t = world - _camPos;
        // 2. Rotate by -yaw about Z (so camera forward becomes +X).
        var x1 = t.X * cosYaw - t.Y * sinYaw;
        var y1 = t.X * sinYaw + t.Y * cosYaw;
        // 3. Rotate by -pitch about Y (so camera looks down +X with pitch=0 horizontal).
        var x2 = x1 * cosPitch + t.Z * sinPitch;
        var z2 = -x1 * sinPitch + t.Z * cosPitch;
        return new Vec3(x2, y1, z2);
    }

    private readonly struct Face
    {
        public readonly Vec3 A, B, C;
        public readonly Color Color;
        public Face(Vec3 a, Vec3 b, Vec3 c, Color color) { A = a; B = b; C = c; Color = color; }
    }
}
