<!-- bmad:context -->
<!-- Verified 2026-10-20 against f31cd9e7b30dc587d0d53df4c7ca42b6e9f805be. Managed by bmad-project-context; edits inside this block are replaced on refresh. Keep anything you want preserved outside the markers. -->

## SAAC (System As A Collaborator)

.NET Framework 4.8 / x64 multimodal pipelines on a forked Microsoft Platform for Situated Intelligence (ψ). Work targets PsiStudio-compatible apps and components. SAAC-specific docs: `docs/wiki/`, root `*Modification*.md`; ψ concepts: sibling `../psi.wiki/`.

## Policy

- Never modify sibling trees `../psi/`, `../psi.wiki/`, or `../CollaborationAnalysisFramework/` — read-only reference. All edits stay in this repo (`saac`).
- Prefer existing ψ packages, types, and patterns (streams, pipelines, remoting, rendezvous, stores). Do not reinvent ψ primitives in SAAC.
- Before inventing SAAC-side behavior, read the relevant ψ concept (via `../psi.wiki/` or SAAC docs that cite the fork). If the capability already exists in ψ or SAAC components, reuse it.

## Where things are

- Apps: `Applications/` — PsiStudio plugin template `Applications/SaaCPsiStudio/`; coordination hub `Applications/ServerApplication/`; remotes under `*RemoteApp/`.
- Shared pipeline/RendezVous types: `Components/PipelineServices/` (not the broken `RendezVousPipelineServices` path in some READMEs).
- Components / Interop: `Components/`, `Interop/`. Exclude `Dependencies/` from searches unless changing native interop wrappers.
- SAAC wiki mirror: `docs/wiki/` — prefer `README.md` over `docs/wiki/Installation.md` when they disagree (stale Psi paths in the wiki).
- Fork behavior notes: `PsiStudioModifications.md`, `RemoteExporterModification.md`, `PsiInUnity.md`; replay detail: `docs/wiki/ReplayPipeline-Guide.md`.
- Event/logigramme contracts: both `Ressources/` (in-repo) and `../Ressources/` (Dev — same trio plus Casper/Miro fixtures). Use both; do not treat Dev zip dumps (`10.zip`, `Backup_Exp_Casper.zip`) as source of truth.
- ψ concepts (read-only): `../psi.wiki/` — start with Writing-Components, Pipeline-Execution, Delivery-Policies, Remoting, Rendezvous-System, Psi-Studio.
- ψ fork sources (read-only): `../psi/` on branch `PsiStudio`; local NuGet feed `../psi/Build/PsiPackages/`.

## Running and verifying

- Restore/build this repo with `saac.sln` (platform **x64**). After a Psi package rebuild: from psi run `.\Build\PsiPackages\Install-LocalPackages.ps1` (clears NuGet cache only), then `dotnet restore saac.sln` here. NuGet source **Psi Local Packages** → `../psi/Build/PsiPackages`.
- Never build individual psi `.csproj` files alone — `SolutionDir` breaks and packages land under `C:\Build\PsiPackages`.
- Missing `Microsoft.Psi.*-beta-SAAC` on restore means the local feed lacks that package; rebuild Psi from root `Psi.sln` (Release | Any CPU or x64), then clear cache + restore.
- Author verification is in **PsiStudio** (load/run the pipeline or plugin). Prefer building the touched app/component so Alexis can test there; do not assume CI or a documented `dotnet test` gate.

## Conventions that differ from defaults

- Consume ψ via the local **-SAAC** / Psi Local Packages feed from the sibling fork — not stock microsoft/psi NuGets alone.
- New work should integrate with PsiStudio (pipeline plugins / RendezVous / remoting as appropriate), not as one-off consoles unless asked.

## Known pitfalls

- `Install-LocalPackages.ps1` does not install packages; it only deletes matching entries under `%USERPROFILE%\.nuget\packages`.
- Optional `scripts/Setup-DevSymlinks.ps1` for shared `UiGenerator.cs` — do not commit while symlinks are active.

<!-- /bmad:context -->
