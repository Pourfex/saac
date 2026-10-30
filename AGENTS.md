<!-- bmad:context -->
<!-- Verified 2026-10-30 against 9ae131107bf241bf4b4b8ed1f2c80eb50b3118dc. Managed by bmad-project-context; edits inside this block are replaced on refresh. Keep anything you want preserved outside the markers. -->

## SAAC (System As A Collaborator)

.NET Framework 4.8 / x64 multimodal pipelines on a forked Microsoft Platform for Situated Intelligence (ψ). Work targets PsiStudio-compatible apps and components. Logigram 1 offline analysis is active product work (Gamma path shipped). SAAC docs: `docs/wiki/`, root `*Modification*.md`; ψ concepts: `../psi.wiki/`; planning: `../_bmad-output/`.

## Policy

- Never modify sibling trees `../psi/`, `../psi.wiki/`, or `../CollaborationAnalysisFramework/` — read-only reference. All edits stay in this repo (`saac`).
- Prefer existing ψ packages, types, and patterns (streams, pipelines, remoting, rendezvous, stores). Do not reinvent ψ primitives in SAAC.
- Before inventing SAAC-side behavior, read the relevant ψ concept (via `../psi.wiki/` or SAAC docs that cite the fork). If the capability already exists in ψ or SAAC components, reuse it.
- Treat Logigram node/outcome rules as human-gated: do not implement straight from workflow diagrams or a single resource reading — confirm each step with a human against `Ressources/Logigramme1.md` + `Ressources/PSI_EVENTS_README.md` (and live Casper topics). Those sources are easy to misread; straight-through implementations have already gone wrong.
- Freeze unless a story explicitly opens them: `Logigram1.GammaPath` FSM, `LogigramOutcomeWriter`, and Analysis OT-merge (`GammaPathTopicOrdering`). Extend with new path units; do not rewrite Gamma “while here”.
- Only `LogigramOutcomeWriter` emits `Logigram1.Outcome` / regulation labels; Logic units emit State/Evidence/decisions only; viz/CSV never invent Alpha|Beta|Gamma.
- Never mutate input Casper `.pds` stores; write sibling analysis output only.

## Where things are

- Apps: `Applications/` — PsiStudio plugin template `Applications/SaaCPsiStudio/`; coordination hub `Applications/ServerApplication/`; remotes under `*RemoteApp/`.
- Logigram 1: console `Applications/Logigram1Analysis/` (`logigram1.json`); contracts `Components/Logigram1/`; Logic `Components/Logigram1.Logic/`; viz `Components/Visualizations/` (contracts-only refs).
- Shared pipeline/RendezVous types: `Components/PipelineServices/` (not the broken `RendezVousPipelineServices` path in some READMEs).
- Components / Interop: `Components/`, `Interop/`. Exclude `Dependencies/` from searches unless changing native interop wrappers.
- Topic / Logigram truth: `Ressources/Logigramme1.md`, `Ressources/PSI_EVENTS_README.md` (also under `../Ressources/`).
- Epic 3 context (durable): `../_bmad-output/implementation-artifacts/epic-3-context.md`. Architecture: `../_bmad-output/initiative-saac/architecture-saac/architecture-saac.md`. Backlog: `../_bmad-output/planning-artifacts/epics.md`.
- SAAC wiki mirror: `docs/wiki/` — prefer `README.md` over `docs/wiki/Installation.md` when they disagree (stale Psi paths in the wiki).
- Fork behavior notes: `PsiStudioModifications.md`, `RemoteExporterModification.md`, `PsiInUnity.md`; replay detail: `docs/wiki/ReplayPipeline-Guide.md`.
- Event/logigramme contracts: both `Ressources/` (in-repo) and `../Ressources/` (Dev — same trio plus Casper/Miro fixtures). Use both; do not treat Dev zip dumps (`10.zip`, `Backup_Exp_Casper.zip`) as source of truth.
- ψ concepts (read-only): `../psi.wiki/` — start with Writing-Components, Pipeline-Execution, Delivery-Policies, Remoting, Rendezvous-System, Psi-Studio.
- ψ fork sources (read-only): `../psi/` on branch `PsiStudio`; local NuGet feed `../psi/Build/PsiPackages/`.

## Running and verifying

- Restore/build this repo with `saac.sln` (platform **x64**). After a Psi package rebuild: from psi run `.\Build\PsiPackages\Install-LocalPackages.ps1` (clears NuGet cache only), then `dotnet restore saac.sln` here. NuGet source **Psi Local Packages** → `../psi/Build/PsiPackages`.
- Never build individual psi `.csproj` files alone — `SolutionDir` breaks and packages land under `C:\Build\PsiPackages`.
- Missing `Microsoft.Psi.*-beta-SAAC` on restore means the local feed lacks that package; rebuild Psi from root `Psi.sln` (Release | Any CPU or x64), then clear cache + restore.
- Logigram loop from `saac/`: `.\scripts\Run-Logigram1.ps1` (Build → Viz → Analysis → Open). Use `-Tests` when changing Logic/Analysis/contracts. Full options under **Logigram local verify** below.
- Author scrub verification is in **PsiStudio** (layout `Logigram`); unit tests do not replace scrub. No CI gate in this repo.

