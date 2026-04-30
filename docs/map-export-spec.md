# `.map` export specification

MapSlopper writes Quake 3 `.map` files via `MapWriter`. The output matches canonical
NetRadiant formatting and is consumed unmodified by `q3map2`.

## Document layout

```
// entity 0
{
"classname" "worldspawn"
// brush 0
{
( x y z ) ( x y z ) ( x y z ) <texture> <shiftS> <shiftT> <rotate> <scaleS> <scaleT> <contentFlags> <surfaceFlags> <value>
...
}
...
}
// entity 1
{
"classname" "info_player_start"
"origin" "x y z"
}
// entity 2
{
"classname" "light"
"origin" "x y z"
"light" "300"
}
...
```

## Formatting rules

- **`classname` is always emitted first** within an entity. All other key/value pairs follow in
  insertion order.
- **Numbers are written in `CultureInfo.InvariantCulture`.** Integer-valued doubles whose
  magnitude is `< 1e9` are emitted bare (no decimal point). All other values are emitted with
  exactly six decimal places (`0.000000`).
- **Non-finite values are rejected** — `NaN` and infinities throw `InvalidOperationException`.
- Plane points are wrapped as `( x y z )` with single spaces.
- One brush per `{ ... }` block, each preceded by `// brush N`. One entity per `{ ... }`
  block, each preceded by `// entity N`. Lines end with `\n` (LF).

## Worldspawn ordering

Entity 0 is always `worldspawn`. Brushes inside worldspawn are emitted in this order:

1. **Wall brushes** — one mitered prism per outline edge, walking the loop.
2. **Floor brushes** — per-cell, in heightmap row-major scan (y outer, x inner).
3. **Ceiling brushes** — per-cell, same scan order, mirrored above the interior.

This order is stable: identical inputs produce byte-identical `.map` output.

## `info_player_start`

Emitted as a separate entity after worldspawn. Origin is:

- `playerStartOverride` if the project specifies it, otherwise
- the polygon centroid in XY, with Z slightly above the floor at that cell.

## Light entities

Lights are scattered across the interior on a regular grid spaced every `lightSpacing` units
(default **800**). Each accepted candidate becomes:

```
{
"classname" "light"
"origin" "x y z"
"light" "<lightIntensity>"
}
```

Z for each light is `ceilingHeight - lightInsetFromCeiling`. Candidate cells outside the
outline polygon are skipped, guaranteeing every light sits in playable space.

## Determinism and diff-friendliness

- Stable entity / brush ordering.
- Invariant culture numerics — no locale drift between machines.
- Bare integers — no `.000000` noise on grid-aligned coords.
- LF line endings, single space separators, no trailing whitespace.

The result is meant to be checked into source control alongside the `.mapsproj.json` and
diffed sensibly.
