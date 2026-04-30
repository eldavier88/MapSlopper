# Algorithms

This note covers the four geometric algorithms behind MapSlopper's leak-free guarantee.

## 1. Ear-clip triangulation (`PolygonTriangulator`)

Used to triangulate the outline polygon for visualization and for sanity checks. Standard
O(n²) ear-clip on a CCW simple polygon: repeatedly find a vertex whose triangle with its two
neighbours is wholly inside the polygon and contains no other vertex, emit it, remove it, and
loop. Robust enough for hand-drawn floor plans where vertex counts are small (tens to low
hundreds).

## 2. Sutherland–Hodgman polygon clipping (`RectangleClipper`)

Used by the floor / ceiling generator to clip each heightmap **cell rectangle** against the
outline polygon. Sutherland–Hodgman is exact for convex clip regions, so the cell rectangle is
treated as the clip and the outline polygon is the subject — actually we run it the standard
way (subject = cell rect, clip = outline), iterating each outline edge as a half-plane and
keeping the inside portion. The result is the convex piece of that cell which lies inside the
outline.

Empty results (cell entirely outside) are dropped; full-rect results (cell entirely inside)
are emitted as-is for tight axis-aligned brushes.

## 3. Mitered prism wall extrusion (`WallGenerator`)

For each outline edge `a → b` (CCW), the outward normal is the **right perpendicular** of the
edge direction. The wall brush footprint is the quad

```
[ b, a, aOut, bOut ]
```

where `aOut` and `bOut` are `a` and `b` displaced along the **mitered** outward direction at
each vertex (the bisector of the two adjacent edge normals). This makes adjacent walls share
exact edges — no gaps, no overlaps, even at sharp corners.

To prevent runaway spikes at very acute interior angles, the miter length is clamped:

```
miterLength = wallThickness / max(sin(halfAngle), 1 / MaxMiterRatio)
MaxMiterRatio = 4
```

i.e. a wall corner can never extrude more than 4× `wallThickness`. The footprint is then
extruded vertically from `floorZ - epsilon` to `ceilingHeight + epsilon` to form a closed
convex prism.

## 4. Per-cell floor / ceiling brush generation (`FloorCeilingGenerator`)

For each cell `(i, j)` in the heightmap:

1. Form the cell rectangle `R = [originX + i·s, originY + j·s] × [+s, +s]`.
2. Clip `R` against the outline polygon (algorithm 2). Skip if empty.
3. Read the cell's height value `h` from `Heightmap16` and map it to world Z via
   `HeightmapLevels`.
4. Emit a **floor brush**: convex prism with the clipped polygon as footprint, top at `h`,
   bottom at `h - floorBaseThickness` (or down to a uniform slab base — see source).
5. Emit a **ceiling brush**: same footprint, top at `ceilingHeight + ceilingThickness`,
   bottom at `ceilingHeight`.

Because Sutherland–Hodgman against a simple polygon produces a convex (in fact polygonal)
result and the cell tiling is a partition of the bounding rect, the union of clipped cells
exactly equals the outline interior. There are no gaps and no overlaps.

## Leak-free guarantee

Combining the above:

- **Walls**: continuous mitered ring around the outline. Adjacent wall quads share exact
  edges. The ring is closed because the outline is closed.
- **Floor + ceiling**: the union of all clipped cells exactly tiles the interior polygon.
  Each cell brush is closed convex (prism over a convex footprint).
- **Wall ↔ floor / ceiling seam**: walls extrude from below the lowest floor to above the
  highest ceiling, with a small epsilon overlap, so q3map2 sees no gap at the join.

Every brush emitted is closed and convex, every interior face is covered, the exterior is
fully enclosed, and the small vertical epsilons on walls absorb any floating-point seam. The
result compiles in q3map2 without leaks.
