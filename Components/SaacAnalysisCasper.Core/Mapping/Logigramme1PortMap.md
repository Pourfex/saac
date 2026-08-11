# Logigramme 1 port map (Core-owned)

Single human-readable source for Logigramme 1 sticky → role id → catalog topic bindings.
Encoded twin: `PortRoleIds` + `PortTopicMap.CreateDefault()` + `Logigramme1PortRequirements`.
Hosts resolve topics only through Core maps (AD-12). Do not invent capture topics outside `experiment.json`.

Graph id: `Logigramme1`

## Authority

| Rank | Source | Role |
| --- | --- | --- |
| 1 | `Ressources/Logigramme1.md` | Topology + node predicates (nodes 1–9 / links) |
| 2 | Correct Course Option C | Wire emit mapping for Story 2.3 (see below) — **not** input ports |
| 3 | `Ressources/PSI_EVENTS_README.md` + `experiment.json` | Payload field meanings + catalog topic strings |

`Logigramme1.mmd` and `Ressources/Miro/` are **non-binding** for L1 science. Sticky aliases from older boards map to catalog names below; mismatches stay flagged (never invent topics).

## Shared-input rule

Several catalog topics are **session-shared** (one stream for both participants). For those roles,
`PortTopicMap` maps **both** M1 and M2 to the **same** topic string. Dual-user branches remain
separate composition instantiations; they simply open the same store stream name. Do **not** invent
`M1-GazeEvent`, `M1-Module status`, etc.

## Direct catalog ports

| Sticky / prior alias | Role id (`PortRoleIds`) | M1 topic | M2 topic | Notes |
| --- | --- | --- | --- | --- |
| M1/M2 module sélectionné; create buttons 6V/12V moto/voiture | `SelectModule` | `M1-SelectModule` | `M2-SelectModule` | Exists (Epic 1) |
| M1/M2 validation sélectionné; bouton de validation | `Validation` | `M1-Validation` | `M2-Validation` | Exists |
| module … out | `ModuleOut` | `M1-ModuleOut` | `M2-ModuleOut` | Exists |
| module … out zone | `ModuleOutZone` | `M1-ModuleOutZone` | `M2-ModuleOutZone` | Exists |
| Génération module / ModuleStatus parent | `ModuleStatus` | `Module status` | `Module status` | Shared; `Item1` = module id |
| Porte générateur 1 | `GeneratorDoor1` | `Porte1 ouverture` | `Porte1 ouverture` | Shared |
| Porte générateur 2 | `GeneratorDoor2` | `Porte2 ouverture` | `Porte2 ouverture` | Shared |
| Zone 1 / exit generator zone | `GeneratorZone1` | `Area1` | `Area1` | Shared; zone name in `info` |
| Zone 2 | `GeneratorZone2` | `Area2` | `Area2` | Shared; zone name in `info` |
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

Blue circles / result stickies (Alpha / Beta / Gamma / Apprentissage / N/A) are **outputs**, not port roles.
Option C collapses E→Gamma and D→no-emit on the wire (Story 2.3) — see emit note below.

## Payload traps (PSI_EVENTS_README — do not re-encode wrong)

| Stream | Authoritative fields | Common traps (do **not** use for L1 science) |
| --- | --- | --- |
| `Area1` / `Area2` (`GeneratorZone1`/`2`) | Zone name in `info` (e.g. `"GeneratorArea"`); `state==false` = **exit**; `id` = player (−1) or module id | Treating `id` as zone index 1/2 (zone identity is `info`, not `id`) |
| `PorteN ouverture` (`GeneratorDoor1`/`2`) | `Item1==false` = **closed**; `Item2` = local Euler orientation degrees | Treating `Item2` as world position for hand distance |
| `Module status` (`ModuleStatus`) | `Item1` = numeric **module id**; status string is separate | Success-substring / `Item1==1` as “génération réussie” |

**Door world pose for hand distance:** catalog door topics expose open/closed + Euler orientation only. Bounds / world pose for &lt;1 m proximity remain TBD for Story 2.3 — keep `PorteN ouverture` as door-state parent; flag if a separate pose/bounds stream is still unknown (do not invent topics).

## Agreed derived input streams (nodes 1–9)

Named and frozen as role ids now; derivation **logic** is Story 2.3. These are **not** `experiment.json` topics.
Authority: `Ressources/Logigramme1.md` (not Miro-era anticipation / DoorElse heuristics).

