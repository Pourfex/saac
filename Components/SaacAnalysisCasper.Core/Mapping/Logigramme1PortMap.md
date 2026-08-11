# Logigramme 1 port map (Core-owned)

Single human-readable source for Logigramme 1 sticky → role id → catalog topic bindings.
Encoded twin: `PortRoleIds` + `PortTopicMap.CreateDefault()` + `Logigramme1PortRequirements`.
Hosts resolve topics only through Core maps (AD-12). Do not invent capture topics outside `experiment.json`.

Graph id: `Logigramme1`

## Shared-input rule

Several catalog topics are **session-shared** (one stream for both participants). For those roles,
`PortTopicMap` maps **both** M1 and M2 to the **same** topic string. Dual-user branches remain
separate composition instantiations; they simply open the same store stream name. Do **not** invent
`M1-GazeEvent`, `M1-Module status`, etc.

## Direct catalog ports

| Miro / sticky alias | Role id (`PortRoleIds`) | M1 topic | M2 topic | Notes |
| --- | --- | --- | --- | --- |
| M1/M2 module sélectionné; create buttons 6V/12V moto/voiture | `SelectModule` | `M1-SelectModule` | `M2-SelectModule` | Exists (Epic 1) |
| M1/M2 validation sélectionné; bouton de validation | `Validation` | `M1-Validation` | `M2-Validation` | Exists |
| module … out | `ModuleOut` | `M1-ModuleOut` | `M2-ModuleOut` | Exists |
| module … out zone | `ModuleOutZone` | `M1-ModuleOutZone` | `M2-ModuleOutZone` | Exists |
| Génération module réussie ? (parent stream) | `ModuleStatus` | `Module status` | `Module status` | Shared |
| Porte générateur 1 | `GeneratorDoor1` | `Porte1 ouverture` | `Porte1 ouverture` | Shared |
| Porte générateur 2 | `GeneratorDoor2` | `Porte2 ouverture` | `Porte2 ouverture` | Shared |
| Zone 1 / exit generator zone | `GeneratorZone1` | `Area1` | `Area1` | Shared |
| Zone 2 | `GeneratorZone2` | `Area2` | `Area2` | Shared |
| Regard sur …; GazeEvent | `GazeEvent` | `GazeEvent` | `GazeEvent` | Shared; object filters are derived |
| Position tête | `Head` | `1-Head` | `2-Head` | |
| Position … mains (left) | `LeftWrist` | `1-LeftWrist` | `2-LeftWrist` | |
| Position … mains (right) | `RightWrist` | `1-RightWrist` | *(unmapped)* | Catalog gap — see Flagged |
| Gaze orientation | `GazeHeadOrientation` | `1-GazeHeadOrientation` | `2-GazeHeadOrientation` | |
| Eye left | `EyeLeft` | `1-EyeLeft` | `2-EyeLeft` | |
| Eye right | `EyeRight` | `1-EyeRight` | `2-EyeRight` | |
| Grab 1 & 2 | `Grab` | `Grab1` | `Grab2` | Not `M1-Grab` |
| Add module sticky | `AddModule` | `AddModule` | `AddModule` | Shared |
| Remove module sticky | `RemoveModule` | `RemoveModule` | `RemoveModule` | Shared |

Blue circles on Miro (Alpha / Beta / Gamma / Apprentissage / N/A) are **outputs**, not port roles.

## Agreed derived input streams

Named and frozen as role ids now; derivation **logic** is Story 2.3. These are **not** `experiment.json` topics.

| Derived role id | Built from catalog roles/topics | Intent (Miro) |
| --- | --- | --- |
| `ModuleGenerationSuccess` | `ModuleStatus` | Bool/success for "Génération module réussie ?" |
| `DoorClosed` | `GeneratorDoor1` / `GeneratorDoor2` (+ which gen) | "Porte fermée ?" — confirm open-vs-closed polarity with Alexis if unclear |
| `HandNearDoor` | `LeftWrist` / `RightWrist` + door pose | "main à côté de la porte". **M2:** use `LeftWrist` only (no `2-RightWrist`). **M1:** `RightWrist` is mapped but omitted from `Logigramme1RequiredRoles` — bind via `TryGetTopic` when needed. Confirm whether `PorteN ouverture` is the correct door-pose parent vs other catalog streams (Story 2.3). |
| `ExitGeneratorZone` | `GeneratorZone1` / `GeneratorZone2` | "sortie de la zone du générateur" |
| `GazeOnDoorClosedIndicator` | `GazeEvent` (object filter) | "Regard sur indicateur visuel porte fermée" 150–250 ms |
| `GazeOnDoor` | `GazeEvent` (object filter) | "Regard sur porte" 150–250 ms |
| `RepeatedValidationSequence` | `SelectModule` + `Validation` | Alpha: validation ×3 or module+validate sequence |
| `DifferentGeneratorButton` | `SelectModule` (and peer cues) | Beta: "Appuie sur différent bouton générateur" |

## Flagged mismatches (do not invent)

| Alias / prior-art name | Why |
| --- | --- |
| `Gaze1`, `Gaze1IndicatorDoor` | CASPERAnalysis prior art — **not** in `experiment.json`; real topic is `GazeEvent` |
| `SpeechTranscription` / speech comprehension | CASPER prior art; **not** in current Event catalog; Whisper is out of scope |
| `VisualFeedbackEnabled` | CASPER optional; **not** in catalog |
| `2-RightWrist` | Asymmetric catalog — M1 has `1-RightWrist`, M2 does not. Role `RightWrist` is M1-only in `PortTopicMap`; omitted from `Logigramme1PortRequirements` |
| Sticky "scores" | Explicit non-output on Events board — exclude |
| Apprentissage blue circle | Deferred product — not a required runtime **input** port |

## Excluded (not required Logigramme 1 runtime inputs)

- **Apprentissage** and Apprentissage±1 (deferred; revisit with Camilo if needed)
- Camilo / Dorian performance **scores** (evaluation method, not analysis products)

## L1 high-order index candidates (PRD Open Q3 — catalogue only)

Identification only until a later story lists them for runtime. No processors in 2.1.

| Candidate (working name) | Level (approx.) | Likely inputs | Logigramme link |
| --- | --- | --- | --- |
| Module generation success pulse | L1 derived | `ModuleStatus` | Start diamond |
| Door closed / open state | L1 derived | `GeneratorDoor1`/`2` | Door decisions |
| Hand-near-door proximity | L1–L2 | wrists + doors | "main à côté de la porte" |
| Exit generator zone | L1 derived | `Area1`/`Area2` | Zone exit |
| Gaze-on-door / indicator dwell (150–250 ms) | L2 | `GazeEvent` object filter | Gaze stickies |
| Repeated validation sequence | L2 | `SelectModule` + `Validation` | Alpha path |
| Different-generator button choice | L2 | `SelectModule` + peer cues | Beta path |
| JVA-style joint attention (literature) | L2+ | head/gaze (+ speech later) | Catalogue only; speech not in catalog |

Full researcher catalogue remains Epic 6 / FR-12; this list seeds SM-3 identification for Logigramme 1 only.

## Required roles for Story 2.3 binding

See `Logigramme1PortRequirements.Logigramme1RequiredRoles` (catalog, both participants resolvable) and
`Logigramme1DerivedInputRoles` (derived names only).
