# Sol — optimisations et vérifications du 18 octobre 2026

Périmètre : optimisations locales, après le commit publié `6f909dc` / v1.0.11. La release publiée reste inchangée.

## Modifications

- Ne plus envoyer le même thème chaque seconde aux vues React, ni recréer le fond natif lorsque sa couleur est identique. Conserver les mises à jour explicites et les changements réels de page.
- Annuler les lectures de thème différées devenues obsolètes et éviter de sonder l’onglet actif quand seul un autre onglet change.
- Charger la vue de capture à sa première utilisation ; attendre son écouteur avant d’envoyer la première commande et rejouer le thème courant, y compris en privé.
- Réduire immédiatement la cible mémoire des pages masquées. Demander ensuite une suspension après 30 secondes jusqu’à 4 Go de RAM, 1 minute jusqu’à 8 Go, 5 minutes au-delà. Si le moteur refuse, conserver le mode mémoire réduite sans répéter cette demande à chaque passage.
- Décharger les pages considérées sûres après 10, 15 ou 30 minutes respectivement. Protéger les champs modifiés, cases cochées, sélections, éditeurs, cadres, stockage de session et médias ; préserver les exclusions des téléchargements, appareils, vues scindées et onglets privés.
- Un document protégé ne subit plus une vérification complète de déchargement à chaque passage. Le retour sur l’onglet ou la désactivation du réglage restaure son fonctionnement normal.

Les modes sont utilisés par étapes, avec retour au niveau normal avant une nouvelle demande de suspension. Le moteur reste libre de la refuser. [Mémoire WebView2](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.memoryusagetargetlevel), [suspension WebView2](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.trysuspendasync).

## Mesures

Machine Windows x64 avec environ 8 Go de RAM. Profils neufs et isolés, page HTTP locale, 12 onglets restaurés dont un chargé. Mesure après stabilisation, sur environ 12 secondes, avec les paramètres ordinaires de ralentissement des pages en arrière-plan. Somme des processus appartenant à cette instance Sol.

| Observation | Avant | Après |
| --- | --- | --- |
| Messages de thème identiques au repos | 13 | 0 |
| Vue `#capture` chargée avant usage | Oui | Non |
| Mémoire privée des processus | 286–287 Mio | 270–281 Mio |

Le CPU et le temps de démarrage varient avec la charge des autres applications. Aucun gain CPU ou démarrage chiffré fiable n’est établi. Ces mesures sur une page locale ne prédisent pas la consommation de sites réels, de vidéos ou de nombreuses extensions.

Références conservées sous `bin/` : baseline `sidebar-check-1792312150823` et `sidebar-check-1792312193488` ; mesures après modification `sidebar-check-1792312818856`, `sidebar-check-1792313853349` et `sidebar-check-1792314519934`.

## Vérifications

- 328 tests du cœur réussis, dont les seuils de RAM et le comportement de repli quand la quantité de mémoire est inconnue.
- Build React et build natif Release réussis. Le dernier compte rendu natif est `bin/performance-final-build.log` ; quatre avertissements de nullabilité déjà présents.
- `check-memory-saver.mjs` : déchargement/rechargement, conservation des six types de documents protégés, absence de répétition des refus et désactivation du réglage. Dernière preuve : `bin/sidebar-check-1792314477572/memory-saver.json`.
- `check-performance.mjs` : thème stable, changement réel de couleur, capture initialisée en privé, retour au thème normal et sélection d’onglets avec le CPU de l’interface ralenti quatre fois. Dernière preuve : `bin/sidebar-check-1792314519934/performance.json`.
- `check-capture.mjs` : captures zone/page/fenêtre, PNG/PDF, aperçu, annulation, conservation du défilement et du formulaire, zoom, appareil tactile et fenêtre étroite. Preuves : `bin/sidebar-check-1792313797610`.
- Les profils, caches, cookies et stockages créés par ces tests sont supprimés après fermeture ; les captures, journaux synthétiques et rapports restent disponibles.

Le contrôle de capture a eu un premier délai d’attente CDP au démarrage, puis une exécution complète réussie. Les essais initiaux ont également montré des refus natifs de suspension ; le mode mémoire réduite est donc un chemin explicitement testé. Ne pas présenter une suspension systématique comme garantie.

Non validés physiquement : PC de 4 Go, ARM64 et x86. Les nouveaux fichiers d’intégration ChatGPT et composants d’agent apparus parallèlement ne sont pas couverts par ces résultats.
