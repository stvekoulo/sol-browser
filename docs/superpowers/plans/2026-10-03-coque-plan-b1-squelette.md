# Coque du navigateur — Plan B1 : squelette navigable

**Goal:** Une vraie fenêtre de navigateur, minimale mais utilisable : barre de titre maison aux trois boutons ronds, onglets, barre d'adresse, navigation, vue scindée.

**Architecture:** Hôte WinUI 3 (`src/Browser.App`) qui empile une vue d'interface (React, fichiers locaux) et une WebView2 par onglet. L'hôte branche `Browser.Core` sur les vues et pousse l'état complet à l'interface à chaque changement.

**Tech Stack:** .NET 10, WinUI 3 non empaqueté, WebView2, React + Vite + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-03-coque-navigateur-design.md`
**Résultats des prototypes:** `docs/superpowers/specs/2026-10-03-prototypes-resultats.md`

Exécuté en ligne par l'auteur du plan : le code n'est pas recopié ici à l'avance, plusieurs API
WinUI étant à confirmer à la compilation. Chaque tâche se termine par une vérification observable.

## Global Constraints

- Rien sur C: ; charger `tools/env.ps1` avant toute commande `dotnet` ou `npm`.
- La vue d'interface ne charge que `https://ui.navigateur/` (dossier local) ; toute autre navigation est annulée.
- Les vues de pages n'ont aucun canal vers l'hôte : pas de `WebMessageReceived`, pas d'objet hôte.
- Chaque message de l'interface passe par `CommandParser.Parse` ; `null` est ignoré.
- Pas de superposition transparente de vues web (exclue par le prototype 1).
- Aucune ressource sous licence Apple.

## Décisions issues des prototypes

- Hôte : WinUI 3.
- Menus et suggestions (Plan B2) : petite vue web opaque à la taille du menu, posée sur la page.
- Flou : bandes de 44 px, couches de 1, 2, 3 et 5 px, haut et bas identiques (Plan B2).

## Structure des fichiers

```
src/Browser.App/
  Browser.App.csproj        WinUI 3 non empaqueté, référence Browser.Core
  App.xaml(.cs)
  MainWindow.xaml(.cs)      fenêtre sans barre de titre système ; grille : interface en haut, pages dessous
  UiBridge.cs               vue d'interface : verrou fichiers locaux, réception des commandes, envoi de l'état
  PageHost.cs               une WebView2 par onglet, événements de navigation, disposition seule/scindée
  BrowserController.cs      exécute les commandes sur Browser.Core et PageHost, construit l'état
ui/                         React + Vite + TypeScript
  src/bridge.ts             send(command), onState(callback)
  src/App.tsx               barre de titre, onglets, barre d'adresse
  src/theme.css             toutes les variables de design
  dist/                     sortie de build, copiée dans la sortie de l'hôte
```

## Format de l'état (hôte → interface)

```json
{ "tabs": [{ "id": "t1", "url": "…", "title": "…", "loading": false,
             "canGoBack": false, "canGoForward": false }],
  "activeId": "t1",
  "split": { "secondaryId": null, "ratio": 0.5 },
  "maximized": false }
```

## Tâches

### Task 1: Hôte, fenêtre et vue d'interface verrouillée
Fenêtre sans barre de titre système, redimensionnable. Vue d'interface de hauteur fixe en haut,
chargée depuis `https://ui.navigateur/index.html`. `UiBridge` annule toute navigation hors de cet
hôte virtuel, parse les messages et les passe au contrôleur.
**Vérification :** la fenêtre s'ouvre, l'interface s'affiche ; un `location.href = 'https://example.com'`
exécuté dans la vue d'interface est annulé ; un message invalide est ignoré sans erreur.

### Task 2: Pages, onglets et navigation
`PageHost` crée une WebView2 par onglet, n'affiche que l'onglet actif (et le secondaire en vue
scindée). Événements titre, URL, chargement, historique → `TabManager.Update` → état poussé.
`window.open` et `target=_blank` ouvrent un nouvel onglet. Commandes `tab.*`, `nav.*`, `split.*`.
**Vérification :** par le port de débogage, envoyer `tab.open`, `nav.go`, `tab.close`,
`split.open` ; l'état reçu et la capture d'écran correspondent.

### Task 3: Interface React
Vite + React + TypeScript dans `ui/`. Barre de titre avec trois boutons ronds (fermer, réduire,
agrandir) et zone de déplacement de la fenêtre ; barre d'onglets ; barre d'adresse avec précédent,
suivant, recharger. Tout le style passe par les variables de `theme.css`.
**Vérification :** capture d'écran ; ouvrir, fermer et changer d'onglet, naviguer vers une URL et
vers une recherche, par l'interface.

### Task 4: Fenêtre : déplacement, boutons, vérification à la souris
Déplacement par la barre de titre, double-clic pour agrandir, boutons ronds fonctionnels.
**Vérification :** par l'utilisateur, à la souris — c'est le seul moyen de valider les clics réels.

## Plan B2 (ensuite)

Menus et suggestions en vue dédiée, menu contextuel, historique, favoris, téléchargements,
raccourcis clavier, restauration de session, pages d'erreur, reprise après plantage, flou injecté.
