# `.mapsproj.json` project format

MapSlopper projects are stored as UTF-8 JSON written by `ProjectJsonIo`. Property names are
camelCase. Trailing commas and `//` / `/* */` comments are tolerated on read but never written.

## Top-level fields (`MapSlopperProject`)

| Field                  | Type        | Default          | Description                                                                  |
| ---------------------- | ----------- | ---------------- | ---------------------------------------------------------------------------- |
| `formatVersion`        | int         | `1`              | Document schema version. Currently always `1`.                               |
| `outline`              | object      | empty            | Outline graph: points (with GUID ids) and edges connecting them.             |
| `heightmap`            | object      | 64×64 @ cell 32  | `Heightmap16`: width, height, cellSize, originX, originY, dataBase64.        |
| `ceilingHeight`        | number      | `256`            | World Z of the interior ceiling top, in game units.                          |
| `wallThickness`        | number      | `16`             | Outward thickness of the exterior wall band.                                 |
| `floorTexture`         | string      | `common/caulk`   | Shader name applied to floor brush top faces (and sides).                    |
| `wallTexture`          | string      | `common/caulk`   | Shader name applied to wall brush faces.                                     |
| `ceilingTexture`       | string      | `common/caulk`   | Shader name applied to ceiling brush bottom faces (and sides).               |
| `playerStartOverride`  | object?     | `null`           | Optional `{x,y,z}`. If set, used as `info_player_start` origin.              |
| `lightSpacing`         | number      | `800`            | Approximate spacing between auto-placed light entities, game units.          |
| `lightIntensity`       | number      | `300`            | `light` key value on emitted light entities.                                 |
| `lightInsetFromCeiling`| number      | `16`             | Vertical inset of lights below the ceiling.                                  |
| `ceilingThickness`     | number      | `16`             | Vertical thickness of generated ceiling brushes.                             |
| `floorBaseThickness`   | number      | `16`             | Thickness of the floor "slab" extending downward below the cell top.         |

### `outline`

```json
{
  "points": [
    { "id": "<guid>", "x": 0, "y": 0 }
  ],
  "edges": [
    { "a": "<guid>", "b": "<guid>" }
  ]
}
```

Points carry stable GUIDs so edges can reference them. **Outlines must be a single closed
counter-clockwise loop** for the wall generator's right-perpendicular outward-normal convention.

### `heightmap`

```json
{
  "width": 64,
  "height": 64,
  "cellSize": 32,
  "originX": 0,
  "originY": 0,
  "dataBase64": "<base64 of width*height little-endian uint16>"
}
```

`dataBase64` decodes to `width * height * 2` bytes. Each pair is one `ushort` (little-endian)
holding that cell's floor-height index, mapped into world Z by `HeightmapLevels`.

## Minimal example

```json
{
  "formatVersion": 1,
  "outline": {
    "points": [
      { "id": "11111111-1111-1111-1111-111111111111", "x": 0, "y": 0 },
      { "id": "22222222-2222-2222-2222-222222222222", "x": 128, "y": 0 },
      { "id": "33333333-3333-3333-3333-333333333333", "x": 128, "y": 128 },
      { "id": "44444444-4444-4444-4444-444444444444", "x": 0, "y": 128 }
    ],
    "edges": [
      { "a": "11111111-1111-1111-1111-111111111111", "b": "22222222-2222-2222-2222-222222222222" },
      { "a": "22222222-2222-2222-2222-222222222222", "b": "33333333-3333-3333-3333-333333333333" },
      { "a": "33333333-3333-3333-3333-333333333333", "b": "44444444-4444-4444-4444-444444444444" },
      { "a": "44444444-4444-4444-4444-444444444444", "b": "11111111-1111-1111-1111-111111111111" }
    ]
  },
  "heightmap": {
    "width": 4,
    "height": 4,
    "cellSize": 32,
    "originX": 0,
    "originY": 0,
    "dataBase64": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
  }
}
```

## Complete example

A 6-point L-shaped outline with all parameters set explicitly. The 16×16 heightmap is all
zeros (`682` 'A' chars + `==`).

```json
{
  "formatVersion": 1,
  "ceilingHeight": 256,
  "wallThickness": 16,
  "ceilingThickness": 16,
  "floorBaseThickness": 16,
  "floorTexture": "common/caulk",
  "wallTexture": "common/caulk",
  "ceilingTexture": "common/caulk",
  "lightSpacing": 512,
  "lightIntensity": 300,
  "lightInsetFromCeiling": 16,
  "outline": {
    "points": [
      { "id": "00000000-0000-0000-0000-000000000001", "x": 0,   "y": 0   },
      { "id": "00000000-0000-0000-0000-000000000002", "x": 512, "y": 0   },
      { "id": "00000000-0000-0000-0000-000000000003", "x": 512, "y": 256 },
      { "id": "00000000-0000-0000-0000-000000000004", "x": 256, "y": 256 },
      { "id": "00000000-0000-0000-0000-000000000005", "x": 256, "y": 512 },
      { "id": "00000000-0000-0000-0000-000000000006", "x": 0,   "y": 512 }
    ],
    "edges": [
      { "a": "00000000-0000-0000-0000-000000000001", "b": "00000000-0000-0000-0000-000000000002" },
      { "a": "00000000-0000-0000-0000-000000000002", "b": "00000000-0000-0000-0000-000000000003" },
      { "a": "00000000-0000-0000-0000-000000000003", "b": "00000000-0000-0000-0000-000000000004" },
      { "a": "00000000-0000-0000-0000-000000000004", "b": "00000000-0000-0000-0000-000000000005" },
      { "a": "00000000-0000-0000-0000-000000000005", "b": "00000000-0000-0000-0000-000000000006" },
      { "a": "00000000-0000-0000-0000-000000000006", "b": "00000000-0000-0000-0000-000000000001" }
    ]
  },
  "heightmap": {
    "width": 16,
    "height": 16,
    "cellSize": 32,
    "originX": 0,
    "originY": 0,
    "dataBase64": "<512 zero bytes in base64>"
  }
}
```

See [samples/l-shaped/l-shaped.mapsproj.json](../samples/l-shaped/l-shaped.mapsproj.json) for
the populated version.