## Conventions that differ from defaults

- Consume ψ via the local **-SAAC** / Psi Local Packages feed from the sibling fork — not stock microsoft/psi NuGets alone.
- New work should integrate with PsiStudio (pipeline plugins / RendezVous / remoting as appropriate), not as one-off consoles unless asked.
- Sole enableable Gamma unit: `Logigram1.GammaPath`. Warn+ignore legacy `Logigram1.FirstUnit` / `Logigram1.Node3` — do not restore them as producers.
- Gamma cycle per `(BatteryId, PersonId)`: node-1 (module create) → 2s window **from create** → door closed + Select/Validation in window = node-2 **reset, no Gamma**; else leave `GeneratorArea` + (door closed or hand-near Generateur AABB) = node-3 → Gamma (C) via Outcome writer only. Side-keyed topics; FANOUT_BOTH on new Module status id.
- Analysis must OT-merge the Gamma topic union (`GammaPathTopicOrdering`) before the FSM — never rely on multi-importer delivery order.
- Battery scopes = Spawn→Finish FIFO in Analysis; never invent Battery keys.
- Visualizations bind `Logigram1` contracts only — never reference `Logigram1.Logic`. Honest empty labels when nothing at playhead; one Graph panel per Battery (P1 yellow / P2 orange). Status prefixes `node-1:` / `node-2:` / `node-3:` are the parse contract.

## Known pitfalls

- `Install-LocalPackages.ps1` does not install packages; it only deletes matching entries under `%USERPROFILE%\.nuget\packages`.
- Optional `scripts/Setup-DevSymlinks.ps1` for shared `UiGenerator.cs` — do not commit while symlinks are active.
- Stories 3.1/3.3 and some planning text still describe Gamma-on-node-2 or FirstUnit/Node3 as current — superseded; follow 3.4–3.6, code, and `epic-3-context.md`.
- Skipping OT-merge yields orphan Gammas and collapsed State times on Casper (e.g. Module status before earlier-OT Area).
- Do not reintroduce a State OT remapper that moves node-3 State off leave OT while Outcomes keep leave OT.
- Do not wire `M?-ModuleOut` into GammaPath; do not invent door world positions (use `ObjectBound` Generateur AABB).
- Type name `FirstUnitPipelineRunner` still runs GammaPath — legacy name only.

<!-- /bmad:context -->

## Logigram local verify (manual — keep across bmad refresh)

From `saac/`, prefer:

```powershell
.\scripts\Run-Logigram1.ps1
```

Default steps: **Build → Viz → Analysis → Open**. Stops PsiStudio automatically before Test/Viz if it holds `Visualizations.dll` locks, then Open relaunches it. Extend this script when new verify steps appear (graph viz, CSV, …); do not invent ad-hoc one-offs. Details also in `scripts/README.md`.

### Common invocations

```powershell
.\scripts\Run-Logigram1.ps1                              # full default loop
.\scripts\Run-Logigram1.ps1 -Tests                       # same + unit tests after Build
.\scripts\Run-Logigram1.ps1 -Steps Viz,Open              # viz-only change
.\scripts\Run-Logigram1.ps1 -Steps Build,Analysis,Open   # logic change, skip viz
.\scripts\Run-Logigram1.ps1 -Steps Analysis,Open         # re-analyze + reopen
.\scripts\Run-Logigram1.ps1 -Steps Open                  # reopen PsiStudio only
.\scripts\Run-Logigram1.ps1 -SkipAnalysis                # reuse existing analysis store
```

### Parameters

| Parameter | Default | Meaning |
|-----------|---------|---------|
| `-Steps` | `All` | Pipeline subset. `All` = Build, Viz, Analysis, Open (no Test). Values: `All`, `Build`, `Test`, `Viz`, `Analysis`, `Open` (array OK). |
| `-Tests` | off | Opt-in: insert `Test` after `Build`. |
| `-SkipBuild` / `-SkipViz` / `-SkipAnalysis` / `-SkipOpen` | off | Drop that step from the active list. |
| `-Configuration` | `Debug` | `dotnet` `-c`. |
| `-Platform` | `x64` | Build platform. |
| `-CasperPds` | `..\Ressources\Casper81\Casper5\Casper5.pds` | Input Casper Dataset (never mutate). |
| `-Logigram1Config` | `Applications\Logigram1Analysis\logigram1.json` | Analysis config. |
| `-PsiStudio` | `..\psi\...\Microsoft.Psi.PsiStudio.exe` (Release net472) | PsiStudio exe. |
| `-Layout` | `Logigram` | PsiStudio layout name. |
| `-NoLaunch` | off | Build combined `.pds` but do not start PsiStudio. |

Related: `scripts/Open-LogigramPsiStudio.ps1` → thin wrapper for `-Steps Open`. Combined catalog: `Casper5.WithLogigram1.pds` (via `OpenLogigramPsiStudio`). Additional Assemblies: `build/bin/Components/Visualizations/x64/Debug/net48/Visualizations.dll`.
