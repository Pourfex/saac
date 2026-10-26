# Visualizations

## Summary
This project contains visualizations for PsiStudio. It needs, as NuGet packages, two projects built from the \psi sources (https://github.com/microsoft/psi/issues/301).  

## Use in PsiStudio
Add `Visualizations.dll` to "Additional Assemblies" in the settings of PsiStudio and start PsiStudio again; then right-click a stream and choose the visualization.
PsiStudio looks for the assemblies a plugin refers to in its own folder first: keep no copy of `Groups.dll`, of `Visualizations.dll` or of another SAAC assembly there.

## Logigram State
The **Logigram State** visualization object binds to the contracts type `SAAC.Logigram1.LogigramState` (from the `Logigram1` contracts assembly — no Logic unit DLL required). Discovery is type-driven; producers must write the sole AD-11 state stream named `Logigram1.State` (constant `LogigramState.StreamName`). When the playhead has no message, the shell shows an honest empty label (`no state`) rather than inventing regulation values.

## Logigram Outcome
The **Logigram Outcome** visualization object binds to `SAAC.Logigram1.LogigramOutcome` on stream `Logigram1.Outcome`. Same Additional Assemblies load as State. At the playhead it shows the AD-10 regulation label + `BatteryId` (or `no outcome` / `absent` when missing) — never invents Alpha|Beta|Gamma. `LogigramOutcome.ToString()` also feeds the built-in **Visualize as Messages** legend when you Show/Hide Legend and scrub onto a message.

## Logigram Graph
The **Logigram Graph** visualization object is a Canvas Gamma-path node map (`1 → 2 → 3 → C`) for Battery-scoped scrub. Bind it from the `Logigram1.State` stream (contracts type `LogigramState`); when the same partition also has `Logigram1.Outcome`, the viz auto-pairs that stream to light terminal **C** only when Regulation is Gamma. Status label shows `Status` + `BatteryId` (DISPLAY_ONLY — no viz-side Battery filter). Highlight is CURSOR_ONLY: only the `node-N` parsed from the current State Status (and C from current Outcome) — prior nodes are not accumulated, and missing/unparseable Status never fabricates a node. Node 3 stays on the skeleton even when unimplemented. Same Additional Assemblies path as State/Outcome: `build/bin/Components/Visualizations/x64/Debug/net48/Visualizations.dll`.

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
* [Position Orientation List Visualization Object](src/VisualizationObjects/PositionOrientationListVisualizationObject.cs)
* [Position Orientation Visualization Object](src/VisualizationObjects/PositionOrientationVisualizationObject.cs)
* [Simplified Body List Visualization Object](src/VisualizationObjects/SimplifiedBodyListVisualizationObject.cs)
* [Simplified Body Visualization Object](src/VisualizationObjects/SimplifiedBodyVisualizationObject.cs)
* [Simplified Flock Group Dictionnary Visualisation Object](src/VisualizationObjects/SimplifiedFlockGroupDictionnaryVisualisationObject.cs)
* [Simplified Flock Group Visualisation Object](src/VisualizationObjects/SimplifiedFlockGroupVisualisationObject.cs)

## Current issues

## Future works
* Integrating 2D visualizations displaying positions of HMD users in virtual environment.