| Node | Derived role id | Parents (catalog and/or other derived) | Intent (`Logigramme1.md`) |
| --- | --- | --- | --- |
| 1 | `ModuleGenerationSuccess` | `ModuleStatus` | **CHANGE:** first-unseen module **id** on ModuleStatus (`Item1` = id). Not success-substring / `Item1==1`. |
| 2 | `PostDoorSelectOrValidation` | `DoorClosed` (derived) + `SelectModule` + `Validation` | DoorClosed ∧ (SelectModule change \| Validation) within **2 s** |
| 3 | `ExitGeneratorZone` | `GeneratorZone1` / `GeneratorZone2` | Area: `info=="GeneratorArea"`, `state==false` (exit). Leaf for node 3 (see full predicate below). |
| 3 | `HandNearDoor` | `LeftWrist` / `RightWrist` + door pose parent | Hand &lt;**1 m** of generator door (not 0.15 m). **M2:** `LeftWrist` only (no `2-RightWrist`). **M1:** `RightWrist` mapped but omitted from `Logigramme1RequiredRoles` — bind via `TryGetTopic` when needed. Door pose parent still flagged for 2.3 (orientation ≠ world pose). |
| 4 | `GazeOnDoorClosedIndicator` | `GazeEvent` (object filter) | Gaze on door-closed indicator, **150–250 ms**, combined within **3 s** with/without door gaze (ET/OU — confirm with Alexis in 2.3) |
| 4 | `GazeOnDoor` | `GazeEvent` (object filter) | Gaze on door, **150–250 ms**, within the same **3 s** combine window |
| 2–6 | `DoorClosed` | `GeneratorDoor1` / `GeneratorDoor2` (+ which gen) | Sustained closed state: `Item1==false` polarity **KEEP**. Decision nodes 5/6; also parent of nodes 2/3. |
| 7 | `RepeatedValidationSequence` | `SelectModule` + `Validation` | Validation×3 **or** (SelectModule + Validation)×3 |
| 8 | `DifferentGeneratorButton` | `SelectModule` + `Validation` | Different SelectModule **then** Validation (Validation required) |
| 9 | `DoorClosure` | `GeneratorDoor1` / `GeneratorDoor2` | Door **closure edge** (open→closed), distinct from sustained `DoorClosed` |

Node 3 full predicate (`Logigramme1.md`): **(DoorClosed ∧ GeneratorArea exit) ∨ (HandNearDoor ∧ GeneratorArea exit)**. Composition combines `DoorClosed`, `ExitGeneratorZone`, and `HandNearDoor` in Story 2.3 — do **not** require DoorClosed on the hand∧exit arm.

## Option C — emit rules for Story 2.3 (not input ports)

Correct Course Option C sticky → wire mapping. Normative emit table for Classification lives in `Classification/OptionC.md` (AD-13); summarized here so map readers see E/D are **not** input ports. **Do not** add Apprentissage / N/A as required input roles.

| Spec sticky | Wire emit |
| --- | --- |
| A | Alpha |
| B | Beta |
| C | Gamma |
| E (Apprentissage) | **Gamma** |
| D (N/A) | **no-emit** |

## Flagged mismatches (do not invent)

| Alias / prior-art name | Why |
| --- | --- |
| `Gaze1`, `Gaze1IndicatorDoor` | CASPERAnalysis prior art — **not** in `experiment.json`; real topic is `GazeEvent` |
| `SpeechTranscription` / speech comprehension | CASPER prior art; **not** in current Event catalog; Whisper is out of scope; L1 has no speech |
| `VisualFeedbackEnabled` | CASPER optional; **not** in catalog |
| `2-RightWrist` | Asymmetric catalog — M1 has `1-RightWrist`, M2 does not. Role `RightWrist` is M1-only in `PortTopicMap`; omitted from `Logigramme1PortRequirements` |
| Door world pose / object bounds for hand&lt;1 m | Door catalog = open/closed + Euler; world pose/bounds stream TBD — flag for 2.3, do not invent |
| `GazeEvent` object ids for “door” vs “door-closed indicator” | `Logigramme1.md` leaves object filter TBD; do not invent ObjectType substrings — confirm with Alexis in 2.3 |
| Sticky "scores" | Explicit non-output on Events board — exclude |
| Apprentissage blue circle / sticky E | Deferred as product label; Option C emits Gamma — not a required runtime **input** port |

## Excluded (not required Logigramme 1 runtime inputs)

- **Apprentissage** and Apprentissage±1 (deferred product label; Option C E→Gamma is emit-only)
- Camilo / Dorian performance **scores** (evaluation method, not analysis products)

## L1 high-order index candidates (catalogue only)

Identification only until Story 2.3 lists them for runtime. No processors in 2.1.

| Candidate (working name) | Level (approx.) | Likely inputs | Logigramme link |
| --- | --- | --- | --- |
| First-unseen ModuleStatus id pulse | L1 derived | `ModuleStatus` | Node 1 |
| Post-door SelectModule\|Validation ≤2 s | L1 derived | doors + Select + Validation | Node 2 |
| Door closed / open state | L1 derived | `GeneratorDoor1`/`2` | Nodes 2–6, 9 parent |
| Door closure edge | L1 derived | `GeneratorDoor1`/`2` | Node 9 |
| Hand-near-door proximity (&lt;1 m) | L1–L2 | wrists + door pose/bounds | Node 3 |
| Exit GeneratorArea | L1 derived | `Area1`/`Area2` via `info` | Node 3 |
| Gaze-on-door / indicator dwell (150–250 ms / 3 s) | L2 | `GazeEvent` object filter | Node 4 |
| Repeated validation sequence | L2 | `SelectModule` + `Validation` | Node 7 → Alpha |
| Different-generator button then Validation | L2 | `SelectModule` + `Validation` | Node 8 → Beta |

Full researcher catalogue remains Epic 6 / FR-12; this list seeds identification for Logigramme 1 only.

## Required roles for Story 2.3 binding

See `Logigramme1PortRequirements.Logigramme1RequiredRoles` (catalog, both participants resolvable) and
`Logigramme1DerivedInputRoles` (derived names only).
