# Logigram1

Contracts-only surface for Logigram 1 (Psi-free / Additional-Assemblies-safe).

## Streams and ownership

| Stream | Constant | Message type | Owner |
|--------|----------|--------------|--------|
| `Logigram1.State` | `LogigramState.StreamName` | `LogigramState` | Progress / position (AD-11). Viz shell binds here. |
| `Logigram1.Outcome` | `LogigramOutcome.StreamName` | `LogigramOutcome` | Regulation terminals only. **Sole producer:** designated writer in sibling project `Logigram1.Logic`. |
| `Logigram1.Evidence` | `LogigramEvidence.StreamName` | `LogigramEvidence` | Path/node evidence stub for future Logic units (not terminals). |

Regulation alphabet (typed on `LogigramRegulation`): **Alpha | Beta | Gamma | N/A | Apprentissage**.

## Battery identity (AD-6 / BOUNDARY_RULE)

Sole scheme for Logigram state, outcomes, evidence, and viz (replaces AD-12 session-only envelopes):

- **BOUNDARY_RULE = Spawn→Finish:** Start = each `BatterySpawn` originating time; End = paired `BatteryFinish` originating time.
- **JOIN_KEY = LIFO stack:** On Spawn, push `BatteryId`; on Finish, pop the most recent still-open id and close that segment. Canonical `BatteryId` (`int`) always comes from `BatterySpawn.BatteryId` (Finish carries none in PreTest.081 / Unity).
- Fail explicitly if Finish arrives with an empty stack, if any Spawn remains open at session end, if required streams are missing, or if zero segments are discovered — never invent keys or silently merge Battery N with Battery M.
- Envelope field: `int BatteryId` only — no `BatteryIndex` / string fork.

Store payload types (Casper / Unity exporters, despite older string-topic wording elsewhere):

| Topic | Catalogue type |
|-------|----------------|
| `BatterySpawn`, `BatteryRegulatedTime`, `BatteryFire` | `PsiBatteryEvent` |
| `Batteries` | `PsiBatterie` |
| `BatteryFinish` | `PsiBatteryFinish` |

Discovery of Spawn→Finish segments lives in Analysis (`BatteryBoundaryDiscovery`), not in this contracts assembly.

## What does not live here

- No Microsoft.Psi package refs or pipeline components (keeps `Logigram1.dll` safe beside Visualizations).
- Designated outcome-writer Psi component: see `Components/Logigram1.Logic/`.
- First Logic unit (`Logigram1.FirstUnit`, nodes 1–2) and Outcome/State persistence: see `Components/Logigram1.Logic/` and Analysis console. CSV / speech / second-store: later stories.

Visualizations and future CSV depend on this contracts assembly only — never on `Logigram1.Logic`.
