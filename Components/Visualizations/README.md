# Visualizations

## Summary
This project contains visualizations for PsiStudio. It needs, as NuGet packages, two projects built from the \psi sources (https://github.com/microsoft/psi/issues/301).  

## Use in PsiStudio
Add `Visualizations.dll` to "Additional Assemblies" in the settings of PsiStudio and start PsiStudio again; then right-click a stream and choose the visualization.
PsiStudio looks for the assemblies a plugin refers to in its own folder first: keep no copy of `Groups.dll`, of `Visualizations.dll` or of another SAAC assembly there.

## Object Bounds
The **Object Bounds** visualization object binds to Casper `ObjectBound` (`ValueTuple<string, Vector3, Vector3>` = name, center, size; AABB = center ± size/2). On stream bind it preloads **all** messages in the store (so you do not need to scrub the sparse early burst), keeps the latest bounds per name, and draws labeled boxes in the XYZ / 3D panel. Names containing **zone** are faded; others stay full opacity. **ReverseYZ** defaults **on**.

## Position Orientation
The **Position Orientation** visualization binds to Casper pose streams (`Tuple<Vector3, Vector3>` = world position + Euler degrees) such as **`1-Head`**, **`1-LeftWrist`**, **`1-RightWrist`** (and person-2 twins; also Eye if needed). It draws a **sphere gizmo** (property **Gizmo Size (m)**, default `0.08`), optional RGB **Axes Length (m)**, and a stream-name label. Colors auto-tint from the stream name (Head / LeftWrist / RightWrist × person). **ReverseYZ** defaults **on**. Tune **Gizmo Size (m)** in the visualizer properties if landmarks are hard to see in the room.

## Gaze Orientation
The **Gaze Orientation** visualization is intended only for **`1-GazeHeadOrientation` / `2-GazeHeadOrientation`** (`Tuple<Vector3, Vector3>` = world position + Euler degrees). Eye / wrist / head streams share that CLR type in PsiStudio’s menu, but this visualizer **refuses raycast** unless the stream name ends with `GazeHeadOrientation` (billboard explains). On a valid gaze stream it draws a thick gaze arrow, sphere-raycasts against companion `ObjectBound` (radius **0**, maxDistance **3 m**, **Ignore Zones** on), and highlights the nearest non-zone hit. Colors: **`1-GazeHeadOrientation`** yellow / orange, **`2-GazeHeadOrientation`** red / black. Status billboard shows `Gaze → name (m)`. **ReverseYZ** defaults **on**. Use **Position Orientation** for Head/Wrist/Eye.

## Logigram State
The **Logigram State** visualization object binds to the contracts type `SAAC.Logigram1.LogigramState` (from the `Logigram1` contracts assembly — no Logic unit DLL required). Discovery is type-driven; producers must write the sole AD-11 state stream named `Logigram1.State` (constant `LogigramState.StreamName`). When the playhead has no message, the shell shows an honest empty label (`no state`) rather than inventing regulation values.

## Logigram Outcome
The **Logigram Outcome** visualization object binds to `SAAC.Logigram1.LogigramOutcome` on stream `Logigram1.Outcome`. Same Additional Assemblies load as State. At the playhead it shows the AD-10 regulation label + `BatteryId` + `PersonId` (or `no outcome` / `absent` when missing) — never invents Alpha|Beta|Gamma. `LogigramOutcome.ToString()` also feeds the built-in **Visualize as Messages** legend when you Show/Hide Legend and scrub onto a message.

## Logigram Graph
The **Logigram Graph** visualization object is a Canvas Gamma-path node map for **one Battery under view** with **two stacked skeletons** (ONE_PANEL_TWO_SKELETONS): person 1 (**yellow** `#FFCC00`) and person 2 (**orange** `#E87A2E`). Bind it from the `Logigram1.State` stream (contracts type `LogigramState`); set the panel property **BatteryId** (default `1`). Add one Graph panel per Battery and set each BatteryId. Status line starts with `Battery N | P1: … | P2: …`. While scrubbing, the panel **holds the last State/Outcome per PersonId for that Battery at or before the playhead** (other-Battery / other-person cursor values do not overwrite the other row). Rewinding earlier than a held message clears that person’s hold. Terminal **C** lights only for Gamma with matching BatteryId **and** PersonId. Same Additional Assemblies path: `build/bin/Components/Visualizations/x64/Debug/net48/Visualizations.dll`.

**How to add:** right-click stream `Logigram1.State` → visualize as **Logigram Graph** (not only Outcome). Save the Logigram layout after adding panels.

**Important:** re-run analysis after Story 3.5 PersonId stamping — old Datasets lack `PersonId` and will not dual-scrub correctly.

## Visualizations of a group
Two visualizations show a group of any number of participants from what the CollaborationIndices component stores: what the group did over the last window, and what it is doing at the instant of the cursor. They are distributed separately from this repository; section 8 of `docs/wiki/CollaborationIndices-Component.md` says which streams they read.

## Files
* [Augmented Body List Visualization Object](src/VisualizationObjects/AugmentedBodyListVisualizationObject.cs)
* [Augmented Body Visualization Object](src/VisualizationObjects/AugmentedBodyVisualizationObject.cs)
* [Augmented Coordinate System List Visualization Object](src/VisualizationObjects/AugmentedCoordinateSystemListVisualizationObject.cs)
* [Augmented Coordinate System Visualization Object](src/VisualizationObjects/AugmentedCoordinateSystemVisualizationObject.cs)
* [Augmented Skeleton Visualization Object](src/VisualizationObjects/AugmentedSkeletonVisualizationObject.cs)
* [Labeled Line2 D Visualization Object](src/VisualizationObjects/LabeledLine2DVisualizationObject.cs)
* [Labeled Point2 D Visualization Object](src/VisualizationObjects/LabeledPoint2DVisualizationObject.cs)
* [Logigram State Visualization Object](src/VisualizationObjects/LogigramStateVisualizationObject.cs)
* [Logigram Outcome Visualization Object](src/VisualizationObjects/LogigramOutcomeVisualizationObject.cs)
* [Logigram Graph Visualization Object](src/VisualizationObjects/LogigramGraphVisualizationObject.cs)
* [Gaze Orientation Visualization Object](src/VisualizationObjects/GazeOrientationVisualizationObject.cs)
* [Object Bound Visualization Object](src/VisualizationObjects/ObjectBoundVisualizationObject.cs)
* [Position Orientation List Visualization Object](src/VisualizationObjects/PositionOrientationListVisualizationObject.cs)
* [Position Orientation Visualization Object](src/VisualizationObjects/PositionOrientationVisualizationObject.cs)
* [Simplified Body List Visualization Object](src/VisualizationObjects/SimplifiedBodyListVisualizationObject.cs)
* [Simplified Body Visualization Object](src/VisualizationObjects/SimplifiedBodyVisualizationObject.cs)
* [Simplified Flock Group Dictionnary Visualisation Object](src/VisualizationObjects/SimplifiedFlockGroupDictionnaryVisualisationObject.cs)
* [Simplified Flock Group Visualisation Object](src/VisualizationObjects/SimplifiedFlockGroupVisualisationObject.cs)

## Current issues

## Future works
* Integrating 2D visualizations displaying positions of HMD users in virtual environment.
