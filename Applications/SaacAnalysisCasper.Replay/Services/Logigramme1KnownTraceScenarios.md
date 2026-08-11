# Logigramme 1 known-trace scenarios (Story 2.4 — Option C)

Host-owned catalog-typed inject schedules for **Logigramme1.md nodes 1–9** (Option C wire labels).
Same `knownTraceScenario` id is applied independently to M1 and M2 (AD-3).
Labels: **Alpha / Beta / Gamma** only — never Apprentissage / N/A / scores.

Authority: `Ressources/Logigramme1.md` + Option C (C→Gamma, E→Gamma, D→no Classification row).
Miro Anticipation / DoorElse / Select-only Beta catalogues are **retired**.

## Option C emit table

| Node | Hit | Miss |
| --- | --- | --- |
| 1 | Arms node-2 path (no label alone) | Timeout → node 4 |
| 2 | DoorClosed ∧ (Select change \| Validation) → node 1 | Door open / window → node 3 |
| 3 | **Gamma** (C) via DoorClosed ∧ GeneratorArea exit | Timeout → node 1 |
| 4 | Gaze dwell → node 6 | Window → node 5 |
| 5 | **D silence** (no Classification row) | Door open → node 7 |
| 6 | **Gamma** (E→Γ) when DoorClosed | Door open → node 7 |
| 7 | **Alpha** (Validation×3 inside entry window) | Window → node 8 |
| 8 | **Beta** (different Select **then** Validation contiguous) | Window → node 9 |
| 9 | **D silence** (DoorClosure open→closed) | Timeout → node 4 |

## Pathing notes

- Reach **4** via node-1 miss (5 s timeout). Heartbeats keep miss clocks advancing.
- Reach **3** via 1-hit → door-open immediate 2-miss.
- Reach **7** via 1-miss → 4-miss → 5 with door open.
- Reach **8/9** via 7-miss / 8-miss (5 s sequence windows).
- **HandNear** stays fail-closed (no door world pose) — node-3 hits use **DoorClosed ∧ GeneratorArea exit** (`id==-1`, `state==false`, `info=="GeneratorArea"`).
- Seed `ModuleStatus` id `0` is pre-registered after session reset so neutrals do not arm node-1 hits; intentional successes use per-participant module ids.
- **ScheduleSpanMs:** 25000 (covers deepest 1→4→5→7→8→9 path).

## Per-node scenarios (1–9)

| ScenarioId | Node | Polarity | Participant | Timeline | ExpectedLabels | ForbiddenLabels | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| N1-hit-success | 1 | hit | Both | Doors CLOSED; first-unseen ModuleStatus; no exit / gaze / αβ | | Alpha, Beta, Gamma | Structural: arms node 2 (closed door). |
| N1-miss-timeout | 1 | miss | Both | Heartbeat already-seen ModuleStatus past 5 s miss timeout; no new module id | | Alpha, Beta, Gamma | Branches toward 4. |
| N2-hit-post-door | 2 | hit | Both | Doors CLOSED before success; ModuleStatus hit → 2; SelectModule A→B while closed | | Alpha, Beta, Gamma | Structural loop toward 1; no emit. |
| N2-miss-door-open | 2 | miss | Both | ModuleStatus hit with doors OPEN → immediate 2→3 | | Alpha, Beta, Gamma | Competing C not armed (no GeneratorArea exit). |
| N3-hit-c-gamma | 3 | hit | Both | Open-door cascade to 3; then DoorClosed ∧ GeneratorArea player exit on both zones | Gamma | Alpha, Beta | C→Gamma. HandNear not used. |
| N3-miss-c | 3 | miss | Both | Cascade to 3; doors stay OPEN; no GeneratorArea exit; wait node-3 timeout | | Alpha, Beta, Gamma | No C. |
| N4-hit-gaze | 4 | hit | Both | Node-1 miss → 4; porte dwell 150–250 ms → 6 (door open → 6 miss to 7) | | Alpha, Beta, Gamma | Structural arming only. |
| N4-miss-gaze | 4 | miss | Both | Node-1 miss → 4; no matching dwell; wait 3 s combine window → 5 | | Alpha, Beta, Gamma | |
| N5-hit-d-silence | 5 | hit | Both | 1-miss → 4; close doors while on 4; 4-miss → 5 with DoorClosed → D | | Alpha, Beta, Gamma | `AssertNoClassificationRows`; hard zero-row gate. |
| N5-miss-d | 5 | miss | Both | 1-miss → 4-miss → 5; doors stay OPEN through ~12 s → 7 | | Alpha, Beta, Gamma | Soft miss (timeline longer than N4-miss). |
| N6-hit-e-gamma | 6 | hit | Both | 1-miss → 4; close doors; porte dwell → 6 with DoorClosed → E | Gamma | Alpha, Beta | E→Gamma (never Apprentissage). |
| N6-miss-e | 6 | miss | Both | 1-miss → 4; porte dwell with doors OPEN → 6 → 7 | | Alpha, Beta, Gamma | |
| N7-hit-alpha | 7 | hit | Both | Path to 7 via 5-open; Validation false then ×3 rising inside 5 s (Reset-safe) | Alpha | Beta, Gamma | |
| N7-miss-alpha | 7 | miss | Both | Path to 7; single Validation rising edge; wait sequence window → 8 | | Alpha, Beta, Gamma | |
| N8-hit-beta | 8 | hit | Both | Path to 8 via 7-miss; Select A→B; Validation false then rising (Reset-safe) | Beta | Alpha, Gamma | Not Select-only. |
| N8-miss-beta | 8 | miss | Both | Path to 8; Select B only (no Validation); wait → 9 | | Alpha, Beta, Gamma | Select-only must not emit. |
| N9-hit-d-silence | 9 | hit | Both | Path to 9; single DoorClosure open→closed (no 18500 open clobber) → D | | Alpha, Beta, Gamma | `AssertNoClassificationRows`. |
| N9-miss-d | 9 | miss | Both | Path to 9; doors stay OPEN; wait node-9 timeout → 4 | | Alpha, Beta, Gamma | Soft miss. |

## Dual-user

Every scenario is wired for **both** M1 and M2 with independent inject producer instances (`GazeEvent.UserId` matches the branch; zone/door parents pair M1↔1 / M2↔2). No shared mutable scenario state.

## Config

Optional run-config key: `"knownTraceScenario": "N3-hit-c-gamma"` with `"graphs": ["Logigramme1"]`.
Absence of the key ⇒ catalog bind (Story 2.5).
Unknown id ⇒ fail closed before run.

D-silence ids (`N5-hit-d-silence`, `N9-hit-d-silence`) set `AssertNoClassificationRows=true` so export fails if any Classification data row appears. Catalog mode and ordinary misses keep soft `ExpectClassificationRows=false` (no zero-row assert).
