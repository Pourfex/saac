# Logigram1.Logic

Psi-backed sibling of the contracts project `Logigram1`. Holds the **designated outcome writer** and the **FirstUnit interim stub**.

## Components

| Type | Role |
|------|------|
| `LogigramOutcomeWriter` | Sole producer of `Logigram1.Outcome` when wired (AD-10). N evidence receivers + optional regulation-decision inputs. |
| `Logigram1FirstUnit` | Interim stub for unit id `Logigram1.FirstUnit` (nodes 1–2). Emits Battery-scoped `Logigram1.State` + `Logigram1.Evidence`; does **not** invent Alpha/Beta/Gamma. |

- `RegulationDecisionIn` emits Outcome with default `BatteryId` (0); `RegulationDecisionWithBatteryIn` stamps the supplied Battery scope (AD-6).
- Evidence alone never invents terminals (honest absent / non-terminal).
- Visualizations must not reference this project.
- Battery Spawn→Finish discovery lives in Analysis (`BatteryBoundaryDiscovery`), not here.

Depends on contracts `Logigram1` upward.

## FirstUnit ↔ Logigramme1.md nodes 1–2 ↔ AD-7 topics (**interim stub**)

> **UNIT_DEPTH B — explicit interim stub.** Topic presence → labeled Status/Evidence only.
> Full ModuleStatus “new module” derivation and door/button 2s timing are deferred to Epic 3.

| Logigramme1.md node | Meaning (full rules later) | AD-7 PSI topic(s) | Stub behavior |
|---------------------|----------------------------|-------------------|---------------|
| **1** | Génération de module réussie (derived from ModuleStatus) | `Module status` | On any message: Status/Evidence labeled `node-1` + BatteryId |
| **2** | Porte fermée + select/validation within 2s | `Porte1 ouverture`, `Porte2 ouverture`, `M1-SelectModule`, `M2-SelectModule`, `M1-Validation`, `M2-Validation` | On any message: Status/Evidence labeled `node-2` + BatteryId |

Catalogue payload types (Analysis importer map; stub ignores field values):

| Topic | Type |
|-------|------|
| `Module status` | `(int, string)` |
| `Porte1 ouverture`, `Porte2 ouverture` | `(bool, Vector3)` |
| `M1-SelectModule`, `M2-SelectModule` | `string` |
| `M1-Validation`, `M2-Validation` | `bool` |

Wire order (Analysis): substrate → Logic topics → Battery discovery → (if enabled) resolve output → importers → FirstUnit → writer → persist `Logigram1.State` / `Logigram1.Outcome` → print path.
