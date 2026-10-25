# Téléchargement vidéo dans Sol

La détection est activée par défaut et ne lance aucun téléchargement. Le bouton
vidéo de la barre d’adresse ouvre directement les qualités déjà connues du lecteur
ou des réponses HLS/DASH, sans lancer d’inspection au clic. L’utilisateur choisit
une qualité réellement annoncée par le site, puis Télécharger.
L’éventail contextuel d’un lecteur vidéo propose aussi « Télécharger la vidéo… ».
Il ouvre le même choix de sources et de qualité, sans lancer le téléchargement.

Dans Paramètres → Général, la préférence peut être automatique, 480p, 720p,
1080p, 1440p, 4K ou 8K. Une préférence absente propose la meilleure résolution
inférieure ; à défaut, la plus basse disponible. Le sélecteur explique ce repli.
Il ne crée pas de résolution artificielle.

## Playlists et sélection multiple

Dans le sélecteur vidéo, **Playlist** recherche les éléments de la page avec
l’extracteur, sans les télécharger. Un lien YouTube `watch?...&list=...` est
converti en URL de playlist uniquement pour cette demande explicite. La lecture
ordinaire et le bouton de téléchargement d’une seule vidéo gardent `--no-playlist`.
Les listes sont paginées par 100 éléments, avec Précédentes/Suivantes ; seules les
cases cochées de la page courante sont ajoutées à Téléchargements. Les vidéos
supprimées, privées, en direct ou marquées DRM ne sont pas sélectionnables lorsque
l’extracteur indique leur état. Une entrée dont l’état devient indisponible peut
échouer sans arrêter les autres téléchargements.

**Plusieurs vidéos de la page** propose les sources déjà détectées, y compris dans
les lecteurs intégrés. Plusieurs sources peuvent représenter des variantes ou des
lecteurs alternatifs d’une même vidéo : l’utilisateur choisit celles à conserver.
Sol n’explore pas automatiquement tous les épisodes d’un catalogue qui ne fournit
pas de playlist exploitable par l’extracteur.

