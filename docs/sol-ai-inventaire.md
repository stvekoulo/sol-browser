# Sol AI — inventaire et premier lot

6 octobre 2026. Ce document distingue les vérifications réalisées des travaux restant à faire.

## Décisions produit

- Le nom est **Sol AI**. Il enrichit les actions de Sol ; ce n’est pas un chatbot.
- Les entrées IA sont masquées lorsque Windows ou le matériel sont incompatibles. Le navigateur et ses fonctions natives conservent leurs exigences actuelles.
- Activation facultative et téléchargement choisi ; préparation au démarrage uniquement si Sol AI est activé et installé. Aucun recours cloud silencieux.
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

## Lot 8 — essais initiaux du module local, 7 octobre 2026

Implémenté dans `src/Sol.AI`, hôte isolé dans `SolAiService`, réglages dans **Paramètres → Sol**. Désactivé au départ ; aucun modèle téléchargé sans acceptation explicite de sa licence. Pas de serveur HTTP permanent, ni de conversation. Les générations réutilisent le modèle chargé pendant la durée choisie ; les autres opérations restent ponctuelles. Annulation et arrêt surveillés côté navigateur ; arrêt du module lorsque le navigateur ferme. Un verrou de fichier protège les mutations du cache partagé entre fenêtres/profils.

Compatibilité exposée : Windows 11 24H2 ou ultérieur, x64 avec AVX2 ou ARM64, RAM et disque suffisants. Le navigateur de base conserve ses exigences. Publication du module x64 et ARM64 vérifiée ; exécution ARM64 non testée sur ce poste. Module absent sur x86. Pas de téléchargement de composants GPU/NPU : parcours CPU uniquement.

La gamme ci-dessous est historique : elle a été abandonnée au lot 9 au profit d’un seul Sol AI.

| Nom Sol | Alias interne du catalogue | Taille CPU annoncée | Budget RAM totale / disponible | Processeurs logiques |
| --- | --- | --- | --- | --- |
| Sol AI Mini | qwen3-0.6b | 593 Mo | 4 Go / 1,5 Go | 2 |
| Sol AI Medium | qwen3.5-2b-text | 1 464 Mo | 8 Go / 3,5 Go | 4 |
| Sol AI High | qwen3-4b | 2 763 Mo | 12 Go / 5,5 Go | 4 |
| Sol AI Ultra | qwen3-8b | 5 806 Mo | 16 Go / 9,5 Go | 8 |
| Sol AI Extra | qwen3-14b | 9 530 Mo | 24 Go / 14,5 Go | 8 |

Tailles issues du catalogue, susceptibles de changer. Ce sont des budgets Sol conservateurs, pas des minima constructeur ni des performances mesurées. Un nombre supérieur de paramètres ne garantit pas une meilleure réponse à toute tâche. Seul Mini a été téléchargé avec l’accord de l’utilisateur et exécuté ici ; les autres niveaux ne sont pas encore évalués. Les noms internes restent dans le code et cet inventaire technique, jamais dans les libellés de l’interface.

Le module x64 autonome ajoute environ 118 Mo avant compression, hors modèle. Téléchargement Mini interrompu à 22 %, puis repris et achevé avec les fragments du SDK. Essai réel « 2 + 2 » : réponse « 2 + 2 = 4. », 6,6 s dont 5,5 s de chargement, pic du processus 960 Mo sur Core i5-8365U / 8 Go. Le client sans état borne la génération à 64 tokens ; `ChatSession` du SDK 2.1 réservait le contexte complet et échouait sur ce PC. Aucun fichier de modèle n’a été modifié.

Régressions natives : activation volontaire, licence, persistance, échelles 100/90/80, annulation, suppression, défaillance forcée du seul module suivie d’un nouvel essai réussi. Résultats d’inférence et capture dans `bin/navigation-check-1791381618895` ; ce premier harnais avait une erreur de sortie après les assertions, corrigée ensuite. Les essais suivants vérifient aussi le refus d’utilisation lorsque les autres applications ne laissent plus la réserve mémoire nécessaire. 225 tests du cœur réussissent.

Contrôle hors ligne reproductible : `node tools/check-navigation.mjs bin/sol-ai-preview/Sol.exe --ai --ai-cache=bin/ai-probe/data --ai-offline`. Le catalogue distant est remplacé par une adresse locale inaccessible : le modèle installé répond depuis son cache. Ce contrôle ne bloque pas tout le réseau de Windows et ne prouve pas l’absence de télémétrie minimale du SDK. La télémétrie facultative est désactivée ; catalogue/téléchargements contactent Microsoft.

