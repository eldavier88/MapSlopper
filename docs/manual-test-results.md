# Manual test checklist

Tick off each box after a successful local run. This complements the automated xunit suite.

## Build

- [ ] `dotnet restore` succeeds on a clean clone
- [ ] `dotnet build` succeeds with zero errors and zero warnings
- [ ] `dotnet test tests/MapSlopper.Core.Tests` reports 44/44 passing

## GUI launch

- [ ] `dotnet run --project src/MapSlopper.Gui` opens the main window
- [ ] 2D editor tab is visible and renders the empty grid
- [ ] 3D preview tab is visible and renders the empty scene
- [ ] Levels window opens from the menu

## 2D editing — all 7 tools

- [ ] **Add Point** — click in empty space adds a point
- [ ] **Insert Edge** — drag from one point to another adds an edge
- [ ] **Move** — drag a point to reposition it; connected edges follow
- [ ] **Erase Edge** — click an edge to remove it (points remain)
- [ ] **Connect Points** — clicking two existing points adds an edge between them
- [ ] **Remove Point** — click a point to delete it (and its edges)
- [ ] **Height Brush** — paint cells; heightmap values update and 3D preview reflects them

## Undo / redo

- [ ] Undo reverses the last 2D edit (any tool)
- [ ] Redo replays the undone edit
- [ ] Multiple consecutive undos walk back through history
- [ ] New edit after undo discards the redo stack

## Save / open round-trip

- [ ] Save writes a `.mapsproj.json`
- [ ] Open reloads the saved file with identical points, edges, heightmap
- [ ] Re-saving without edits produces a byte-identical file

## Export `.map` → q3map2

- [ ] CLI `build` writes a `.map` for the square-room sample
- [ ] CLI `build` writes a `.map` for the l-shaped sample
- [ ] CLI `build` writes a `.map` for the stepped-floor sample
- [ ] `q3map2` compiles each `.map` to a `.bsp` with no leaks
- [ ] `q3map2 -vis` and `q3map2 -light` complete without errors

## 3D preview navigation

- [ ] WASD moves the camera horizontally
- [ ] Mouse look rotates the view
- [ ] Q / E (or equivalents) move vertically
- [ ] Camera speed feels reasonable for the default 256 ceiling