Chaque élément a sa carte et ses commandes pause/reprise/annulation. Deux vidéos
sont transférées à la fois ; les autres attendent sans ouvrir de processus vidéo.
Une qualité commune est appliquée séparément à chaque vidéo avec le tri
[`height:<préférence>` de yt-dlp](https://github.com/yt-dlp/yt-dlp#sorting-formats) :
meilleure résolution inférieure ou égale, sinon la plus basse au-dessus. La valeur
automatique choisit la meilleure disponible. Aucun agrandissement artificiel.

Les lecteurs de sites tiers sont traités par leurs fichiers MP4/WebM ou manifestes
HLS/DASH, sans liste de domaines autorisés. Sol conserve le Referer du lecteur
intégré et les cookies applicables à ses origines pour les hébergeurs qui les
exigent. Cela ne garantit pas la compatibilité de tous les hébergeurs : les
protections DRM, les vérifications interactives et les restrictions serveur
restent applicables, indépendamment du nom du site.

## Architecture

- WebView2 observe les réponses réseau : fichiers MP4/WebM/MOV/MKV/OGV,
  manifestes HLS et DASH. Les fragments `.ts`/`.m4s` sont exclus des fichiers.
- Un observateur DOM signale les lecteurs, les changements de source et les
  événements de protection, y compris dans les cadres. Une URL `blob:` est un
  lecteur, pas un fichier téléchargeable. Les métadonnées du lecteur YouTube et
  les manifestes déjà reçus fournissent les qualités sans téléchargement supplémentaire.
  Si ces données sont absentes, l’extraction de la page reste un recours.
- Les identifiants proposés sont internes à l’onglet et au document courant.
  Les messages d’une page ne démarrent jamais directement un téléchargement.
- Les fichiers directs démarrent par HTTP sans moteur externe. Les pistes
  vidéo/audio résolues sont téléchargées en parallèle, puis assemblées.
  Les requêtes reprennent avec Range et un ETag fort ; sinon le fichier repart
  de zéro pour éviter de juxtaposer deux versions.
- yt-dlp retrouve les URL non résolues et télécharge les flux segmentés ; FFmpeg assemble les
  pistes séparées. Sol fournit progression, pause/reprise et annulation dans
  ses cartes de téléchargement habituelles. La reprise conserve les fichiers
  partiels tant que le site permet de réutiliser la source.
- Fermer Sol annule ses processus enfants. Les erreurs affichées ne recopient
  pas de lignes du moteur, qui pourraient contenir des URL signées.

## Moteurs et données

Les outils sont préparés en arrière-plan dès la première détection, dans le dossier de données
Sol `tools/video`, sans installation globale ni IDM. Seuls les actifs des
distributions officielles [yt-dlp](https://github.com/yt-dlp/yt-dlp/releases)
et [BtbN FFmpeg](https://github.com/BtbN/FFmpeg-Builds/releases), ainsi que le
runtime officiel [Deno](https://github.com/denoland/deno/releases), sont acceptés.
Le SHA-256 annoncé par l’API GitHub est vérifié avant extraction/exécution.
Un reçu local permet de revérifier les fichiers mis en cache.

FFmpeg utilise la distribution LGPL partagée ; ses fichiers de licence sont
conservés avec la distribution. Aucun binaire IDM ou code de son extension
n’est repris. Les exécutables ne sont pas inclus dans le dépôt ni l’installeur.
Les sources et conditions des moteurs restent disponibles chez leurs auteurs.

Seuls les cookies applicables à la page, au lecteur intégré et à la source choisie sont transmis
au moteur via un fichier temporaire Netscape, supprimé en fin d’opération.
Ils ne sont pas journalisés. Aucun export de l’ensemble du profil n’est fait.

## Limites explicites

La disponibilité dépend du site, de l’expiration des liens et des extracteurs.
Pas de contournement DRM ni de capture d’écran réencodée ; les directs sont
refusés lors de l’inspection. Un site exigeant des en-têtes spécifiques ou
une vérification interactive peut refuser le téléchargement. Certains
extracteurs nécessitent un moteur JavaScript : Deno est fourni automatiquement
sur Windows 64 bits et passé explicitement à yt-dlp. Cela suit le parcours
[EJS officiel de yt-dlp](https://github.com/yt-dlp/yt-dlp/wiki/EJS).
L’affichage des qualités n’attend pas ces outils. Un premier téléchargement
segmenté ou une URL YouTube à résoudre peut toutefois attendre leur acquisition
et les réponses du site : aucun délai universel de cinq secondes n’est garanti.
Sur Windows 32 bits, FFmpeg win64 est indisponible : seules les variantes
déjà réunies peuvent être téléchargées. Windows ARM64 utilise FFmpeg win64
par émulation ; cette architecture nécessite une validation sur appareil.

## Analyse IDM avec REA — 22 octobre 2026

Cible locale : `IDMGCExt.crx`, extension IDM Integration Module 6.43.1.
SHA-256 CRX : `1D2297CCBDBBF06E9E2546BB9F9B9985433EB7C02D75F09BBCBC9784ECE583F7`.
Le conteneur CRX3 n’étant pas accepté par REA, son en-tête de 1320 octets a été
retiré pour analyser son ZIP incorporé, sans modifier les fichiers embarqués.
SHA-256 ZIP : `331A7B98AED7583B4B897205CD0393462A668576F0C5D32339EF1EA897DEB946`.

REA a inventorié 57 entrées et extrait 37 fichiers sans contradiction
d’intégrité. Son analyse statique de `background.js`, `content.js` et
`document.js` a parcouru 35 746 nœuds AST, sans exécuter ces scripts.
Le manifeste observé déclare `webRequest`, `downloads`, `cookies` et
`nativeMessaging`, avec des scripts à `document_start` dans tous les cadres.
Le manifeste natif `com.tonec.idm` relie cette extension à `IDMMsgHost.exe`
par stdio. Des types MIME HLS/DASH et vidéo apparaissent dans l’extension.

Interprétation : la séparation observation du navigateur / moteur natif
convient à Sol. Cette analyse ne démontre ni les algorithmes de segmentation
du moteur propriétaire, ni les performances IDM, ni une compatibilité universelle.
La couverture sémantique REA est partielle et les scripts sont obfusqués.

Identifiants de preuve REA :

- Inventaire : `ev_1b16af21785ce17808fe415c54cc305a2df1faae89b68fef1976cb58cdbe9a2e`.
- Extraction : `ev_688296633762e28b49ba6b46ea228a2def66f977f6fae980f419bccaf0839e0f`.
- Analyse JS : `ev_831978813040a7a42aaa1dd937f332d8d7a72a5e7880baa1eab5b5de7b692f07`.

Le comportement visible documenté par [IDM](https://www.internetdownloadmanager.com/register/new_faq/video5.html)
confirme la proposition de formats/qualités après le début de la lecture.
L’implémentation Sol est indépendante de ces scripts propriétaires.

## Vérification reproductible

Après compilation de l’interface et de Sol en Release x64 :
`node tools/check-video-instant.mjs` mesure sous cinq secondes la détection,
l’affichage des qualités et le démarrage d’un fichier direct, moteurs absents.
Ce banc utilise des métadonnées de lecteur et des réponses locales déterministes,
et vérifie le cookie HttpOnly, les octets et le repli de qualité.

`node tools/check-video-downloads.mjs`. Charger d’abord `tools/env.ps1`.
Le banc prépare les moteurs vérifiés si nécessaire et génère ses propres
vidéos locales. Il teste HLS 720p/1080p, le repli 1440p → 1080p, le choix
720p, l’audio, DASH séparé, les cookies HttpOnly, l’éventail par vrai clic,
pause/reprise/annulation, navigation, désactivation et blocage DRM.
Les profils, cookies et téléchargements de test sont effacés après arrêt ;
le rapport JSON, les captures et le journal natif restent dans `bin/sidebar-check-*`.

`node tools/check-video-collections.mjs <chemin-Sol.exe>` utilise une playlist
locale de 101 vidéos et les vrais moteurs : pagination 100/1, sélection beUI de
trois éléments, rejet des identifiants arbitraires/périmés, file de deux transferts,
pause/reprise d’un élément en attente, repli 720p vers la source 360p, fichiers
complets et conservation du Referer d’une iframe. Le sélecteur est contrôlé à
320 px. Ces essais locaux ne certifient aucun catalogue tiers.
