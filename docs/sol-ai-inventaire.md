# Sol AI — inventaire et premier lot

6 octobre 2026. Ce document distingue les vérifications réalisées des travaux restant à faire.

## Décisions produit

- Le nom est **Sol AI**. Il enrichit les actions de Sol ; ce n’est pas un chatbot.
- Les entrées IA sont masquées lorsque Windows ou le matériel sont incompatibles. Le navigateur et ses fonctions natives conservent leurs exigences actuelles.
- Activation facultative, téléchargement choisi, aucun modèle chargé au démarrage et aucun recours cloud silencieux.
- Pilules, menus, thèmes, couleurs dynamiques et échelles de Sol constituent l’interface commune.

## Points d’intégration vérifiés dans le code

| Fonction | Point existant à réutiliser |
|---|---|
| Commandes des interfaces | `ui/src/bridge.ts`, validation `src/Browser.Core/CommandParser.cs`, exécution `BrowserController` |
| Actions sur une sélection | Événement WebView2 `ContextMenuRequested` et menus Sol dans `BrowserController` |
| Affichage des résultats | `PopupHost`, `Popup.tsx`, styles `popup.css` et publication du thème par `UiBridge` |
| Données par profil | `AppPaths.DataDir`, `JsonFileStore` et `BrowserDb` |
| Réseau | `NetworkInspector`, capture CDP par onglet, panneau existant ; capture console/rejeu/mocks restent à implémenter |
| Session et vues | `PageHost`, gestion des onglets, vue scindée, veille ; préserver ces chemins communs |
| Extraction pour lecture/IA | À créer au lot mode lecture ; aucune extraction d’article robuste commune actuellement vérifiée |

Le projet cible .NET 10 / Windows 10 build 19041, WinUI 3 et WebView2, avec distributions x64, ARM64 et x86. Le parcours Foundry Windows documenté cible Windows 11 24H2 build 26100 ou ultérieur, x64/ARM64. La machine de développement retourne Windows build 26300 et architecture x64 : le critère OS/architecture est satisfait ici, mais cela ne valide ni modèle, ni accélération, ni qualité.

Source : [prérequis et fonctionnement du SDK Foundry Local](https://learn.microsoft.com/en-us/windows/ai/foundry-local/get-started).

## Première mesure, à ne pas confondre avec un benchmark au repos

Une lecture du navigateur de développement déjà ouvert, PID 16696, donne environ **102 MiB de working set pour l’hôte** et **65 MiB de mémoire privée de l’hôte**. Ses huit processus WebView2 descendants représentent environ **398 MiB de working sets additionnés**. Cette somme peut compter des pages mémoire partagées plusieurs fois et dépend des onglets chargés ; elle n’est ni une mémoire physique exclusive ni une mesure normalisée au repos. Les autres arbres WebView2 présents sur la machine ont été exclus.

Il faut encore mesurer, dans un profil propre : démarrage froid, un accueil, une page, plusieurs onglets, veille et fermeture du processus auxiliaire IA. Les latences, tailles des modèles, consommation GPU et qualité français/anglais restent à relever après sélection et téléchargement d’un modèle. Aucun runtime IA ni modèle n’a été installé pour ce premier lot ; **l’essai d’inférence du lot 0 reste à faire avant le lot IA**.

## Notes rapides réalisées

- Accès dans la barre de Sol et raccourci Ctrl+Alt+N, sans modifier Ctrl+Maj+N pour le privé.
- Vue de gestion dans un onglet interne Sol, avec liste chronologique à gauche et document à droite ; modal réservé aux notes rapides. Vue interne isolée du zoom des sites, avec pont limité aux commandes de notes et validation de l’adresse source.
- Création et édition, recherche, suppression confirmée et export texte via le sélecteur Windows.
- Clic droit sur un texte sélectionné : choisir une nouvelle note ou une note existante. Un ajout conserve le texte précédent et ajoute le passage avec sa source.
- Stockage `notes.json` par profil par l’écriture atomique existante ; fichier inclus dans la sauvegarde chiffrée Sol.
- Brouillon conservé dans la fenêtre, y compris lorsqu’on ferme le panneau ; protection avant changement de note. Un brouillon non enregistré n’est pas conservé à la fermeture de Sol.
- Privé : notes uniquement en mémoire, distinctes des notes normales. L’export est une action volontaire qui crée un fichier choisi par l’utilisateur.
- Si le disque ne permet pas d’enregistrer ou si la longueur limite est dépassée, conserver l’état précédent et le brouillon, et afficher l’erreur.

## Vérification

Commandes reproductibles : `node tools/check-navigation.mjs bin/notes-ready/Sol.exe --notes`, puis `node tools/check-navigation.mjs bin/notes-ready/Sol.exe` pour la non-régression du navigateur.

Les essais utilisent un profil jetable et le navigateur natif compilé : saisie dans l’éditeur, menu contextuel de sélection, ajout à une note nouvelle/existante, URL source, brouillon, redémarrage, privé et échelles 100/90/80. Les tests du cœur vérifient persistance, séparation des profils, édition/suppression et refus de contenu ou sources invalides. Le moteur Sol AI reste un travail distinct et non livré.

Résultats : build de l’interface réussi, build natif réussi, 217 tests du cœur réussis, tests natifs des notes réussis (y compris thèmes, vue scindée, zoom indépendant et refus des commandes étrangères depuis la page Notes). Captures dans `bin/navigation-check-1791327593527`. La vérification native générale est conservée dans `bin/navigation-check-1791327690912`.
