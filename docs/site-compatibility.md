# Prime Video et session GitLab

## Prime Video

Le script `streaming-fix.js` a été retiré. Il soumettait automatiquement les
formulaires de consentement Amazon toutes les 600 ms, masquait certains calques
et changeait le verrouillage du défilement. Cela interférait avec les interfaces
de consentement et de profil/PIN. Sol laisse maintenant ces interactions au site.

Un diagnostic sur la vraie page Prime a ensuite identifié une autre panne :
`DVWebClient_app` lit `chrome.webview.hostObjects.sync.PrimeVideoWinRTComponent`
sans protéger cette détection. Le proxy WebView2 refuse cet accès avec
`0x80070005`, interrompant l’initialisation JavaScript avant la lecture.
Sol n’intègre pas l’application WinRT d’Amazon. `web-host-compat.js` expose donc
un espace d’objets natifs vide uniquement sur Prime/Amazon, tout en conservant
`AreHostObjectsAllowed=false` et les messages filtrés de Sol. Les autres sites
gardent le comportement du moteur. Aucun droit natif n’est accordé au site.

`node tools/check-streaming-compat.mjs` lance Sol hors écran sur une page HTTPS
interceptée, sans compte Prime ni requête de licence. Il vérifie l’absence de
soumissions automatiques, les calques et les clics réels de profil/lecture.
Il sonde aussi les capacités EME Widevine et PlayReady du runtime. Un résultat
« available » ne garantit pas l’acceptation du navigateur, des codecs et de
la licence par Prime Video. Le correctif a aussi été essayé dans la session
Prime réelle : le profil Jourdan ouvre le catalogue et le lecteur. L’utilisateur
a ensuite confirmé que la lecture fonctionne.

## GitLab

Une sauvegarde uniquement à la fermeture normale ne couvre pas un arrêt forcé,
ni une interruption après restauration. Sol crée désormais un point de reprise
chiffré DPAPI lorsque le serveur modifie `_gitlab_session` ou `gitlab_user`.
Les réponses rapprochées sont regroupées ; le cookie courant est relu dans le
moteur pour suivre les rotations. Les valeurs ne sont jamais inscrites aux logs.

Le point de reprise reste disponible après restauration et est invalidé dès
la suppression des cookies à la déconnexion ou lors de l’effacement du site.
Les profils de session restent séparés et la navigation privée n’écrit aucune
sauvegarde. Les autres cookies gardent leur durée normale. La validité côté
serveur et les révocations GitLab restent applicables.

- `node tools/check-gitlab-session.mjs` : redémarrages réels, profils isolés,
  chiffrement, attributs des cookies, déconnexion, effacement et navigation privée.
- `node tools/check-gitlab-interrupted.mjs` : point de reprise avant fermeture,
  rotation, interruption après restauration et déconnexion suivie d’un arrêt forcé.

Tous ces essais utilisent des profils jetables et des cookies factices. Leurs
données sont effacées après arrêt ; les rapports restent dans `bin/`.
