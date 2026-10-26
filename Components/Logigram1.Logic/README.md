# Logigram1.Logic

Psi-backed sibling of the contracts project `Logigram1`. Holds the **designated outcome writer**, **`Logigram1.FirstUnit`** (nodes 1–2), and **`Logigram1.Node3`** (node 3).

## Components

| Type | Role |
|------|------|
| `LogigramOutcomeWriter` | Sole producer of `Logigram1.Outcome` when wired (AD-10). N evidence receivers + optional regulation-decision inputs. |
| `Logigram1FirstUnit` | Unit id `Logigram1.FirstUnit` (nodes 1–2). Derives node 1 then node 2; emits Battery-scoped `Logigram1.State` + `Logigram1.Evidence`; on node-2 posts Gamma to the writer via `RegulationDecisionWithBatteryOut`. |
| `Logigram1Node3Unit` | Unit id `Logigram1.Node3` (node 3). Door-closed leave-zone **or** hand-near Generateur AABB + leave; independently enableable (no FirstUnit / Alpha / Beta / gaze / speech). On fire → State/Evidence + Gamma for the writer. |

- `RegulationDecisionIn` emits Outcome with default `BatteryId` (0); `RegulationDecisionWithBatteryIn` stamps the supplied Battery scope (AD-6).
- Evidence alone never invents terminals (honest absent / non-terminal).
- Visualizations must not reference this project.
- Battery Spawn→Finish discovery lives in Analysis (`BatteryBoundaryDiscovery`), not here.

Depends on contracts `Logigram1` upward.

## FirstUnit ↔ Logigramme1.md nodes 1–2 ↔ AD-7 topics

> Real derivation (story 3.1). Node 2 only after node 1; Gamma only via writer on node-2 fire.

| Logigramme1.md node | Meaning | AD-7 PSI topic(s) | Derivation |
|---------------------|---------|-------------------|------------|
| **1** | Génération de module réussie | `Module status` | `(int id, string)` — fire when `id` was **not yet seen on that Battery** (per-Battery process; same id on another Battery can fire again). |
| **2** | Porte fermée + select/validation within 2s | `Porte1 ouverture`, `Porte2 ouverture`, `M1-SelectModule`, `M2-SelectModule`, `M1-Validation`, `M2-Validation` | Door first bool `false` **arms** a **2s** window **for that Battery**. Within the window, any non-empty `M?-SelectModule` **or** `M?-Validation == true` fires node 2 — **only if that same Battery already had node 1**. On fire → State/Evidence + `(Gamma, BatteryId)` for the writer. Door open (`true`) does not arm. Validation `false` does not fire. |

Catalogue payload types (Analysis importer map):

| Topic | Type |
|-------|------|
| `Module status` | `(int, string)` |
| `Porte1 ouverture`, `Porte2 ouverture` | `(bool, Vector3)` |
| `M1-SelectModule`, `M2-SelectModule` | `string` |
| `M1-Validation`, `M2-Validation` | `bool` |

## Node3 ↔ Logigramme1.md node 3 ↔ AD-7 topics

> Independently enableable (story 3.3). Evaluates local inputs only (no gate on “node 2 did not fire”). Status prefix `node-3:` for graph viz.

| Logigramme1.md node | Meaning | AD-7 PSI topic(s) | Derivation |
|---------------------|---------|-------------------|------------|
| **3** | Porte fermée + leave GeneratorArea **OU** hand near door + leave | `Porte1/2 ouverture`, `Area1/2`, `Object Bound`, `?-LeftWrist`, `?-RightWrist` | On **player** (`id=-1`) **exit** (`state=false`) of `info=GeneratorArea` on AreaN: fire if PorteN latest first bool is `false` (**door branch**) **or** either player-N wrist is within **1 m** of `Object Bound` message named `GenerateurN` AABB `(string name, Vector3, Vector3)` (**hand branch**). Pairing: AreaN↔PorteN↔GenerateurN↔player N. On fire → State/Evidence + `(Gamma, BatteryId)` for the writer. |

Catalogue payload types:

| Topic | Type |
|-------|------|
| `Porte1 ouverture`, `Porte2 ouverture` | `(bool, Vector3)` |
| `Area1`, `Area2` | `(int id, bool state, string info)` |
| `Object Bound` | `(string name, Vector3, Vector3)` — use `Generateur1` / `Generateur2` |
| `1-LeftWrist`, `1-RightWrist`, `2-LeftWrist`, `2-RightWrist` | `Tuple<Vector3, Vector3>` (position, orientation) — position = Item1 |

Wire order (Analysis): substrate → Logic topics (union of enabled units) → Battery discovery → (if FirstUnit and/or Node3 enabled) resolve output → importers → unit(s) → Evidence → writer; RegulationDecisionWithBattery → writer → persist `Logigram1.State` / `Logigram1.Outcome` → print path. Empty `EnabledUnits` → substrate only (no analysis Dataset).
