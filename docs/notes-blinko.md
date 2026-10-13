# Notes Sol : adaptation locale de Blinko

Référence étudiée : [blinkospace/blinko](https://github.com/blinkospace/blinko), commit `b2586d03fddb796252f050a96922879bf4a41fad`, le 13 octobre 2026.
Lecture du schéma Prisma, des cartes, menus, éditeur, tags, historiques, statistiques, réglages et jobs d’import/export.

Le choix demandé est une intégration locale, sans IA. Le stockage existant de Sol est conservé et étendu ; les notes déjà présentes restent lisibles. Pas de connexion obligatoire ni de serveur Blinko.
L’éditeur Vditor 3.11.2, également utilisé par Blinko, est embarqué avec ses ressources et notices de licence MIT. Les fonctions Sol sont implémentées dans les couches existantes React/WinUI/Browser.Core.

## Inventaire fonctionnel

| Fonction de notes étudiée | Adaptation dans Sol |
| --- | --- |
| Capture rapide | Modal existant conservé ; ajout d’un passage sélectionné à une note existante ou nouvelle conservé. |
| Idées, notes longues, tâches | Trois types, conversion individuelle et multiple, état terminé pour une tâche, échéance facultative. |
| Édition riche | Vditor : texte riche, rendu instantané, mode Markdown, listes, tableaux, liens, code, annuler/rétablir, plan et compteur. |
| Lecture Markdown | GFM, listes cochables, tableaux, code coloré, mathématiques, notes de bas de page ; HTML nettoyé. |
| Enregistrement | Automatique dans la vue complète ; bouton manuel dans le modal rapide ; brouillon conservé en cas d’échec d’écriture. |
| Affichage | Cartes, liste, panneau de détail et mode concentration ; retour à la collection sur fenêtre étroite. |
| Recherche | Texte, expressions entre guillemets, `#tag`, `type:task`, `has:file`, `is:pinned`, `is:done`, `before:` et `after:`. Les dates de recherche concernent la modification. |
| Tri | Récentes, anciennes, titre, ordre manuel par déplacement des cartes. |
| Épinglage | Épingler/détacher ; priorité dans les tris automatiques. |
| Tags | Tags saisis ou extraits du texte ; hiérarchie `projet/sous-projet`, filtrage, renommage et suppression avec sous-tags ; icône, repli et ordre personnalisables. |
| Déplacement de tags | Déposer un tag sur un autre pour en faire un sous-tag ; déplacement cyclique refusé. |
| Références | Liens `[[id]]` ou `[[titre]]`, sélecteur de note et liens entrants. IDs réattribués et références réécrites à l’import Sol. |
| Images et documents | Vue Fichiers, pièces jointes, collage et dépôt de fichiers, aperçu agrandi des images et enregistrement via le sélecteur Windows. |
| Audio | Enregistrement vocal sur autorisation explicite du microphone, annulation, lecture et export du fichier ; aucune transcription IA. |
| Archives | Archivage/désarchivage manuel, individuel ou multiple. |
| Archivage programmé | Réglage désactivé initialement ; anciennes idées non épinglées archivées pendant que Sol fonctionne, sans affecter la note ouverte. |
| Corbeille | Suppression récupérable, restauration et suppression définitive confirmée. Les pièces jointes partagées par d’autres notes restent conservées. |
| Actions multiples | Sélection, sélection de la vue, épinglage, archivage, conversion, corbeille, restauration et suppression définitive. |
| Versions | Les 50 derniers contenus antérieurs sont conservés, consultables et restaurables. La restauration conserve aussi la version remplacée. |
| Annotations | Commentaires locaux datés, ajout et suppression. Les fils sociaux et mentions ne sont pas simulés. |
| Dates | Consultation et modification des dates de création et modification. |
| Redécouverte | Vue des notes à revoir, tirage aléatoire, marquer comme revue / à revoir. |
| Statistiques | Comptages, tâches terminées, activité des 12 dernières semaines et distribution des tags. |
| Export | Markdown, JSON, CSV et ZIP Sol avec fichiers ; note individuelle, totalité ou vue filtrée. Le CSV neutralise les formules sans perdre le texte au réimport. |
| Import | Sélection multiple de Markdown/TXT, JSON Sol/Blinko, CSV, ZIP Sol, ZIP Markdown avec fichiers relatifs, ZIP d’export JSON/CSV Blinko. |
| Import Memos | Base SQLite en lecture seule : textes, dates, tags, épinglage, archives, commentaires et références. Les fichiers externes absents de la base ne sont pas inventés. |
| Sauvegarde du profil | Notes et fichier des pièces jointes inclus dans la sauvegarde chiffrée Sol. |
| Privé | Données conservées en mémoire dans la fenêtre privée ; pas d’écriture des notes ou fichiers de cette fenêtre dans le profil normal. |

## Fonctions volontairement hors de cette intégration

Choix explicite de l’utilisateur : les fonctions d’IA sont exclues et Sol AI reste masqué/désactivé.
Cela couvre rédaction IA, tags IA, recherche vectorielle/RAG, suggestions sémantiques, OCR et transcription, agents, modèles et tâches IA.

Le choix « entièrement locale » exclut les fonctions nécessitant un service : comptes et SSO Blinko, synchronisation multi-appareils, liens publics/protégés/expirants, abonnements sociaux, mentions et notifications serveur, API/tokens/webhooks, stockage S3 et connecteurs externes. L’export local remplace la diffusion d’un lien public.
Les backups complets PostgreSQL `.bko` restent un format de serveur ; utiliser les exports de notes Markdown/JSON/CSV. Les plugins Blinko, bots, lecteur musical indépendant et administration de son serveur ne sont pas des fonctionnalités de la vue Notes Sol.

## Contraintes et intégrité

- Une note : 50 000 caractères, 40 tags, 30 fichiers ; 50 versions, 500 annotations. Ces limites évitent de charger un profil démesuré dans le canal WebView2.
- Pièces jointes : 20 Mo par fichier, 100 Mo par profil ; import : 150 Mo et 1 000 notes par opération.
- Les imports échouent en entier si un document est invalide ; les notes existantes ne sont pas remplacées. Les fichiers ajoutés par un import raté sont nettoyés.
- Les ZIP sont lus en mémoire, avec tailles vérifiées ; aucun chemin contenu dans l’archive n’est extrait sur le disque.
- Les textes Markdown ne peuvent pas exécuter des scripts dans l’aperçu ou l’éditeur riche. Les actions natives restent réservées à l’origine interne Notes.
- Les réglages visuels des tags et de la vue sont liés au profil WebView2 ; l’export de notes porte sur les contenus, pas sur ces préférences d’affichage.
- L’archivage automatique fonctionne quand Sol est ouvert. Il n’installe pas de service Windows en arrière-plan.

## Vérification

- `dotnet test tests/Browser.Core.Tests/Browser.Core.Tests.csproj -c Release` : migration, persistance, versions, actions atomiques, liens d’import, ZIP, Memos, fichiers, échecs disque et archivage.
- `node tools/check-notes-model.mjs` : recherche, filtres, mapping des listes cochables et métadonnées modifiées.
- `node tools/check-notes.mjs <Sol.exe>` : profil jetable dans le vrai Sol, sauvegarde et frappe rapide, tags, références, cases cochables, versions, images, audio synthétique, actions, saisie dans l’éditeur riche hors ligne, HTML non exécutable, petite fenêtre et redémarrage.
- Compilation TypeScript/Vite et WinUI. Régressions de la sidebar et de la navigation vérifiées séparément.
- Le microphone physique et les dialogues de sélection de fichiers demandent encore une validation manuelle. Les limites d’import sont testées dans le noyau ; aucune base ou note personnelle n’est utilisée pour les tests.
