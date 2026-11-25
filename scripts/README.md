# Developer Scripts

This directory contains optional scripts for SAAC developers.

## Run-Logigram1.ps1 (main entry)

One command for the full Logigram 1 loop: **build → Visualizations.dll → re-run analysis → open PsiStudio** (Casper video + analysis, Logigram layout). Unit tests are opt-in.

```powershell
cd saac
.\scripts\Run-Logigram1.ps1
```

Useful variants:

```powershell
# Full loop (auto-stops PsiStudio if needed so Viz can rebuild the DLL, then reopens it)
.\scripts\Run-Logigram1.ps1

# Same + unit tests after Build
.\scripts\Run-Logigram1.ps1 -Tests

# After Logic-only change: rebuild, re-analyze, open (skip viz)
.\scripts\Run-Logigram1.ps1 -Steps Build,Analysis,Open

# Viz-only change
.\scripts\Run-Logigram1.ps1 -Steps Viz,Open -SkipAnalysis

# Re-open PsiStudio only
.\scripts\Run-Logigram1.ps1 -Steps Open

# Nudge State/Outcome + gaze hits vs video (+1.5s), rebuild analysis + hits, open
.\scripts\Run-Logigram1.ps1 -Steps Analysis,GazeHits,Open -ScrubDelaySec 1.5

# Nudge the video instead: delayed copy of VideoRemoteApp/FullScreen into a sibling PostProcess store (+1.5s), open
.\scripts\Run-Logigram1.ps1 -Steps DelayVideo,Open -VideoDelaySec 1.5
```

**Steps** (extend this list as epics grow): `Build`, `Viz`, `Analysis`, `GazeHits`, `DelayVideo`, `Open` (+ `Test` via `-Tests`). `DelayVideo` is opt-in: it is not part of `All` (it copies the video into a sibling PostProcess store without mutating Casper, ~3.6 GB for Casper5).

**VideoDelaySec** (signed seconds, `DelayVideo` only): `Applications/DelayedStreamCopy` reads `VideoRemoteApp/FullScreen` (`Shared<EncodedImage>`) from the Casper store, and writes `Casper5.PostProcess/PostProcess.0000` with the **same stream name (`FullScreen`), payloads and type**, every OriginatingTime = raw + delay (positive = later). Independent of `-ScrubDelaySec` (State/Outcome/GazeHits keep using that). The Casper input store is never modified; re-running replaces the PostProcess output. `Open` adds the `PostProcess` partition to `Casper5.WithLogigram1.pds` beside the original `VideoRemoteApp`; in PsiStudio scrub `PostProcess`/`FullScreen` for the delayed timeline. The tool fails non-zero if the source partition/stream is missing or empty. Direct use: `DelayedStreamCopy.exe --casper <Casper5.pds> [--partition VideoRemoteApp] [--stream FullScreen] [--delay-sec <s>] [--out-root <dir>] [--no-pds]` (one source stream per run).

**ScrubDelaySec** (signed seconds): shifts persisted `Logigram1.State` / `Logigram1.Outcome` and the single `Spike42.GazeHit` stream for video sync. `-GazeDelaySec` is an alias when `-ScrubDelaySec` is left at 0. Re-run Analysis/GazeHits to apply a new delay.

Related tools:

| Script / app | Role |
|--------------|------|
| `Run-Logigram1.ps1` | Orchestrator |
| `Open-LogigramPsiStudio.ps1` | Thin wrapper → `-Steps Open` |
| `Applications/OpenLogigramPsiStudio/` | Builds `Casper5.WithLogigram1.pds` + launches PsiStudio |
| `Applications/DelayedStreamCopy/` | `DelayVideo`: delayed `PostProcess` copy of `FullScreen` |

## Setup-DevSymlinks.ps1

Optional script for creating symbolic links for shared files across multiple applications.

### Purpose

