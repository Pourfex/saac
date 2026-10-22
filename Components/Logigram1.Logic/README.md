# Logigram1.Logic

Psi-backed sibling of the contracts project `Logigram1`. Holds the **designated outcome writer** only (`LogigramOutcomeWriter`).

- Sole producer of `Logigram1.Outcome` when wired on a pipeline (AD-10).
- Accepts N path/node evidence receivers plus optional regulation-decision inputs; emits one Outcome stream.
- `RegulationDecisionIn` emits Outcome with default `BatteryId` (0); `RegulationDecisionWithBatteryIn` stamps the supplied Battery scope (AD-6).
- Does not invent Alpha/Beta/Gamma when no decision is available (honest absent / non-terminal).
- Console end-to-end wiring is deferred to story 2.4.

Depends on contracts `Logigram1` upward; Visualizations must not reference this project.
Battery Spawn→Finish discovery lives in Analysis (`BatteryBoundaryDiscovery`), not here.
