<p align="center"><img src="docs/images/logo.png" alt="Sol" width="120"></p>

# Sol

**Sol browser** est un navigateur web léger pour Windows, avec les onglets dans une carte latérale, un bloqueur de publicités intégré et des outils pour les développeurs web. Son interface prend les couleurs de la page affichée.

*[English version](README.md)*

![Sol affichant une page, la carte des onglets à gauche](docs/images/browser.png)

![Sol en thème sombre](docs/images/browser-dark.png)

> Un projet personnel, partagé tel quel. Les versions 0.1 sont des premières versions : tout n'est pas encore poli.

## Ce qu'il fait

**Naviguer au quotidien**
- Les onglets dans une carte latérale qu'on redimensionne ou qu'on masque. Glissez un onglet pour le déplacer, ou sur un groupe pour l'y ajouter ; glissez-le dehors pour l'en retirer.
- Un onglet épinglé reste à sa place quand on le ferme : sa page est déchargée et revient au clic.
- Groupes d'onglets nommés et colorés, vue scindée (deux pages côte à côte), favoris et historique.
- Des raccourcis qui ouvrent leur site sur place.
- La session est rouverte au démarrage.
- Sol s'ouvre en plein écran avec toute son interface ; le bouton vert repasse en fenêtre. Une vidéo en plein écran occupe tout l'écran.
- En français et en anglais, selon la langue de Windows.
- Des profils aux données séparées, et des fenêtres privées effacées à leur fermeture.

**Vos données**
- Configuration à la première ouverture : votre nom, la langue, le thème, la police, le moteur de recherche et ce qu'il faut bloquer.
- Import depuis Chrome, Edge, Brave, Comet, Arc, Vivaldi et Opera : favoris, historique et mots de passe enregistrés. Les mots de passe qu'un navigateur garde pour lui seul s'importent depuis le fichier CSV qu'il exporte.
- Sauvegarde : réglages, onglets, favoris, historique et mots de passe dans un seul fichier, chiffré par une clé que vous choisissez. Sol ne garde pas cette clé.

**Apparence**
- Thème clair, sombre, celui de Windows ou celui de la page.
- La couleur de l'interface suit la page, y compris la vidéo en cours ou la partie de la page à l'écran. Chaque zone peut être désactivée.
- Polices de l'interface : système, Inter ou SF Pro.

