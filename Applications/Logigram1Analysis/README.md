# Logigram1Analysis

Offline Casper Dataset console for Logigram 1 Logic. Enables the **`Logigram1.GammaPath`** cycle FSM (nodes 1→2→3→C) **per (Battery, Person)**, checks AD-7 topic presence, discovers Battery Spawn→Finish scopes, and persists Battery+Person-scoped `Logigram1.State` / `Logigram1.Outcome` for PsiStudio scrub.

## Enableable unit

| Id | Role |
|----|------|
| `Logigram1.GammaPath` | Sole enableable unit. Required topics include `ObjectBound` + LeftWrist; RightWrist is optional. `M1-ModuleOut` / `M2-ModuleOut` are **not** in the topic bag. |

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

1. New `Module status` id (FANOUT_BOTH) → node 1 on PersonId 1 **and** 2, each with an independent 2s window.
2. Side-keyed door closed + Select/Validation in that person’s window → node 2, **that person resets, no Gamma**.
3. After that person’s unused window: door/hand + leave GeneratorArea on their side → node 3 → writer emits **Gamma (C)** with `PersonId`.

Persisted messages carry `PersonId` (1|2). Stream names stay `Logigram1.State` / `Logigram1.Outcome`.

See `Components/Logigram1.Logic/README.md` for topic payloads and derivation detail.
