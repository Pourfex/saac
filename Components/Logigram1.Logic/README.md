# Logigram1.Logic

Psi-backed sibling of the contracts project `Logigram1`. Holds the **designated outcome writer** and **`Logigram1.FirstUnit`** (nodes 1–2).

## Components

| Type | Role |
|------|------|
| `LogigramOutcomeWriter` | Sole producer of `Logigram1.Outcome` when wired (AD-10). N evidence receivers + optional regulation-decision inputs. |
| `Logigram1FirstUnit` | Unit id `Logigram1.FirstUnit` (nodes 1–2). Derives node 1 then node 2; emits Battery-scoped `Logigram1.State` + `Logigram1.Evidence`; on node-2 posts Gamma to the writer via `RegulationDecisionWithBatteryOut`. |

- `RegulationDecisionIn` emits Outcome with default `BatteryId` (0); `RegulationDecisionWithBatteryIn` stamps the supplied Battery scope (AD-6).
- Evidence alone never invents terminals (honest absent / non-terminal).
- Visualizations must not reference this project.
- Battery Spawn→Finish discovery lives in Analysis (`BatteryBoundaryDiscovery`), not here.

Depends on contracts `Logigram1` upward.

## FirstUnit ↔ Logigramme1.md nodes 1–2 ↔ AD-7 topics

> Real derivation (story 3.1). Node 2 only after node 1; Gamma only via writer on node-2 fire.

| Logigramme1.md node | Meaning | AD-7 PSI topic(s) | Derivation |
|---------------------|---------|-------------------|------------|
| **1** | Génération de module réussie | `Module status` | `(int id, string)` — fire when `id` was **not yet seen this session** (SEEN_SCOPE B: accumulates across Batteries). |
| **2** | Porte fermée + select/validation within 2s | `Porte1 ouverture`, `Porte2 ouverture`, `M1-SelectModule`, `M2-SelectModule`, `M1-Validation`, `M2-Validation` | Door first bool `false` **arms** a **2s** window. Within the window, any non-empty `M?-SelectModule` **or** `M?-Validation == true` fires node 2 — **only if node 1 already fired**. On fire → State/Evidence + `(Gamma, BatteryId)` for the writer. Door open (`true`) does not arm. Validation `false` does not fire. |

Catalogue payload types (Analysis importer map):

| Topic | Type |
|-------|------|
| `Module status` | `(int, string)` |
| `Porte1 ouverture`, `Porte2 ouverture` | `(bool, Vector3)` |
| `M1-SelectModule`, `M2-SelectModule` | `string` |
| `M1-Validation`, `M2-Validation` | `bool` |

Wire order (Analysis): substrate → Logic topics → Battery discovery → (if enabled) resolve output → importers → FirstUnit → `EvidenceOut` → writer.EvidenceIn; `RegulationDecisionWithBatteryOut` → writer.RegulationDecisionWithBatteryIn → persist `Logigram1.State` / `Logigram1.Outcome` → print path.
