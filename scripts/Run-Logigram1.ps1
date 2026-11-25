<#
.SYNOPSIS
  One-command local loop for Logigram 1 work (build -> viz -> analysis -> PsiStudio).

.DESCRIPTION
  Extensible step pipeline under saac/scripts. Default runs everything needed to scrub
  Casper5 video + Logigram State/Outcome (+ Spike42 Generateur gaze-hit streams) in PsiStudio
  with the Logigram layout. Unit tests are opt-in via -Tests.

  Add new steps in the script's $StepOrder / switch as stories grow (graph viz, node 3, CSV, ...).

.EXAMPLE
  .\scripts\Run-Logigram1.ps1

.EXAMPLE
  # Same loop, plus unit tests after Build
  .\scripts\Run-Logigram1.ps1 -Tests

.EXAMPLE
  # Code + viz only; reuse existing analysis store; open PsiStudio
  .\scripts\Run-Logigram1.ps1 -SkipAnalysis

.EXAMPLE
  # Recompute analysis + reopen (no rebuild)
  .\scripts\Run-Logigram1.ps1 -Steps Analysis,Open

.EXAMPLE
  # Nudge State/Outcome + gaze-hit timelines vs video (+1.5s), rebuild + reopen
  .\scripts\Run-Logigram1.ps1 -Steps Analysis,GazeHits,Open -ScrubDelaySec 1.5
#>
[CmdletBinding()]
param(
    # Pipeline steps. Use All (default) or a subset.
    [ValidateSet('All', 'Build', 'Test', 'Viz', 'Analysis', 'GazeHits', 'Open')]
    [string[]]$Steps = @('All'),

    [string]$Configuration = 'Debug',
    [string]$Platform = 'x64',

    [string]$CasperPds = 'C:\Users\alexi\Documents\Dev\Ressources\Casper81\Casper5\Casper5.pds',
    [string]$Logigram1Config = '',
    [string]$PsiStudio = 'C:\Users\alexi\Documents\Dev\psi\Sources\Tools\PsiStudio\Microsoft.Psi.PsiStudio\bin\Release\net472\Microsoft.Psi.PsiStudio.exe',
    [string]$Layout = 'Logigram',

    # Signed seconds added to Logigram1.State / Logigram1.Outcome and Spike42.GazeHit OTs (video sync).
    [double]$ScrubDelaySec = 0,

    # Alias for -ScrubDelaySec (kept for the GazeHits-only workflow).
    [double]$GazeDelaySec = 0,

    # Opt-in: run unit tests after Build (not part of default All).
    [switch]$Tests,

    # Convenience skips (same as omitting from -Steps when using All).
    [switch]$SkipBuild,
    [switch]$SkipViz,
    [switch]$SkipAnalysis,
    [switch]$SkipGazeHits,
    [switch]$SkipOpen,

    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$saacRoot = Split-Path $PSScriptRoot -Parent
Set-Location $saacRoot

function Write-Step([string]$name) {
    Write-Host ""
    Write-Host "=== $name ===" -ForegroundColor Cyan
}

function Invoke-DotNet([string[]]$DotNetArgs) {
    & dotnet @DotNetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($DotNetArgs -join ' ') failed with exit $LASTEXITCODE"
    }
}

function Find-BuiltExe([string]$name) {
    $candidates = @(
        (Join-Path $saacRoot "build\bin\$name\$Platform\$Configuration\$name.exe"),
        (Join-Path $saacRoot "build\bin\$name\$Configuration\$name.exe")
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    throw "Built exe not found for $name. Looked under build\bin\$name\"
}

function Get-PsiStudioProcesses {
    # Avoid Get-Process -Name (dots/wildcards); match by ProcessName / path.
    Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -eq 'Microsoft.Psi.PsiStudio' -or
        $_.ProcessName -like '*PsiStudio*' -or
        ($_.Path -and $_.Path -like '*Microsoft.Psi.PsiStudio.exe')
    }
}

function Stop-PsiStudioIfRunning {
    $procs = @(Get-PsiStudioProcesses)
    if ($procs.Count -eq 0) { return }
    $ids = $procs.Id -join ', '
    Write-Host "Stopping PsiStudio so Visualizations.dll can rebuild (PID(s): $ids) ..."
    $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 500
        $procs = @(Get-PsiStudioProcesses)
    } while ($procs.Count -gt 0 -and (Get-Date) -lt $deadline)
    if ($procs.Count -gt 0) {
        throw "PsiStudio still running after stop (PID(s): $($procs.Id -join ', ')). Close it manually and re-run."
    }
    # Extra beat so Windows releases DLL file locks.
    Start-Sleep -Seconds 2
}

