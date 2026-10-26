# OpenLogigramPsiStudio

Mini launcher: merges **Casper** (video/audio/Server) + **Logigram1Analysis** into one Dataset session, selects the **Logigram** PsiStudio layout, and starts PsiStudio.

Does **not** copy video or mutate `Casper5.pds` — only writes a sibling `Casper5.WithLogigram1.pds` catalog.

## Quick start

From `saac/`:

```powershell
.\scripts\Open-LogigramPsiStudio.ps1
```

Or:

```powershell
dotnet build Applications/OpenLogigramPsiStudio/OpenLogigramPsiStudio.csproj -c Debug -p:Platform=x64
.\build\bin\OpenLogigramPsiStudio\x64\Debug\OpenLogigramPsiStudio.exe
```

## Options

| Flag | Default |
|------|---------|
| `--casper <path.pds>` | `…\Ressources\Casper81\Casper5\Casper5.pds` |
| `--analysis <path.pds\|storeDir>` | Sibling `Casper5.Logigram1Analysis.pds` / folder |
| `--psi-studio <exe>` | Fork build under `Dev\psi\…\Microsoft.Psi.PsiStudio.exe` |
| `--layout <name>` | `Logigram` (`Documents\PsiStudio\Layouts\Logigram.plo`) |
| `--no-launch` | Only write the combined `.pds` |

Save the **Logigram** layout once in PsiStudio (Layouts → save as `Logigram`) if the `.plo` is missing.
