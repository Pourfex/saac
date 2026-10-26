# Thin wrapper — prefer Run-Logigram1.ps1 for the full loop.
# Equivalent to: .\scripts\Run-Logigram1.ps1 -Steps Open

param(
    [string]$CasperPds = 'C:\Users\alexi\Documents\Dev\Ressources\Casper81\Casper5\Casper5.pds',
    [string]$Analysis = '',
    [string]$PsiStudio = 'C:\Users\alexi\Documents\Dev\psi\Sources\Tools\PsiStudio\Microsoft.Psi.PsiStudio\bin\Release\net472\Microsoft.Psi.PsiStudio.exe',
    [string]$Layout = 'Logigram',
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Run-Logigram1.ps1') `
    -Steps Open `
    -CasperPds $CasperPds `
    -PsiStudio $PsiStudio `
    -Layout $Layout `
    -SkipBuild:$false `
    -NoLaunch:$NoLaunch
# Note: -Analysis is resolved by OpenLogigramPsiStudio defaults (sibling of Casper).
exit $LASTEXITCODE
