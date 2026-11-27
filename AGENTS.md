# PleyCompanion

PleyCompanion est un fork sous licence MIT d’OpenClicky. La plateforme prioritaire est Windows 11 x64. macOS pourra être pris en charge ultérieurement.

## Vision

- Curseur IA animé, guidage visuel et compréhension de l’écran en temps réel.
- Conversation vocale naturelle en français et dictée vocale universelle.
- Launcher Alt + Espace pour rechercher des fichiers, lancer des applications et exécuter des commandes.
- Outils locaux partagés entre le launcher et l’assistant.
- Fournisseurs IA interchangeables, dont Hermes, ChatGPT, Claude et les API compatibles.
- Remplacement progressif de HeyClicky et Raycast sans abonnement supplémentaire obligatoire.

## Phase 1

- Limiter cette phase à l’audit Windows, à l’identité d’installation et à la chaîne de compilation, de test et de packaging.
- Préserver la licence MIT et les crédits d’OpenClicky, ainsi que les instructions locales, notamment celles de [macos/OpenClicky](macos/OpenClicky/AGENTS.md).
- Limiter les changements sur Linux, macOS et les composants partagés aux besoins explicites de la tâche.
- Utiliser le runner Windows x64 pour valider le client. Indiquer la plateforme, la commande et le résultat réel de chaque vérification.
- Garder les installateurs dans les artefacts GitHub Actions. La publication, la signature et l’activation du flux de mises à jour demandent une phase de validation distincte.
- Compiler sans service payant ni secret du dépôt amont. Garder les clés API, jetons, réglages personnels et journaux locaux hors du dépôt et des artefacts. Respecter les protections de Windows.

## Conventions de développement

- Garder la logique testable dans `OpenClicky.Core` et les appels Windows, l’interface et le cycle de vie dans `OpenClicky.Windows`. Réutiliser les abstractions de plateforme existantes.
- Suivre les conventions C# de [windows/PORTING.md](windows/PORTING.md). Mettre à jour l’interface sur le dispatcher WPF et conserver les noms de champs JSON compatibles.
- Épingler le SDK .NET et la version du CLI Velopack. Mettre à jour ensemble le CLI Velopack et sa référence NuGet.
- Garder le package `PleyCompanion.Windows`, les données `%LOCALAPPDATA%\PleyCompanion` et le canal `pleycompanion-win-x64` distincts des équivalents OpenClicky.
- Travailler sur une branche dédiée et ouvrir une pull request pour revue. Documenter les commandes et le résultat des tests.
- Suivre [windows/README.md](windows/README.md) pour compiler, exécuter les tests et récupérer l’installateur.
