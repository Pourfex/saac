# Logigram1

Contracts-only surface for Logigram 1 (Psi-free / Additional-Assemblies-safe).

## Streams and ownership

| Stream | Constant | Message type | Owner |
|--------|----------|--------------|--------|
| `Logigram1.State` | `LogigramState.StreamName` | `LogigramState` | Progress / position (AD-11). Viz shell binds here. |
| `Logigram1.Outcome` | `LogigramOutcome.StreamName` | `LogigramOutcome` | Regulation terminals only. **Sole producer:** designated writer in sibling project `Logigram1.Logic`. |
| `Logigram1.Evidence` | `LogigramEvidence.StreamName` | `LogigramEvidence` | Path/node evidence stub for future Logic units (not terminals). |

Regulation alphabet (typed on `LogigramRegulation`): **Alpha | Beta | Gamma | N/A | Apprentissage**.

## What does not live here

- No Microsoft.Psi package refs or pipeline components (keeps `Logigram1.dll` safe beside Visualizations).
- No Battery id/index fields yet (AD-12 until story 2.3).
- Designated outcome-writer Psi component: see `Components/Logigram1.Logic/`.

Visualizations and future CSV depend on this contracts assembly only — never on `Logigram1.Logic`.
