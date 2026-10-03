# Résultats des prototypes

Date : 2026-10-03. Machine de développement, fenêtre de 1025 × 531 px, Windows App SDK 1.x,
.NET SDK 10.0.401. Code : `prototypes/Proto` (jetable).

## Prototype 1 — menus par-dessus la page

- **Compilation WinUI 3 par `dotnet build` sans Visual Studio : OUI.** 0 erreur, 0 avertissement.
- **1. Le menu s'affiche par-dessus la page : OUI.** La vue d'interface, agrandie à toute la
  fenêtre, se dessine bien au-dessus de la vue de page.
- **1 bis. La page reste visible autour du menu : NON.** La zone censée être transparente est
  remplie par la couleur de fond du thème (gris foncé 32,32,32). La page disparaît entièrement
  tant que le menu est ouvert.
- **2 et 3. Clics réels (fermer le menu en cliquant la page, clics sur la page menu fermé) :
  NON VÉRIFIÉS.** Les captures ont été faites en pilotant les vues par le port de débogage, ce
  qui ne passe pas par la souris. À vérifier à la main.
- **4. Flash à l'ouverture : non observé**, mais sans objet tant que le point 1 bis échoue.

Tentatives pour obtenir la transparence, toutes sans effet :
1. `DefaultBackgroundColor = Colors.Transparent` avant l'initialisation.
2. La même affectation après `EnsureCoreWebView2Async`.
3. Variable d'environnement `WEBVIEW2_DEFAULT_BACKGROUND_COLOR=00FFFFFF`.
4. Surcharge de `ApplicationPageBackgroundThemeBrush` en transparent dans `App.xaml`.

Vérification croisée : avec un fond de fenêtre rouge et la vue de page masquée, la zone reste
gris foncé. La vue d'interface n'est donc pas transparente du tout ; ce n'est pas un problème
d'empilement des deux vues.

C'est un défaut connu et toujours ouvert de WinUI 3 :
https://github.com/microsoft/microsoft-ui-xaml/issues/6527

**Décision : à prendre avec l'utilisateur.** La superposition transparente en WinUI 3 est exclue.

## Prototype 2 — flou progressif injecté

Défilement automatique de 5 s, temps entre trames mesuré dans la page (16,7 ms = 60 images/s).

| Site | Sans flou | 4 couches | 1 couche |
|---|---|---|---|
| Wikipédia (article « France », 71 800 px de haut) | p95 16,9 ms, 5 lentes / 285 | p95 16,9 ms, 2 lentes / 271 | p95 16,9 ms, 3 lentes / 293 |
| GitHub (WebKit/WebKit) | p95 17,0 ms, 3 lentes / 284 | p95 17,0 ms, 0 lente / 302 | p95 16,9 ms, 0 lente / 302 |
| YouTube (accueil) | mesure invalide | mesure invalide | mesure invalide |

- **Coût : aucun écart mesurable** entre sans flou, 4 couches et 1 couche sur Wikipédia et GitHub.
- **YouTube : mesure invalide.** La page d'accueil hors connexion ne défile pas (404 px de haut),
  et la mesure « sans flou » a été prise pendant le chargement. Aucune conclusion à en tirer.
- **Injection : fonctionne sur les trois sites**, y compris YouTube qui impose Trusted Types.
- **Branchement hôte : vérifié.** Le bouton de l'interface active le flou via
  `AddScriptToExecuteOnDocumentCreatedAsync`, la page rechargée porte bien les bandes.
- **Rendu : le flou du haut recouvre l'en-tête fixe des sites.** Sur YouTube, le logo et la barre
  de recherche deviennent flous et peu lisibles.

Limites de la mesure :
- Fenêtre petite. Le coût d'un flou croît avec la surface ; en plein écran il sera plus élevé.
- Le temps entre trames reflète surtout le fil principal de la page. Un coût côté carte graphique
  qui ne ferait pas chuter la cadence n'apparaît pas ici (consommation, chauffe).
- Clics traversant les bandes : non vérifiés à la souris.

**Décision proposée : garder les 4 couches**, réglage désactivable comme prévu. La question de
l'en-tête fixe des sites est à trancher avec l'utilisateur.
