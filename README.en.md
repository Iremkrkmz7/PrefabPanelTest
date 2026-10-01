# PrefabPanelTest

![Unity](https://img.shields.io/badge/Unity-2022.3.56f1-black?logo=unity)
![C#](https://img.shields.io/badge/C%23-Editor%20Tool-239120?logo=csharp)

[Türkçe](README.md) | **English**

A Unity editor panel that shows, at a glance, **which values of a prefab instance in the scene differ** from its source prefab, and lets you **Apply or Revert just the changes you pick**.

Unity's built-in *Overrides* dropdown only tells you which components were changed. This panel lists every overridden property with a clear **old value → new value** diff, hides fake (noise) overrides, and lets you select individual rows to write them to the prefab or undo them.

Example view:

```
Prefab Instance: TestPanel                              [Refresh]
[x] Hide noise   (4 hidden)

2 objects, 3 changed properties:
[All|None]                            [Revert (2)|Apply (2)]

[x] ▼ TestPanel / Image  (1)                             [Select]
      [x] Color            ■ #FFFFFFFF  →  ■ #FF0000FF

[-] ▼ Icon / RectTransform  (2)                          [Select]
      [x] Anchored Position.X   0        →  25.5
      [ ] Size Delta.Y          100      →  64
```

## Features

### Diff view
- **Old → new value comparison:** For every overridden property, the value in the source prefab and the value in the scene are shown side by side.
- **Live updates:** The panel refreshes when you edit a value in the Inspector, apply/revert overrides, or use Undo/Redo. Refreshing is throttled to ~10 times per second, so dragging a slider stays smooth.
- **Color preview:** Color properties get a small color swatch next to the value.
- **Readable names:** Internal paths like `m_Items.Array.data[2].m_Name` are shown as `Items[2].Name`. Hover over a row to see Unity's internal path in a tooltip.
- **Default overrides hidden:** Fields that always show up as overridden on a prefab root (position, rotation, name, etc.) are hidden, just like in Unity's own panel.
- **Quick select:** The **Select** button next to each group selects and pings that object in the Hierarchy.
- **Culture-independent numbers:** Floats are always written as `2.5`, never `2,5`, even on a Turkish system. Tiny non-zero values never show up as a misleading `0 → 0`.

### Noise filter (Hide noise)
Unity sometimes reports overrides that you never actually made. With **Hide noise** on, these are hidden and the panel tells you how many were hidden. What counts as noise:

| Case | Example |
|---|---|
| Known noisy properties | TextMeshPro internal state flags, `m_LocalEulerAnglesHint` |
| Layout-driven fields | RectTransform position/size when the parent has a *Layout Group*; size when the object has a *Content Size Fitter* |
| TextMeshPro Auto Size | With Auto Size on, TMP calculates Font Size itself |
| Fields hidden in the Inspector | Internal fields on script components that you can't see |
| Floating-point drift | Rounding errors like `100 → 100.000008` |
| No visible difference | Values that round to the same text on screen |

- With the filter off, noise rows are shown **dimmed** with a `(noise)` label. The tooltip explains why each one is considered noise.
- The toggle state is remembered across Unity restarts.
- Hidden rows are never left selected, so they can't be applied or reverted by accident.
- The code compiles even in projects without the UI or TextMeshPro packages.

### Selective Apply / Revert
- Every row has a **checkbox**. The checkbox on a group header selects all rows of that object (it shows `-` when only some are selected).
- Use **All / None** to select or clear everything.
- **Apply (n):** Writes the selected changes to the source prefab file. A confirmation dialog first lists which prefab files will be affected.
- **Revert (n):** Undoes the selected changes, returning them to the prefab's values.
- **One Ctrl+Z:** The whole bulk operation is a single Undo step.
- **Safe:** Prefabs that can't be modified (FBX models, prefabs inside Packages) are marked `[read-only]` and skipped on Apply. If one row fails, the rest are still processed and the error is logged to the Console.

### Supported types

int, float, bool, string, char, enum, Color, Vector2/3/4, Vector2Int/3Int, Quaternion (shown as Euler angles), Rect, RectInt, Bounds, BoundsInt, object references, AnimationCurve, Gradient, LayerMask and array sizes.

## Usage

1. In Unity, open **Tools → Prefab Override Panel** from the menu.
2. Select a **prefab instance** in the Hierarchy (selecting any child of the instance works too).
3. The panel lists every change on that instance.
4. Tick the rows you want to save to the prefab or undo, then press **Apply** or **Revert**.

> Only **instances in the scene** are inspected, not prefab assets in the Project window.

## Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/Iremkrkmz7/PrefabPanelTest.git
   ```
2. Unity Hub → **Add** → select the cloned folder.
3. Open the project with **Unity 2022.3.56f1**.

To use just the panel in another project, copy `Assets/Editor/PrefabOverridePanel.cs` into any `Editor` folder in that project.

## Project structure

| File | Description |
|---|---|
| `Assets/Editor/PrefabOverridePanel.cs` | The editor panel (all code lives in this file) |
| `Assets/TestPanel.prefab` | Sample UI prefab for testing |
| `Assets/Text (TMP).prefab` | Sample TextMeshPro prefab for testing |
| `Assets/Scenes/SampleScene.unity` | Sample scene |
| `Assets/TextMesh Pro/` | Unity's TextMesh Pro resources |

## How it works

1. The instance root is found from the selected object with `PrefabUtility.GetNearestPrefabInstanceRoot`.
2. Components with overrides are collected with `PrefabUtility.GetObjectOverrides`.
3. Each component is walked with a `SerializedObject`. Only properties marked `prefabOverride` are entered, down to the leaf field that actually changed.
4. That property is compared with its counterpart in the source prefab, found via `GetCorrespondingObjectFromSource`.
5. Every diff runs through the noise rules. If it's noise, the reason is stored.
6. Apply/Revert uses `ApplyPropertyOverride` / `RevertPropertyOverride`. When every row of an object is selected, `ApplyObjectOverride` / `RevertObjectOverride` is called once instead.