# Resolve step list (default All = no tests; -Tests or -Steps Test adds them)
$order = @('Build', 'Test', 'Viz', 'Analysis', 'GazeHits', 'Open')
if ($Steps -contains 'All') {
    $active = [System.Collections.Generic.List[string]]@('Build', 'Viz', 'Analysis', 'GazeHits', 'Open')
} else {
    $active = [System.Collections.Generic.List[string]]@($Steps | Select-Object -Unique)
}

if ($Tests -and -not ($active -contains 'Test')) {
    $buildIdx = $active.IndexOf('Build')
    if ($buildIdx -ge 0) {
        $active.Insert($buildIdx + 1, 'Test')
    } else {
        $active.Insert(0, 'Test')
    }
}

if ($SkipBuild) { [void]$active.Remove('Build') }
if ($SkipViz) { [void]$active.Remove('Viz') }
if ($SkipAnalysis) { [void]$active.Remove('Analysis') }
if ($SkipGazeHits) { [void]$active.Remove('GazeHits') }
if ($SkipOpen) { [void]$active.Remove('Open') }

$devRoot = Split-Path $saacRoot -Parent
$spikeDumpDir = Join-Path $devRoot 'Ressources\_analysis-out\_tmp-dump'
$gazeHitsOutRoot = Join-Path (Split-Path $CasperPds -Parent) (
    [IO.Path]::GetFileNameWithoutExtension($CasperPds) + '.Spike42GazeHits')
$casperServerPath = Join-Path (Split-Path $CasperPds -Parent) 'PreTest.081'

# One scrub offset for State/Outcome + GazeHits (-GazeDelaySec is an alias when ScrubDelaySec left at 0).
$effectiveScrubDelaySec = $ScrubDelaySec
if ($GazeDelaySec -ne 0 -and $ScrubDelaySec -eq 0) {
    $effectiveScrubDelaySec = $GazeDelaySec
}
elseif ($GazeDelaySec -ne 0 -and $ScrubDelaySec -ne 0 -and $GazeDelaySec -ne $ScrubDelaySec) {
    Write-Warning "Both -ScrubDelaySec ($ScrubDelaySec) and -GazeDelaySec ($GazeDelaySec) set; using ScrubDelaySec."
}
$scrubDelayInvariant = $effectiveScrubDelaySec.ToString([cultureinfo]::InvariantCulture)

if ($active.Count -eq 0) {
    throw "No steps left to run. Check -Steps / -Skip* flags."
}

Write-Host "Run-Logigram1 steps: $($active -join ' -> ')" -ForegroundColor Green

if (-not $Logigram1Config) {
    $Logigram1Config = Join-Path $saacRoot 'Applications\Logigram1Analysis\logigram1.json'
}

# Unlock Visualizations.dll before Test/Viz copy (PsiStudio holds those files open).
if ($active -contains 'Test' -or $active -contains 'Viz') {
    Stop-PsiStudioIfRunning
}

