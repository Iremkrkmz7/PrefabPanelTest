# PrefabPanelTest

![Unity](https://img.shields.io/badge/Unity-2022.3.56f1-black?logo=unity)
![C#](https://img.shields.io/badge/C%23-Editor%20Tool-239120?logo=csharp)

[Türkçe](README.md) | **English**

A Unity editor panel that shows, at a glance, **which values of a prefab instance in the scene differ** from its source prefab.

Unity's built-in *Overrides* dropdown only tells you which components were changed. This panel lists every overridden property with a clear **old value → new value** diff.

Example view:

```
Prefab Instance: TestPanel                         [Yenile]
2 obje, 3 degisen property:

▼ TestPanel / Image  (1)                              [Sec]
      Color              ■ #FFFFFFFF  →  ■ #FF0000FF

▼ Icon / RectTransform  (2)                           [Sec]
      Anchored Position.X   0        →  25.5
      Size Delta.Y          100      →  64
```

> The panel's UI labels are in Turkish: *Yenile* = Refresh, *Sec* = Select, *obje* = objects, *degisen property* = changed properties.

## Features

- **Old → new value comparison:** For every overridden property, the value in the source prefab and the value in the scene are shown side by side.
- **Live updates:** The panel refreshes when you edit a value in the Inspector, apply/revert overrides, or use Undo/Redo. Refreshing is throttled to ~10 times per second, so dragging a slider stays smooth.
- **Color preview:** Color properties get a small color swatch next to the value.
- **Readable names:** Internal paths like `m_Items.Array.data[2].m_Name` are shown as `Items[2].Name`. Hover over a row to see Unity's internal path in a tooltip.
- **No clutter:** *Default overrides* that always appear on a prefab root (position, rotation, name, etc.) are hidden, just like in Unity's own panel.
- **Quick select:** The **Sec** (Select) button next to each group selects and pings that object in the Hierarchy.
- **Collapsible groups:** Fold groups away when there are many overrides.
- **Culture-independent numbers:** Floats are always written as `2.5`, never `2,5`, even on a Turkish system. Tiny non-zero values never show up as a misleading `0 → 0`.

### Supported types

int, float, bool, string, char, enum, Color, Vector2/3/4, Vector2Int/3Int, Quaternion (shown as Euler angles), Rect, RectInt, Bounds, BoundsInt, object references, AnimationCurve, Gradient, LayerMask and array sizes.

## Usage

1. In Unity, open **Tools → Prefab Override Panel** from the menu.
2. Select a **prefab instance** in the Hierarchy (selecting any child of the instance works too).
3. The panel lists every change on that instance.

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
| `Assets/Scenes/SampleScene.unity` | Sample scene |

## How it works

1. The instance root is found from the selected object with `PrefabUtility.GetNearestPrefabInstanceRoot`.
2. Components with overrides are collected with `PrefabUtility.GetObjectOverrides`.
3. Each component is walked with a `SerializedObject`. Only properties marked `prefabOverride` are entered, down to the leaf field that actually changed.
4. That property is compared with its counterpart in the source prefab, found via `GetCorrespondingObjectFromSource`.
