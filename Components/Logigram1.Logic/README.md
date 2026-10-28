# Logigram1.Logic

Psi-backed sibling of the contracts project `Logigram1`. Holds the **designated outcome writer** and the enableable **`Logigram1.GammaPath`** cycle FSM (nodes 1→2→3→C).

## Components

| Type | Role |
|------|------|
| `LogigramOutcomeWriter` | Sole producer of `Logigram1.Outcome` when wired (AD-10). N evidence receivers + optional regulation-decision inputs. |
| `Logigram1GammaPathUnit` | Unit id `Logigram1.GammaPath`. Full Anticipation cycle **per (BatteryId, PersonId)**: node 1 → try node 2 (reset, no Gamma) → else try node 3 → Gamma (C) via the writer. No Alpha/Beta/gaze/speech. |

- `RegulationDecisionIn` emits Outcome with default `BatteryId`/`PersonId` (0); `RegulationDecisionWithBatteryIn` stamps `(Regulation, BatteryId, PersonId)` (AD-6).
- Evidence alone never invents terminals (honest absent / non-terminal).
- Visualizations must not reference this project.
- Battery Spawn→Finish discovery lives in Analysis (`BatteryBoundaryDiscovery`), not here.
- Legacy ids `Logigram1.FirstUnit` / `Logigram1.Node3` are **not** enableable Gamma producers (Analysis warns and ignores them).

Depends on contracts `Logigram1` upward.

## Per-participant routing (Story 3.5)

- **Person 1** ↔ side-1 topics: `M1-*`, `Area1`, `Porte1 ouverture`, `1-*` wrists / `Generateur1`.
- **Person 2** ↔ side-2 topics: `M2-*`, `Area2`, `Porte2 ouverture`, `2-*` / `Generateur2`.
- One side never advances the other person’s cycle.
- **FANOUT_BOTH:** a new `Module status` id (deduped once per Battery) starts node-1 on **both** PersonId 1 and 2.
- State / Evidence / Outcome carry `PersonId` (1|2). Stream names stay `Logigram1.State` / `Logigram1.Outcome`.
- `M1-ModuleOut` / `M2-ModuleOut` are side-mapped in lab docs but **not** required or consumed by GammaPath (topic bag unchanged).

## GammaPath cycle ↔ Logigramme1.md nodes 1→2→3→C ↔ AD-7 topics

> Stories 3.4–3.5. Status prefixes `node-1:` / `node-2:` / `node-3:` for graph viz. Gamma only at C via the writer. Cycles are independent per person.

| Node | Meaning | AD-7 PSI topic(s) | Derivation |
|------|---------|-------------------|------------|
| **1** | Génération de module réussie | `Module status` | `(int id, string)` — fire when `id` was **not yet seen on that Battery**. **FANOUT_BOTH:** starts a cycle + **2s node-2 window** on PersonId 1 **and** 2. |
| **2** | Door closed + Select/Validation **together** within 2s of create | `Porte1/2 ouverture`, `M?-SelectModule`, `M?-Validation` | Side-keyed to matching PersonId. Within that person’s create window: door closed **and** Select/Validation → State/Evidence, **that person’s cycle reset, no Gamma**. |
| **3** | Porte fermée + leave GeneratorArea **OU** hand near + leave | `Porte1/2`, `Area1/2`, `ObjectBound`, wrists | Eligible only **after** that person’s unused 2s window. On player (`id=-1`) exit of `GeneratorArea` on AreaN: fire if PorteN closed **or** player-N wrist ≤1 m of `GenerateurN` AABB. On fire → State/Evidence + `(Gamma, BatteryId, PersonId)` for the writer. |

Catalogue payload types (Analysis importer map):

| Topic | Type |
|-------|------|
| `Module status` | `(int, string)` |
| `Porte1 ouverture`, `Porte2 ouverture` | `(bool, Vector3)` |
| `M1-SelectModule`, `M2-SelectModule` | `string` |
| `M1-Validation`, `M2-Validation` | `bool` |
| `Area1`, `Area2` | `(int id, bool state, string info)` |
| `ObjectBound` | `(string name, Vector3, Vector3)` — use `Generateur1` / `Generateur2` |
| `1-LeftWrist`, `2-LeftWrist` | `Tuple<Vector3, Vector3>` (position, orientation) — position = Item1 (**required**) |
| `1-RightWrist`, `2-RightWrist` | Same payload — **optional** (Casper often omits; hand-near uses whichever wrists are present) |

## Analysis console

Wire order: substrate → required Logic topics (`ObjectBound`, LeftWrist, …) → optional RightWrist when present → Battery discovery → (if `Logigram1.GammaPath` enabled) resolve output → importers → unit → Evidence → writer; RegulationDecisionWithBattery → writer → persist `Logigram1.State` / `Logigram1.Outcome` → print path. Empty `EnabledUnits` or legacy-only ids → substrate only (no analysis Dataset). Missing required topics → fail loud (non-zero exit).
