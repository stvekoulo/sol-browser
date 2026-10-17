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

Au redémarrage, seuls les onglets affichés sont chargés. Les épingles, raccourcis et onglets
restaurés en arrière-plan restent endormis jusqu’à leur premier clic, dans leur session sauvegardée.

## Autorisations des sites

Les choix caméra, micro et autres autorisations sont enregistrés par origine et par session.
Ils sont aussi synchronisés avec le profil natif WebView2 avant sa première navigation :
les sites retrouvent leur état autorisé ou refusé dès le redémarrage. Les réponses existantes
de Sol sont reprises sans nouvelle demande. Deux demandes simultanées sont présentées à la suite.

Oublier une autorisation dans les paramètres, ou vider la session du site, efface également
le choix natif. Fermer une question ou quitter l’onglet refuse cette demande sans mémoriser
un nouveau choix. Une fenêtre privée utilise uniquement ses données temporaires.

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

Les actions sans popup, comme celle de PromptCard, sont exécutées dans Sol. Une adaptation
du script de fond relaie le clic vers les écouteurs de l’extension ; une commande de menu
natif accorde `activeTab` à l’onglet visé, sans élargir ses droits aux autres sites.
La commande interne est masquée ; les menus contextuels propres aux extensions restent disponibles.
Ce même passage natif fournit l’identité de l’onglet aux popups et panneaux, même quand des
épingles ont été chargées en arrière-plan. Il n’ajoute aucun droit d’accès aux sites.
Sol ne propose plus d’ouvrir ou d’installer Microsoft Edge pour exécuter une extension.

Les API restent celles du moteur WebView2 : la compatibilité de toutes les extensions Chrome
et de leurs services externes n’est pas garantie.

## Vérification

- `dotnet test tests/Browser.Core.Tests -c Release --no-restore`
- `node tools/check-extension-activation.mjs` : action native avec `activeTab`, menus contextuels,
  popup, panneau, onglet actif et erreur signalée dans Sol.
- `node tools/check-restored-pins.mjs` : aucun chargement des épingles ou raccourcis en arrière-plan
  après ouverture et redémarrage ; chargement uniquement au premier clic.
- `node tools/check-download-reappearance.mjs` : encart natif réaffiché pour chaque téléchargement
  successif, même quand sa taille ne change pas ; réglage d’activation respecté.
- `node tools/check-start-favorites.mjs` : quatre rangées de favoris, affichage complet et vue étroite,
  sans sites fréquents ni vues cachées pour leurs aperçus.
- `node tools/check-permission-persistence.mjs` : caméra/micro simulés, redémarrage, révocation
  native, refus persistants, demandes simultanées et isolation des sessions.
- `node tools/check-session-inheritance.mjs` : sessions A/B/C, cookies et stockage d’extension
  isolés, liens, duplication, réouverture, historique, redirection, autre domaine, suppression,
  activation/désactivation communes et redémarrage.

Les tests natifs utilisent uniquement un profil temporaire, des extensions et des comptes fictifs.
Ils n’effectuent pas de connexion à un service réel ni d’installation dans le vrai Microsoft Edge.
Le test média utilise des appareils simulés et conserve la vraie question d’autorisation de Sol.
