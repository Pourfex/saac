# Option C — Classification wire mapping (AD-13)

Correct Course 2026-08-11 locked **Option C** under AD-13 KEEP: product labels on the wire stay `Alpha` / `Beta` / `Gamma` only.
Spec stickies E (Apprentissage) and D (N/A) may appear in `Ressources/Logigramme1.md`; they **collapse at composition emit** (Story 2.3) — they are not `ClassificationLabel` values and must not appear as CSV or derived-store product labels.

| Spec sticky | Wire emit (`ClassificationLabel`) |
| --- | --- |
| A | Alpha |
| B | Beta |
| C | Gamma |
| E (Apprentissage) | **Gamma** (same wire value as sticky C; sticky provenance is not preserved on the DTO) |
| D (N/A) | **no-emit** — do not post a `ClassificationEvent` and do not write a classification CSV row (not a blank `Label` cell) |

Stickies outside A–E are out of scope for L1 Option C; do not invent wire labels for them.

## Rules

- Do **not** expand `ClassificationLabel` with Apprentissage, N/A, None, or scores. (`None` is a historical ban; sticky D is N/A → no-emit, not a `None` enum member.)
- Do **not** invent Apprentissage/N/A CSV columns or stream roles.
- Index streams (if any later) use distinct Core types/names — never overload Alpha/Beta/Gamma as index roles.
- L1 input-port notes that mention Option C live in `Mapping/Logigramme1PortMap.md`; this file is the Classification-owned wire record for AD-13 consumers.

## Envelope / export shape (unchanged)

- Payload: `Label`, `Participant`, `GraphId`
- Time: Psi envelope `OriginatingTime` (not a DTO field)
- CSV: `OriginatingTime,Label,Participant,GraphId` with `Label` as the enum name (`Alpha` / `Beta` / `Gamma`)
- Stream role: `Classification` / `Classification_W{Wms}`