## Lot 9 — actions contextuelles et personnalisation locale

Un seul nom public et un seul modèle : **Sol AI**, SmolLM3 3B CPU (2 131 Mo annoncés au catalogue), quelle que soit la machine compatible. Les niveaux Mini/Medium/High/Ultra/Extra ne sont plus proposés. Le modèle installé se prépare en arrière-plan à l’ouverture quand Sol AI est activé, puis reste chargé 45 minutes après la dernière action par défaut. Une durée personnalisée (1 à 10 080 minutes) ou une conservation jusqu’à la fermeture est configurable dans Paramètres → Sol. Désactivation, annulation active, fermeture du navigateur ou pression mémoire peuvent libérer le processus. Windows 11 24H2, x64 AVX2/ARM64, environ 8 Go de RAM totale et 4 processeurs logiques restent requis ; la réserve physique et la mémoire allouable de Windows sont vérifiées avant chaque action. ARM64 n’a pas été testé physiquement ici.

Résumé de page via Readability, explication de sélection, rédaction, reformulation, correction et traduction locale fr/en. Les champs sensibles sont exclus. Le résultat apparaît dans une proposition Sol : copier, créer une note ou remplacer explicitement le texte. Un remplacement refuse une page ou un texte modifié depuis la sélection. Dans les notes, il modifie le brouillon sans l’enregistrer. La traduction locale produit du texte ; elle ne traduit pas toute la mise en page d’un site. Le parcours Google Traduction existant reste distinct.

Le SDK Jinja ne prenait pas en charge le modèle SmolLM3 : le format officiel ChatML est rendu puis exécuté avec l’API native ONNX GenAI déjà incluse. Les fichiers du modèle ne sont pas modifiés. Les passages sont séquentiels ; la limite technique commune est 50 000 caractères d’entrée/sortie et 768 tokens de génération par passage sans Think, 2 816 avec Think. Think est activé par défaut et configurable dans Paramètres → Sol ou le panneau d’action, sans recharger le modèle. Le raisonnement est exclu avant décodage à partir du marqueur token de fin ; seul le résultat final est rendu. Une réponse incomplète est signalée et ne peut pas remplacer un texte. Les délais de 3/4 minutes surveillent l’inactivité, pas la durée totale d’une génération qui progresse. Annulation et arrêt du navigateur libèrent le processus.

Personnalisation facultative dans Paramètres → Sol : ton et longueur conservés par profil. L’adaptation à la navigation est désactivée au départ. Si activée, les titres et domaines de six favoris, six pages fréquentes et six pages récentes contextualisent seulement la rédaction et les explications ; ni chemins ni paramètres des URL ne sont utilisés. Informations consultables, mises à jour à la demande depuis le profil, sans copie supplémentaire d’historique, sans entraînement des poids et sans navigation privée. Réinitialiser coupe cette adaptation et remet les préférences à zéro. Effacer l’historique ou retirer un favori retire les sources correspondantes.

Contrôle reproductible : `node tools/check-sol-ai.mjs bin/sol-ai-final/ai/Sol.AI.exe bin/sol-ai-user-preview/ai smollm3-3b-generic-cpu:1`. Six usages hors ligne et deux demandes de rédaction ont passé les contrôles de sens. Certaines rédactions produisent des éléments entre crochets à compléter ; une validation utilisateur reste nécessaire, ces essais ne garantissent pas une exactitude universelle. 226 tests du cœur et builds UI/natif passent.

Contrôle natif : `node tools/check-navigation.mjs bin/sol-ai-final/Sol.exe --ai-actions --ai-cache=bin/sol-ai-user-preview/ai --ai-offline`. Il couvre la sélection, les champs sensibles, le refus des modifications périmées, les trois échelles, le résumé local, les préférences et l’application explicite dans les champs et brouillons.

Think et conservation en mémoire : `node tools/check-navigation.mjs bin/sol-ai-final/Sol.exe --ai-warm --ai-cache=bin/sol-ai-user-preview/ai --ai-offline`, réussi dans `bin/navigation-check-1791414878819`. Résumé avec Think, traduction sans Think sans second chargement, réglages 45/personnalisé/fermeture, expiration réelle après une minute d’inactivité, maintien du panneau au blur, fermeture au clic extérieur et trois échelles vérifiés. Une commande de navigateur envoyée par un site est rejetée ; son canal permet seulement de signaler un clic pour fermer le panneau. La conservation évite le rechargement des poids, pas le coût du calcul CPU ; Think peut rallonger nettement une réponse.

