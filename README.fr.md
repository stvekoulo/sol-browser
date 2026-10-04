<p align="center"><img src="docs/images/logo.png" alt="Sol" width="120"></p>

# Sol

**Sol browser** — un navigateur web léger pour Windows, pensé pour les développeurs, avec une interface calme qui prend les couleurs de la page visitée.

*[English version](README.md)*

![Le navigateur affichant une page, la carte des onglets à gauche](docs/images/browser.png)

> Un projet personnel, partagé tel quel. La version 0.1.0 est une première version.

## Ce qu'il fait

**Naviguer au quotidien**
- Les onglets dans une carte latérale qu'on redimensionne ou qu'on range : onglets épinglés, groupes nommés et colorés, déplacement à la souris, session rouverte au démarrage.
- Des raccourcis qui ouvrent leur site sur place, la vue scindée (deux pages côte à côte), les favoris et l'historique.
- Une interface qui suit la page : son fond et sa couleur teintent le navigateur, en douceur. Thème clair, sombre, celui de Windows ou celui de la page.
- En français et en anglais, selon la langue de Windows.
- Des profils aux données séparées, et des fenêtres privées effacées à leur fermeture.
- Import des favoris, de l'historique et des mots de passe d'autres navigateurs, et une sauvegarde chiffrée que vous exportez et restaurez avec votre propre clé.

**Tranquille par défaut**
- Publicités et traqueurs bloqués d'origine par [uBlock Origin Lite](https://github.com/uBlockOrigin/uBOL-home), intégré et tenu à jour.
- Un lecteur flottant : quittez un onglet où une vidéo joue, elle reste à l'écran sous les commandes du navigateur.
- Les accès demandés par les sites (micro, caméra, position…) sont demandés une fois, retenus par site, et faciles à oublier.

**Léger**
- Les onglets inactifs sont mis en veille après 5 minutes et déchargés après 30 ; les onglets rouverts ne se chargent que lorsqu'on les affiche.
- Un seul processus de rendu par site.

**Pour les développeurs**
- Simulateur d'appareils avec leur vraie forme, rotation, et comparaison de plusieurs écrans côte à côte.
- Inspecteur réseau : requêtes, filtre API, erreurs, en-têtes et corps des réponses.
- Confort localhost : `:3000` ouvre `http://localhost:3000`, et un serveur pas encore démarré est retenté tout seul.
- Captures d'une zone, de la partie visible ou de la page entière, en PNG ou en PDF.

![La même page sur un téléphone, une tablette et un écran d'ordinateur, côte à côte](docs/images/responsive.png)

## Télécharger

Prenez la dernière archive sur la page [Releases](https://gitlab.com/stevencrdkoulo/sol-browser/-/releases) :

| Votre Windows | Fichier |
|---|---|
| 64 bits Intel ou AMD (la plupart des PC) | `Sol-<version>-win-x64.zip` |
| ARM (PC Snapdragon, Surface Pro X…) | `Sol-<version>-win-arm64.zip` |
| 32 bits | `Sol-<version>-win-x86.zip` |

Chaque architecture a aussi son installateur, `Sol-<version>-win-<arch>-Setup.exe` : il installe Sol pour votre
compte (sans droits d'administrateur), crée les raccourcis et tient Sol à jour depuis cette page. Sinon, décompressez
l'archive où vous voulez et lancez `Sol.exe`. Dans les deux cas, il n'y a rien d'autre à
installer : seul le **WebView2 Runtime** de Microsoft Edge est requis ; il est livré avec Windows 11 et la plupart des
Windows 10 ([à télécharger](https://developer.microsoft.com/microsoft-edge/webview2/) s'il manque). Windows 10 version 1809 ou plus récent.

### « Windows a protégé votre ordinateur »

Les archives ne sont **pas signées** : un certificat de signature coûte de l'argent que ce projet n'a pas. Windows
SmartScreen vous avertira donc au premier lancement ; choisissez *Informations complémentaires* → *Exécuter quand même*.

Vous n'avez pas à nous croire sur parole. Chaque version se vérifie avec son empreinte publiée :

```powershell
# L'empreinte est celle publiée avec la version (SHA256SUMS.txt)
Get-FileHash .\Sol-0.1.0-win-x64.zip -Algorithm SHA256
```

## Vie privée

- Aucune télémétrie, aucun compte.
- Ce qui sort de votre ordinateur à l'initiative du navigateur : les suggestions de recherche envoyées à votre moteur
  pendant la saisie dans la barre d'adresse (désactivable), une vérification hebdomadaire d'une nouvelle version
  d'uBlock Origin Lite sur GitHub, une vérification d'une nouvelle version de Sol sur GitLab au démarrage (copies
  installées seulement), et les icônes des sites de vos raccourcis.
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
| `Ctrl +` / `Ctrl -` / `Ctrl 0` | Zoom |

## Compiler depuis les sources

Il faut Windows, le [SDK .NET 10](https://dotnet.microsoft.com/download) et [Node.js](https://nodejs.org).

```powershell
cd ui
npm ci
npm run build          # l'interface (React), copiée à côté de l'exécutable par la compilation
cd ..
pwsh tools/get-ublock.ps1                      # récupère uBlock Origin Lite (facultatif : sans lui, rien n'est bloqué)
dotnet test tests/Browser.Core.Tests
dotnet run --project src/Browser.App
```

Archives publiables pour les trois architectures : `pwsh tools/publish.ps1 -Arch x64,arm64,x86`.

`tools/env.ps1` est la configuration propre à l'auteur (tout sur le disque `D:`) ; vous n'en avez pas besoin.

## Comment c'est fait

- **Moteur** : Microsoft Edge WebView2 (Chromium), une vue par onglet.
- **Hôte** : C# / .NET 10 avec WinUI 3 (`src/Browser.App`). Il détient tout l'état.
- **Logique** : du C# simple et testé, sans dépendance à l'interface (`src/Browser.Core`, tests dans `tests/`).
- **Interface** : React et TypeScript (`ui/`), chargée depuis des fichiers locaux sous une politique de sécurité stricte.
  Elle envoie des commandes validées à l'hôte et reçoit l'état en retour.

## Publier une version (mainteneurs)

1. Régler `<Version>` dans `src/Browser.App/Browser.App.csproj`.
2. Poser l'étiquette et la pousser : `git tag v0.1.0 && git push origin v0.1.0`.
3. La tâche `release` du pipeline construit les installateurs et les archives des trois architectures, publie leurs empreintes et crée la version sur GitLab.

## Politique de signature du code

Les versions ne sont pas encore signées. Une demande à la [SignPath Foundation](https://signpath.org), qui signe
gratuitement les projets open source, est prévue ; en attendant, utilisez les empreintes décrites plus haut.

- Auteur des modifications, relecteur et approbateur : [Steven KOULO](https://gitlab.com/stevencrdkoulo).
- Chaque fichier publié est construit par le [pipeline de publication](.gitlab-ci.yml) à partir d'un commit étiqueté de ce dépôt ; rien n'est construit ni déposé à la main.
- Vie privée : Sol n'envoie aucune information à son auteur. Voir [Vie privée](#vie-privée) pour les seules connexions qu'il établit de lui-même.

## Licence

[MIT](LICENSE). Les logiciels tiers livrés avec le navigateur, uBlock Origin Lite (GPLv3) en particulier, sont listés
dans [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
