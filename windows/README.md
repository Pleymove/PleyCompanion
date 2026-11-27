# PleyCompanion pour Windows

PleyCompanion est issu du port Windows d’OpenClicky, écrit et maintenu en amont par Tan Wei Jie (Justin). Le stage de dessin et les modèles 3D ont notamment été apportés par darkworks. Le fork conserve la [licence MIT](../LICENSE) et les attributions d’origine. [Tan Wei Jie (Justin)](https://github.com/weijietan97) · [darkworks](https://github.com/darkworks) · [OpenClicky](https://github.com/prasanthsasikumar/openclicky)

## Audit de l’architecture héritée

| Composant | Rôle |
|---|---|
| `OpenClicky.Windows.sln` | Solution C# qui regroupe le client, Core et les tests. |
| `src/OpenClicky.Core` | Bibliothèque .NET 8. Logique de dictée, voix, règles d’interface, fournisseurs et contrats de plateforme. |
| `src/OpenClicky.Windows` | Exécutable WPF .NET 8. `Program.cs` traite les hooks Velopack, les processus auxiliaires Sherpa et l’instance unique. `AppComposition.cs` relie Core aux adaptateurs Win32, UI Automation, NAudio et à l’interface. WebView2 héberge le stage de dessin. |
| `tests/OpenClicky.Core.Tests` | Suite xUnit. Certains tests couvrent des comportements Windows, dont PATH, PATHEXT et les shims npm. |
| `installer/build-release.ps1` | Publication autonome .NET `win-x64` et packaging par Velopack. Le script télécharge le modèle Moonshine public, vérifie son SHA-256 et conserve sa licence. |

La chaîne héritée conditionnait le job au dépôt amont et savait publier sur ses GitHub Releases. Le client utilisait ce même dépôt pour les mises à jour, et partageait avec OpenClicky les chemins de configuration et de données, la clé de démarrage et le verrou d’instance. Le workflow du fork compile la révision choisie sur un runner Windows et conserve l’installateur dans le run Actions. Le [guide de portage](PORTING.md) documente les correspondances entre les sources Windows et macOS.

## Compiler et tester en local

Prérequis sur Windows 11 x64. PowerShell 7.4 ou ultérieur et le SDK .NET indiqué dans [global.json](global.json). WebView2 Runtime est requis pour le stage de dessin.

Depuis la racine du dépôt, dans PowerShell.

```powershell
Set-Location windows
dotnet build OpenClicky.Windows.sln -c Release -p:RestoreLockedMode=true
dotnet test tests/OpenClicky.Core.Tests -c Release --no-build --logger 'trx;LogFileName=windows-x64.trx'
dotnet run --project src/OpenClicky.Windows
```

Le build CI et le script de packaging utilisent les fichiers de verrouillage NuGet en mode verrouillé. Pour une mise à jour volontaire des dépendances, lancer `dotnet restore OpenClicky.Windows.sln --force-evaluate` puis vérifier et versionner les changements des fichiers `packages.lock.json`.

Le client s’ouvre dans la zone de notification. Lancez-le une seconde fois ou choisissez **Open PleyCompanion** pour afficher sa fenêtre.

## Identité et cohabitation

| Élément | PleyCompanion |
|---|---|
| Exécutable et raccourcis | `PleyCompanion.exe`, **PleyCompanion** |
| Package et installation par utilisateur | `PleyCompanion.Windows`, sous `%LOCALAPPDATA%\PleyCompanion.Windows` |
| Données | `%LOCALAPPDATA%\PleyCompanion` contient `shell.json`, préférences, historique, modèles téléchargés, profils WebView2, skills et journaux. |
| Espace agent par défaut | `%USERPROFILE%\PleyCompanion` |
| Démarrage à la connexion | Valeur `PleyCompanion` dans `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| Instance unique | Mutex et canal IPC sous le nom `PleyCompanion`. |
| Futur flux Velopack | Canal `pleycompanion-win-x64` réservé aux GitHub Releases de Pleymove/PleyCompanion. Les contrôles manuels, automatiques et l’application au démarrage sont désactivés en phase 1. |

La désinstallation Velopack cible le dossier du package. Les données sont stockées à côté dans leur propre dossier. Le fork démarre avec ses propres réglages, sans migration des données OpenClicky. Les raccourcis globaux hérités peuvent se chevaucher quand les deux applications tournent. Le backend et les intégrations hérités gardent leur configuration et se règlent indépendamment du flux de mise à jour.

### Vérifier la cohabitation sur Windows 11 x64

1. Avec OpenClicky déjà installé, installer PleyCompanion et vérifier les deux entrées dans **Paramètres → Applications → Applications installées** et dans le menu Démarrer.
2. Lancer chaque application et vérifier qu’un second lancement de PleyCompanion ouvre sa propre fenêtre. Vérifier que les réglages et journaux sont dans les dossiers respectifs. Effectuer le test depuis le menu Démarrer pour éviter les raccourcis globaux communs.
3. Vérifier que **Check Now** et la recherche quotidienne sont désactivés dans PleyCompanion. Désinstaller PleyCompanion puis relancer OpenClicky et vérifier ses réglages.

## Debug flags

| Flag | What |
|---|---|
| `--openclicky-capturable-windows` (or `OPENCLICKY_CAPTURABLE_WINDOWS=1`) | let screenshots see the island, overlay and orb (normally excluded from every capture) |
| `--openclicky-ui-demo [--shots <folder>]` | walks the floating UI through its states, saving screenshots |
| `--openclicky-window-demo [--shots <folder>] [--page <page>] [--tour]` | the window over a seeded throwaway data folder |
| `--openclicky-platform-smoke` | exercises the OS layer on this PC and prints the results |
| `--openclicky-smoke-run "<text>"` | the agent gate + a Codex run through the `openclicky` CLI |
| `--openclicky-smoke-transcribe <wav> [moonshine\|offline\|parakeet\|sarvam\|openclicky\|assemblyai]` | one dictation engine on a recording |
| `--openclicky-voice-smoke <voice>` | renders two sentences in one voice (`openai`, `kokoro`, `mac`…) |
| `--openclicky-check-source <source>` | Settings' Test button for one answer source (`anthropic`, `openai`…) |
| `--openclicky-answer-smoke` | one real question (a red square) through the answer route |
| `--openclicky-drawstage-smoke [program.js \| reply:<file> \| lesson:<question>] [--speech] [--voice] [--shot <png>]` | streams a drawing program, or a whole drawing answer, onto the drawing stage on the cursor's screen ([docs/draw-stage.md](../docs/draw-stage.md)) |

The smoke flags write to the console they were started from and exit with 0 on success. The
`--openclicky-kokoro-helper` / `--openclicky-parakeet-helper` / `--openclicky-moonshine-helper` flags are the app's own helper processes.

## Générer et récupérer un installateur x64

Le workflow [Windows x64 - build, tests and installer](https://github.com/Pleymove/PleyCompanion/actions/workflows/windows-release.yml) s’exécute sur `windows-2025`. Il compile la solution et exécute la suite Core sur un runner Windows x64. Les pull requests qui touchent le client ou le workflow produisent aussi l’installateur pour revue. Le résumé du run et l’artefact `windows-x64-test-results` donnent le résultat des tests, au format TRX.

1. Depuis **Actions → Windows x64 - build, tests and installer**, sélectionner **Run workflow** puis la branche ou le tag à compiler. GitHub affiche la commande de lancement manuel une fois le déclencheur présent sur la branche par défaut.
2. Laisser `version` vide pour utiliser la version de `windows/Directory.Build.props`, ou saisir `X.Y.Z` ou `X.Y.Z-tag.N` sans préfixe `v`. Sélectionner **Run workflow**.
3. Après réussite, ouvrir **Summary → Artifacts → PleyCompanion-windows-x64**, puis extraire le ZIP. Il contient `PleyCompanion-Setup.exe` et `SHA256SUMS.txt`.

Avec GitHub CLI authentifié, les commandes équivalentes sont les suivantes. Remplacer `<id-du-run>` par le numéro du run affiché par GitHub.

```powershell
gh workflow run windows-release.yml --repo Pleymove/PleyCompanion --ref main
gh run list --repo Pleymove/PleyCompanion --workflow windows-release.yml --event workflow_dispatch
gh run watch <id-du-run> --repo Pleymove/PleyCompanion --exit-status
gh run download <id-du-run> --repo Pleymove/PleyCompanion --name PleyCompanion-windows-x64
```

Le même script crée l’installateur localement, depuis la racine du dépôt.

```powershell
./windows/installer/build-release.ps1
# Avec une version explicite
./windows/installer/build-release.ps1 -Version 0.12.1-dev.1
```

Le résultat se trouve dans `windows/Releases/win-x64/`. Pour vérifier le fichier extrait, comparer la sortie de `Get-FileHash .\PleyCompanion-Setup.exe -Algorithm SHA256` à `SHA256SUMS.txt`. L’installateur de phase 1 est non signé et destiné à la revue. Respecter les décisions de SmartScreen et les politiques du poste. [Statut de la signature](docs/code-signing.md)

Les tests CI exercent Core. La validation interactive de l’installation côte à côte, du microphone et de l’affichage se fait sur Windows 11 x64.
