# Logigram1.Logic

Psi-backed sibling of the contracts project `Logigram1`. Holds the **designated outcome writer** only (`LogigramOutcomeWriter`).

- Sole producer of `Logigram1.Outcome` when wired on a pipeline (AD-10).
- Accepts N path/node evidence receivers plus an optional regulation-decision input; emits one Outcome stream.
- Does not invent Alpha/Beta/Gamma when no decision is available (honest absent / non-terminal).
- Console end-to-end wiring is deferred to story 2.4.

Depends on contracts `Logigram1` upward; Visualizations must not reference this project.