foreach ($step in $order) {
    if (-not ($active -contains $step)) { continue }

    switch ($step) {
        'Build' {
            Write-Step 'Build (Logigram1 + Logic + Analysis + launcher)'
            Invoke-DotNet @(
                'build', 'Components\Logigram1\Logigram1.csproj',
                '-c', $Configuration, '-p:Platform=x64'
            )
            Invoke-DotNet @(
                'build', 'Components\Logigram1.Logic\Logigram1.Logic.csproj',
                '-c', $Configuration, '-p:Platform=x64'
            )
            Invoke-DotNet @(
                'build', 'Applications\Logigram1Analysis\Logigram1Analysis.csproj',
                '-c', $Configuration, '-p:Platform=x64'
            )
            Invoke-DotNet @(
                'build', 'Applications\OpenLogigramPsiStudio\OpenLogigramPsiStudio.csproj',
                '-c', $Configuration, '-p:Platform=x64'
            )
        }

        'Test' {
            Write-Step 'Test (Logic + Analysis + contracts + Visualizations)'
            Invoke-DotNet @(
                'test', 'UnitTests\Logigram1.Logic.Tests\Logigram1.Logic.Tests.csproj',
                '-c', $Configuration, '-p:Platform=x64', '--no-restore'
            )
            Invoke-DotNet @(
                'test', 'UnitTests\Logigram1Analysis.Tests\Logigram1Analysis.Tests.csproj',
                '-c', $Configuration, '-p:Platform=x64', '--no-restore'
            )
            Invoke-DotNet @(
                'test', 'UnitTests\Logigram1.Tests\Logigram1.Tests.csproj',
                '-c', $Configuration, '-p:Platform=x64', '--no-restore'
            )
            try {
                Invoke-DotNet @(
                    'test', 'UnitTests\Visualizations.Tests\Visualizations.Tests.csproj',
                    '-c', $Configuration, '-p:Platform=x64', '--no-restore'
                )
            } catch {
                Write-Warning "Visualizations.Tests failed (often PsiStudio DLL lock). Quit PsiStudio and re-run, or use -Steps Viz first. $_"
            }
        }

        'Viz' {
            Write-Step 'Viz (rebuild Visualizations.dll for PsiStudio Additional Assemblies)'
            Stop-PsiStudioIfRunning
            Invoke-DotNet @(
                'build', 'Components\Visualizations\Visualizations.csproj',
                '-c', $Configuration, '-p:Platform=x64'
            )
            Write-Host "Plugin output: build\bin\Components\Visualizations\$Platform\$Configuration\net48\Visualizations.dll"
        }

        'Analysis' {
            Write-Step 'Analysis (re-run Logigram1Analysis -> sibling *.Logigram1Analysis.pds)'
            if (-not (Test-Path $Logigram1Config)) {
                throw "Config not found: $Logigram1Config"
            }
            $analysisExe = Find-BuiltExe 'Logigram1Analysis'
            Write-Host "Exe: $analysisExe"
            Write-Host "Config: $Logigram1Config"
            Write-Host "ScrubDelaySec: $scrubDelayInvariant (State/Outcome OT shift)"
            & $analysisExe '--config' $Logigram1Config '--scrub-delay-sec' $scrubDelayInvariant
            if ($LASTEXITCODE -ne 0) {
                throw "Logigram1Analysis exited $LASTEXITCODE"
            }
        }

        'GazeHits' {
            Write-Step 'GazeHits (Spike42 sphere-raycast Generateur hits -> scrub streams)'
            $csproj = Join-Path $spikeDumpDir 'Spike42GazeHits.csproj'
            if (-not (Test-Path $csproj)) {
                throw "Spike42GazeHits.csproj not found: $csproj"
            }
            if (-not (Test-Path $casperServerPath)) {
                throw "Casper Server store path not found: $casperServerPath (expected PreTest.081 next to Casper5.pds)"
            }
            Invoke-DotNet @(
                'build', $csproj,
                '-c', 'Release'
            )
            $gazeExe = Join-Path $spikeDumpDir 'bin\Release\net48\Spike42GazeHits.exe'
            if (-not (Test-Path $gazeExe)) {
                throw "Spike42GazeHits.exe not found after build: $gazeExe"
            }
            Write-Host "Exe: $gazeExe"
            Write-Host "Server: $casperServerPath"
            Write-Host "Out: $gazeHitsOutRoot"
            Write-Host "ScrubDelaySec: $scrubDelayInvariant (Spike42.GazeHit OT = raw + delay)"
            & $gazeExe `
                '--store-name' 'Server' `
                '--store-path' $casperServerPath `
                '--out-root' $gazeHitsOutRoot `
                '--delay-sec' $scrubDelayInvariant
            if ($LASTEXITCODE -ne 0) {
                throw "Spike42GazeHits exited $LASTEXITCODE"
            }
        }

        'Open' {
            Write-Step 'Open (combined Casper + analysis [+ GazeHits] Dataset -> PsiStudio / Logigram layout)'
            $launcher = Find-BuiltExe 'OpenLogigramPsiStudio'
            # Ensure launcher is current even if Build was skipped
            if (-not ($active -contains 'Build')) {
                Invoke-DotNet @(
                    'build', 'Applications\OpenLogigramPsiStudio\OpenLogigramPsiStudio.csproj',
                    '-c', $Configuration, '-p:Platform=x64'
                )
                $launcher = Find-BuiltExe 'OpenLogigramPsiStudio'
            }
            $argList = @(
                '--casper', $CasperPds,
                '--psi-studio', $PsiStudio,
                '--layout', $Layout,
                '--gaze-hits', $gazeHitsOutRoot
            )
            if ($NoLaunch) { $argList += '--no-launch' }
            & $launcher @argList
            if ($LASTEXITCODE -ne 0) {
                throw "OpenLogigramPsiStudio exited $LASTEXITCODE"
            }
        }
    }
}

Write-Host ""
Write-Host "Run-Logigram1 finished: $($active -join ' -> ')" -ForegroundColor Green