**Moins de distractions**
- Publicités et traqueurs bloqués par [uBlock Origin Lite](https://github.com/uBlockOrigin/uBOL-home), intégré et tenu à jour.
- Lecteur flottant : quittez un onglet où une vidéo joue, elle reste à l'écran avec les commandes de Sol.
- Les sites demandent une fois l'accès au micro, à la caméra ou à la position ; la réponse est retenue par site et peut être oubliée.

**Économe en mémoire**
- Les onglets inactifs sont mis en veille après 5 minutes et déchargés après 30. Les onglets rouverts ne se chargent que lorsqu'on les affiche.
- Un seul processus de rendu par site.

**Pour les développeurs**
- Simulateur d'appareils avec leur vraie forme, rotation, et plusieurs écrans côte à côte.
- Inspecteur réseau : requêtes, filtre API, erreurs, en-têtes et corps des réponses.
- `:3000` ouvre `http://localhost:3000`, et un serveur pas encore démarré est retenté tout seul.
- Captures d'une zone, de la partie visible ou de la page entière, en PNG ou en PDF.

![La même page sur un téléphone, une tablette et un écran d'ordinateur, côte à côte](docs/images/responsive.png)

## Télécharger

Prenez la dernière version sur la page [Releases](https://gitlab.com/stevencrdkoulo/sol-browser/-/releases).

| Votre Windows | Installateur | Archive portable |
|---|---|---|
| 64 bits Intel ou AMD (la plupart des PC) | `Sol-<version>-win-x64-Setup.exe` | `Sol-<version>-win-x64.zip` |
| ARM (PC Snapdragon) | `Sol-<version>-win-arm64-Setup.exe` | `Sol-<version>-win-arm64.zip` |
| 32 bits | `Sol-<version>-win-x86-Setup.exe` | `Sol-<version>-win-x86.zip` |

**L'installateur** installe Sol pour votre compte, sans droits d'administrateur, et crée les raccourcis. Un Sol installé :
- se met à jour tout seul : une nouvelle version se télécharge en arrière-plan, puis un encart en bas à droite propose de redémarrer ;
- est reconnu par Windows comme navigateur, dans « Ouvrir avec » pour les pages web et les PDF, et dans *Paramètres → Applications → Applications par défaut*.

**L'archive** ne demande aucune installation : décompressez-la où vous voulez et lancez `Sol.exe`. Elle ne se met pas à jour toute seule.

Configuration requise : Windows 10 version 1809 ou plus récent, et le **WebView2 Runtime** de Microsoft Edge, livré avec Windows 11 et la plupart des Windows 10 ([à télécharger](https://developer.microsoft.com/microsoft-edge/webview2/) s'il manque).

### « Windows a protégé votre ordinateur »

Les fichiers ne sont **pas signés** : un certificat de signature coûte de l'argent que ce projet n'a pas. Windows SmartScreen avertit au premier lancement ; choisissez *Informations complémentaires* → *Exécuter quand même*.

Vous pouvez vérifier un téléchargement avec l'empreinte publiée dans chaque version (`SHA256SUMS.txt`) :

```powershell
Get-FileHash .\Sol-0.1.4-win-x64-Setup.exe -Algorithm SHA256
```

## Vie privée

- Aucun compte ni télémétrie envoyée à l’auteur de Sol. Sol AI, facultatif, utilise le moteur local Microsoft avec sa télémétrie facultative désactivée ; catalogue/téléchargements contactent Microsoft et le moteur peut recueillir des diagnostics essentiels. L’inférence du modèle reste locale.
- Favoris, historique, mots de passe et réglages restent sur votre ordinateur. Une sauvegarde n'en sort que sous la forme du fichier chiffré que vous exportez.
- Les connexions que Sol établit de lui-même :
  - les suggestions de recherche, envoyées à votre moteur pendant la saisie dans la barre d'adresse (désactivable) ;
  - la recherche d'une nouvelle version de Sol sur GitLab au démarrage (copies installées seulement) ;
  - la recherche hebdomadaire d'une nouvelle version d'uBlock Origin Lite sur GitHub ;
  - les icônes des sites de vos raccourcis.
- Les pages web n'ont aucun canal vers le code du navigateur.

## Raccourcis clavier

| | |
|---|---|
| `Ctrl+T` / `Ctrl+W` | Nouvel onglet / fermer l'onglet |
| `Ctrl+Maj+T` | Rouvrir le dernier onglet fermé |
| `Ctrl+Maj+N` | Nouvelle fenêtre privée |
| `Ctrl+L` | Barre d'adresse |
| `Ctrl+Tab` / `Ctrl+Maj+Tab` | Onglet suivant / précédent |
| `Alt+←` / `Alt+→` | Page précédente / suivante |
| `Ctrl +` / `Ctrl -` / `Ctrl 0` | Zoom avant / arrière / réinitialiser |
| `F5` | Recharger |

## Compiler depuis les sources

Il faut Windows, le [SDK .NET 10](https://dotnet.microsoft.com/download) et [Node.js](https://nodejs.org) (LTS en cours).

```powershell
cd ui
npm ci
npm run build          # l'interface (React), copiée à côté de l'exécutable par la compilation
cd ..
pwsh tools/get-ublock.ps1                      # récupère uBlock Origin Lite (facultatif : sans lui, rien n'est bloqué)
dotnet test tests/Browser.Core.Tests
dotnet run --project src/Browser.App
```

Fichiers publiables pour les trois architectures : `pwsh tools/publish.ps1 -Arch x64,arm64,x86`. Les installateurs sont construits aussi quand l'outil Velopack est présent (`dotnet tool install -g vpk`).

`tools/env.ps1` est la configuration propre à l'auteur (tout sur le disque `D:`) ; vous n'en avez pas besoin.

## Comment c'est fait

- **Moteur** : Microsoft Edge WebView2 (Chromium), une vue par onglet.
- **Hôte** : C# / .NET 10 avec WinUI 3 (`src/Browser.App`). Il détient tout l'état.
- **Logique** : du C# simple et testé, sans dépendance à l'interface (`src/Browser.Core`, tests dans `tests/`).
- **Interface** : React et TypeScript (`ui/`), chargée depuis des fichiers locaux sous une politique de sécurité stricte. Elle envoie des commandes validées à l'hôte et reçoit l'état en retour.
- **Installateur et mises à jour** : [Velopack](https://velopack.io), qui lit les versions publiées dans ce dépôt.

## Publier une version (mainteneurs)

1. Régler `<Version>` dans `src/Browser.App/Browser.App.csproj`.
2. Poser l'étiquette et la pousser : `git tag v0.1.4 && git push origin v0.1.4`.
3. La tâche `release` du pipeline construit les installateurs et les archives des trois architectures, publie leurs empreintes et crée la version sur GitLab. Les copies installées la récupèrent de là.

## Politique de signature du code

Les versions ne sont pas encore signées. Une demande à la [SignPath Foundation](https://signpath.org), qui signe gratuitement les projets open source, est prévue ; en attendant, utilisez les empreintes décrites plus haut.

- Auteur des modifications, relecteur et approbateur : [Steven KOULO](https://gitlab.com/stevencrdkoulo).
- Chaque fichier publié est construit par le [pipeline de publication](.gitlab-ci.yml) à partir d'un commit étiqueté de ce dépôt ; rien n'est construit ni déposé à la main.
- Vie privée : Sol n'envoie aucune information à son auteur. Voir [Vie privée](#vie-privée) pour les seules connexions qu'il établit de lui-même.

## Licence

[MIT](LICENSE). Les logiciels tiers livrés avec le navigateur, uBlock Origin Lite (GPLv3) en particulier, sont listés dans [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
