# Logigram1Analysis

Offline Casper Dataset console for Logigram 1 Logic. Enables the **`Logigram1.GammaPath`** cycle FSM (nodes 1→2→3→C), checks AD-7 topic presence, discovers Battery Spawn→Finish scopes, and persists Battery-scoped `Logigram1.State` / `Logigram1.Outcome` for PsiStudio scrub.

## Enableable unit

| Id | Role |
|----|------|
| `Logigram1.GammaPath` | Sole enableable unit. Topic bag = former FirstUnit ∪ Node3 topics. |

Legacy `Logigram1.FirstUnit` and `Logigram1.Node3` in `EnabledUnits` are **warned and ignored** (stderr). They do not run and do not contribute topics.

## Config

```json
{
  "StorePath": "path/to/Casper.pds",
  "EnabledUnits": [ "Logigram1.GammaPath" ],
  "OutputPath": null
}
```

## Cycle (summary)

1. Module create (node 1) → 2s window from create time.
2. Door closed + Select/Validation together in that window → node 2, **reset, no Gamma**.
3. After unused window: door/hand + leave GeneratorArea → node 3 → writer emits **Gamma (C)**.

See `Components/Logigram1.Logic/README.md` for topic payloads and derivation detail.