Some files are shared across multiple SAAC applications (e.g., `UiGenerator.cs` is used in ServerApplication, CameraRemoteApp, VideoRemoteApp, and WhisperRemoteApp). By default, these files exist as regular copies in each location.

Developers who frequently edit these shared files can optionally use symbolic links to maintain a single source file that automatically updates all linked locations.

### Requirements

**Windows 10/11:**
- **Recommended:** Enable Developer Mode (Settings → Privacy & security → For developers → Developer mode)
- **Alternative:** Run PowerShell as Administrator

**Earlier Windows versions:**
- Must run PowerShell as Administrator

### Usage

```powershell
# Create symbolic links
.\scripts\Setup-DevSymlinks.ps1

# Remove symbolic links (restore to regular files)
.\scripts\Setup-DevSymlinks.ps1 -Remove

# Force operation without confirmation prompts
.\scripts\Setup-DevSymlinks.ps1 -Force

# Combine options
.\scripts\Setup-DevSymlinks.ps1 -Remove -Force
```

### What It Does

**When creating symlinks:**
1. Checks for appropriate permissions (Admin or Developer Mode)
2. Verifies target files exist
3. Backs up existing files if they differ from targets
4. Creates symbolic links pointing to the source file
5. Reports success/failure for each operation

**When removing symlinks:**
1. Identifies symbolic links
2. Removes them (regular file copies remain in repository)
3. Reports results

### Configuration

The script configuration is defined in the `$symlinks` hashtable within the script:

```powershell
$symlinks = @{
    "Applications\VideoRemoteApp\UiGenerator.cs" = "Applications\CameraRemoteApp\UiGenerator.cs"
    "Applications\WhisperRemoteApp\UiGenerator.cs" = "Applications\CameraRemoteApp\UiGenerator.cs"
    "Applications\ServerApplication\UiGenerator.cs" = "Applications\CameraRemoteApp\UiGenerator.cs"
}
```

**Key:**
- Relative path to the symbolic link
- This location will point to the target

**Value:**
- Relative path to the target file
- This is the actual file that will be edited

To add more shared files, simply add entries to this hashtable.

### Important Notes

1. **Optional:** Symlinks are completely optional. The repository works fine without them.

2. **Local Only:** Symlinks are for local development only. They are NOT committed to the repository.

3. **Git Behavior:** Git stores the file contents (not the symlink) thanks to `.gitattributes` configuration.

4. **Backup Safety:** The script creates `.backup` files before replacing existing files.

5. **Cross-Application Editing:** When symlinks are active, editing any linked file updates all linked locations automatically.

### Troubleshooting

**"Administrator privilege required" error:**
- Enable Developer Mode (Settings → Privacy & security → For developers)
- OR run PowerShell as Administrator

**"Target does not exist" error:**
- Ensure the repository is fully cloned
- Check that the source file exists
- Verify you're running the script from the repository root

**Git shows modified files after creating symlinks:**
- This is expected behavior
- Don't commit these files while symlinks are active
- OR remove symlinks before committing: `.\scripts\Setup-DevSymlinks.ps1 -Remove`

**Symlink points to wrong location:**
- Remove and recreate: `.\scripts\Setup-DevSymlinks.ps1 -Remove` then `.\scripts\Setup-DevSymlinks.ps1`
- OR use `-Force` to replace: `.\scripts\Setup-DevSymlinks.ps1 -Force`

### For New Developers

If you're new to the project and just want to build and run SAAC:
- **You don't need to run this script**
- The regular file copies work perfectly
- Symlinks are only useful if you're actively developing shared code

### For Maintainers

To add new shared files:
1. Edit the `$symlinks` hashtable in `Setup-DevSymlinks.ps1`
2. Add corresponding entries to `.gitattributes`
3. Update this README
4. Document in the main Installation guide

### See Also

- [Installation Guide](https://github.com/SaacPSI/saac/wiki/Installation) - Complete SAAC setup instructions
- [README.md](../README.md) - Main project README