Le rendu des résultats et des aperçus de notes couvre CommonMark/GFM : titres, listes imbriquées, tâches, tableaux, citations, liens, images, notes de bas de page, code coloré et formules KaTeX. Le HTML est nettoyé avant le rendu ; scripts et liens dangereux sont exclus. Les images distantes se chargent seulement sur clic. Les bibliothèques et polices sont livrées avec leurs licences dans `ui/licenses/third-party.txt` du navigateur compilé.

Contrôles natifs Markdown et notes : `bin/navigation-check-1791405618659` et `bin/navigation-check-1791405845219`. Adaptation privée exclue : `bin/navigation-check-1791405721312`. Le contrôle `tools/check-sol-ai-runtime.mjs` a terminé une traduction de quatre passages, au-delà de 6 000 caractères, en 698 secondes, sans limite totale de trois minutes ; il vérifie aussi l’annulation après le début réel de la génération. Cette durée CPU est une mesure sur ce PC, pas une garantie de performance.

### Préparation, réponses progressives et GPU

Le modèle installé se prépare en arrière-plan lorsque Sol AI est activé, au démarrage de Sol et après installation ou optimisation. Il reste ensuite chargé selon la durée configurée. Le résultat apparaît progressivement pendant la génération ; les tokens Think sont exclus avant décodage. Copier, enregistrer et appliquer attendent la réponse terminée.

Paramètres → Sol → Optimiser Sol AI compare un essai fixe sur CPU et sur la variante GPU du même modèle. Le fournisseur WebGPU officiel est enregistré avant de consulter le catalogue matériel. Cette action peut télécharger des composants et la variante GPU ; aucun texte de navigation n’est utilisé. Le GPU est retenu seulement si sa génération est au moins 20 % plus rapide lors de cet essai, hors chargement. Sinon sa variante est supprimée ; le modèle CPU reste disponible. Un échec du GPU pendant une action reprend sur CPU. Le résultat mesuré est conservé, sans promettre des réponses instantanées sur toutes les machines.

Contrôle : `node tools/check-sol-ai-acceleration.mjs bin/sol-ai-accelerated/ai/Sol.AI.exe <cache-de-test> smollm3-3b-generic-cpu:1`. Vérifie la sélection matérielle, la préparation avant demande, une génération sans rechargement, les sorties progressives et l’annulation. Le catalogue d’optimisation contacte Microsoft ; la génération du contrôle utilise le cache hors ligne.

Essai réel sur i5-8365U / Intel UHD 620 : CPU 11 774 ms, GPU 15 598 ms pour la traduction fixe, hors chargement ; CPU retenu. Contrôle natif final réussi dans `bin/navigation-check-1791417913527` : Think masqué, réponses progressives, réutilisation sans chargement, expiration personnalisée, fermeture par clic extérieur et échelles 100/90/80. Le contrôle mémoire actualise les mesures du processus chargé et compte sa mémoire privée déjà allouée, sans supprimer la réserve de sécurité. Les copies de modèles des tests ont été supprimées ; le modèle principal est conservé.

### Effacement de la session d’un site (lot 8)

Clic droit dans la page → **Vider la session de ce site…**, confirmation centrée avec hôte et session. Efface cookies (y compris HttpOnly et tous chemins), DOM/IndexedDB/CacheStorage/service workers de l’origine, sessionStorage des vues chargées de cette origine, autorisations Sol et copie mémoire des cookies de session. Recharge les vues concernées. Les cookies parent-domain partagés avec des sous-domaines sont aussi supprimés. Le cache HTTP WebView2 ne peut pas être purgé par origine : seul ce cache est vidé pour toute la session ; cookies et stockages des autres sites/sessions restent séparés. Mots de passe, historique et favoris conservés. Téléchargement de l’onglet en cours : attendre sa fin.

Contrôle : `node tools/check-navigation.mjs bin/sol-ai-preview/Sol.exe --site-reset`. Vérifie clic droit réel via le moteur, annulation, refus d’une confirmation périmée, suppression des différents stockages et maintien des cookies d’une autre origine et du même site dans une autre session. Aucune donnée du profil utilisateur n’est utilisée.

Contrôle final réussi dans `bin/navigation-check-1791383474949`, avec cookies HttpOnly, autorisations et rechargement du document vérifiés. Réglages Sol AI dans une fenêtre 400 × 400, licence et persistance : `bin/navigation-check-1791383407259`, sortie 0. Nouvel essai CPU isolé pendant que le PC était occupé : 14,6 s, pic 957 Mo ; la latence est une mesure ponctuelle, pas une garantie.
