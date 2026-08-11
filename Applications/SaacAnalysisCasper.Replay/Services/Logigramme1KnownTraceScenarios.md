# Logigramme 1 known-trace scenarios (Story 2.4)

Host-owned catalog-typed inject schedules for Miro nodes N1–N8 plus a full-tree proof run.
Same `knownTraceScenario` id is applied independently to M1 and M2 (AD-3).
Labels: Alpha / Beta / Gamma only — no speech / VisualFeedback / Apprentissage.

Priority mux (must silence competitors on hits): AnticipationΓ → GazeΓ → Alpha → Beta → DoorElseΓ.

## Primary proof run: `full-tree-coverage`

One Replay inject timeline sequences **all** Miro branch polarities (hits and mid-timeline misses) so a single run’s Classification CSV must contain **Alpha, Beta, and Gamma**.

| Offset (ms) | Segment | Effect |
| --- | --- | --- |
| 0–1 | Seed neutrals | ModuleA, Validation false @1, doors OPEN, wrist far, idle |
| 100–200 | Anticipation hit | Wrist near + ModuleStatus success → AnticipationΓ |
| 400–800 | Anticipation miss | Wrist far + success → no AnticipationΓ from that path; idle again |
| 900–1450 | Indicator + Gaze hit | Indicator dwell → porte dwell → door CLOSE → GazeΓ |
| 1700 | Reset | Doors OPEN, gaze clear |
| 1900–2200 | Gaze miss | Porte dwell, doors stay OPEN → no GazeΓ; gaze clear |
| 2400–2900 | Alpha hit | ModuleA + Validation×3 rising edges → Alpha |
| 3100 | Beta hit | SelectModule ModuleB → Beta |
| 3600–3800 | DoorElseΓ | Door CLOSE (no gaze/success/hand) → DoorElseΓ; doors reopen |

- **ExpectedLabels:** Alpha, Beta, Gamma (all must appear)
- **ForbiddenLabels:** empty (misses are mid-timeline; later hits emit labels)
- **MiroNode:** `ALL`
- **ScheduleSpanMs:** 5000 (covers max offset ~3800)

**Alpha miss** is *not* sequenced mid-timeline: Alpha’s Core 5 s lookback would still see the hit edges after the Alpha segment. Use dedicated `N6-miss-alpha` for the single-rising-edge miss polarity.

Mid-timeline misses prove **no-emit** for that path only (Anticipation miss, Gaze miss). They do not forbid later Alpha/Beta/Gamma from other branches.

## Per-node scenarios (N1–N8)

| ScenarioId | MiroNode | Polarity | Participant | CatalogTimeline | ExpectedLabels | ForbiddenLabels | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| N1-hit-success | N1 ModuleGenerationSuccess | hit | Both (M1+M2) | ModuleStatus success + LeftWrist near door pose; doors OPEN; no SelectModule change; no Validation×3; no gaze dwell | Gamma | | Anticipation path (also exercises N2). Dual-user hit for M1 and M2. |
| N1-miss-success | N1 ModuleGenerationSuccess | miss | Both | ModuleStatus failure + hand near; doors OPEN; no gaze / alpha / beta | | Gamma | Success absent → no AnticipationΓ. |
| N2-hit-handnear | N2 HandNearDoor | hit | Both | Same as N1-hit-success (success ∧ hand-near lookback) | Gamma | | AnticipationΓ. |
| N2-miss-handnear | N2 HandNearDoor | miss | Both | ModuleStatus success + wrist far from doors; doors OPEN | | Gamma | Success without hand-near → no AnticipationΓ. |
| N3-hit-indicator | N3 GazeOnDoorClosedIndicator | hit | Both | Indicator ObjectType dwell 150–250 ms, then door ObjectType dwell + door CLOSE within join; no success/hand/alpha/beta | Gamma | | Indicator keep-observed then GazeΓ path. GazeEvent.UserId = participant. |
| N3-miss-indicator | N3 GazeOnDoorClosedIndicator | miss | Both | Non-matching / non-dwelling gaze only; doors OPEN; neutrals | | Gamma | Indicator dwell not satisfied; no classification leaf armed. |
| N4-hit-gaze-door | N4 GazeOnDoor | hit | Both | Door ObjectType dwell + door CLOSE near dwell end (same mux tick beats DoorElse); no success/hand/alpha/beta | Gamma | | GazeΓ. |
| N4-miss-gaze-door | N4 GazeOnDoor | miss | Both | Door ObjectType dwell; doors stay OPEN | | Gamma | Gaze without DoorClosed → no GazeΓ / DoorElseΓ. |
| N5-hit-door-closed-gaze | N5 DoorClosed (gaze paths) | hit | Both | Same pattern as N4-hit-gaze-door | Gamma | | DoorClosed ∧ GazeOnDoor → GazeΓ. |
| N5-miss-door-closed-gaze | N5 DoorClosed (gaze paths) | miss | Both | Door ObjectType dwell; doors stay OPEN | | Gamma | No DoorClosed → fall through. |
| N6-hit-alpha | N6 RepeatedValidationSequence | hit | Both | SelectModule seed once; ≥3 Validation rising edges within 5 s; doors OPEN; no success/hand; no SelectModule change; no gaze | Alpha | Gamma, Beta | Competitors silenced. |
| N6-miss-alpha | N6 RepeatedValidationSequence | miss | Both | SelectModule seed; only 1 Validation rising edge; doors OPEN | | Alpha | Below Alpha threshold. |
| N7-hit-beta | N7 DifferentGeneratorButton | hit | Both | SelectModule A then B; doors OPEN; no success/hand; no Validation×3; no gaze | Beta | Alpha, Gamma | |
| N7-miss-beta | N7 DifferentGeneratorButton | miss | Both | Single SelectModule seed only; doors OPEN | | Beta | First seed is not a change. |
| N8-hit-door-else-gamma | N8 Final DoorClosed | hit | Both | Doors OPEN then CLOSE rising edge; no success/hand; no gaze; no alpha/beta | Gamma | Alpha, Beta | DoorElseΓ only. |
| N8-miss-door-else | N8 Final DoorClosed | miss | Both | Doors stay OPEN; neutrals | | Gamma | No DoorElseΓ / no substitute Apprentissage. |

## Dual-user

Every scenario above is wired for **both** M1 and M2 with independent inject producer instances (`GazeEvent.UserId` matches the branch). `full-tree-coverage` (or any documented hit) therefore covers M1 and M2 simultaneously.

## Config

Optional run-config key: `"knownTraceScenario": "full-tree-coverage"` with `"graphs": ["Logigramme1"]`.
Absence of the key ⇒ catalog bind (Story 2.5).
Unknown id ⇒ fail closed before run.
