# Sessions et extensions

La session appartient à l’onglet. Le domaine visité ne la remplace jamais automatiquement.
Les anciens choix par site ne sont plus appliqués ; leurs fichiers ne sont pas supprimés.

| Cas | Comportement |
| --- | --- |
| Nouvel onglet, adresse saisie, raccourci ou favori | Reprend la session de l’onglet actif au moment de l’ouverture. |
| Lien, nouvel onglet par clic droit, `window.open`, authentification | Reprend la session de l’onglet source, même si un autre onglet est actif. |
| Navigation vers un autre site, redirection, précédent, suivant, actualisation | Conserve la session de cet onglet. |
| Changement de session d’un onglet | Recharge uniquement cet onglet dans le profil moteur choisi ; les autres restent inchangés. |
| Retour vers un onglet A après avoir changé B | A retrouve sa propre session, sans reprendre celle de B. |
| Duplication, réouverture d’un onglet fermé | Conserve la session de l’onglet d’origine. |
| Épinglage, groupes, vue scindée, mise en veille | Ne change pas l’identité de session stockée avec l’onglet. |
| Redémarrage | Chaque adresse sauvegardée reprend sa session. |
| Suppression d’une session | Refusée tant qu’un onglet l’utilise ; retire aussi ses onglets de la pile de réouverture. |
| Identité restaurée devenue introuvable | Ouvre l’accueil plutôt que le site avec les connexions d’un autre compte. |

## Extensions communes

Le fichier `extension-state.json` appartient au profil Sol. Une installation, activation,
désactivation, mise à jour ou suppression est appliquée aux sessions ouvertes et aux suivantes.
Une suppression garde un marqueur pour empêcher une ancienne session de réinstaller l’extension.
Une extension désactivée n’est pas chargée dans une nouvelle session.

Les installations des anciennes sessions sont reprises en lecture seule depuis leurs préférences
WebView2, sans ouvrir ces sessions. Une ancienne désactivation est conservée. Les préférences
natives ne sont jamais modifiées par la migration.

Les cookies, connexions et données `chrome.storage` restent isolés entre sessions ; la liste
d’extensions et les préférences d’épinglage sont communes. Les profils Sol restent distincts.
Les extensions utilisateur ne sont pas chargées en navigation privée.

## Compatibilité

Les popups et les pages `side_panel.default_path` sont ouvertes dans leur propre contexte
d’extension, avec les API natives disponibles dans WebView2. Leur vue n’est pas considérée
comme le site actif lors d’une requête `chrome.tabs.query`.

WebView2 n’expose pas toutes les fonctions d’interface de Microsoft Edge. Une action sans
popup, comme celle de PromptCard, propose d’ouvrir la page et d’installer l’extension dans Edge.
Le même repli est accessible dans le gestionnaire pour les autres extensions. Installation et
connexion dans Edge sont séparées ; les cookies de Sol ne sont pas transférés.

## Vérification

- `dotnet test tests/Browser.Core.Tests -c Release --no-restore`
- `node tools/check-extension-activation.mjs` : popup, panneau, onglet actif et repli Edge.
- `node tools/check-session-inheritance.mjs` : sessions A/B/C, cookies et stockage d’extension
  isolés, liens, duplication, réouverture, historique, redirection, autre domaine, suppression,
  activation/désactivation communes et redémarrage.

Les tests natifs utilisent uniquement un profil temporaire, des extensions et des comptes fictifs.
Ils n’effectuent pas de connexion à un service réel ni d’installation dans le vrai Microsoft Edge.
