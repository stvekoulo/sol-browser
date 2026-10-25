# Agent navigateur Sol AI

Sol AI exécute les demandes dans le navigateur avec le raisonnement du compte ChatGPT connecté.

## Actions disponibles

- Lire et rechercher dans les pages et notes, consulter les liens réels d’un site.
- Examiner les champs d’un formulaire : libellés, contraintes, options et états de validation, sans lire les valeurs déjà saisies. Une recherche par libellé permet d’examiner un grand formulaire par parties.
- Remplir plusieurs champs, choisir des options, cocher, cliquer, utiliser le clavier et le survol, faire défiler la page.
- Extraire les tableaux HTML et grilles ARIA en lignes et cellules structurées ; collecter et dédupliquer les liens visibles par texte ou adresse.
- Attendre jusqu’à cinq secondes un texte visible après une action asynchrone. Un délai dépassé reste un échec à examiner.
- Rechercher des onglets existants, les ouvrir, activer, dupliquer, épingler, regrouper, fermer ou rouvrir ; changer le zoom et naviguer en arrière/avant.
- Afficher deux pages côte à côte, préparer un groupe de ressources et une note de travail.

Les références d’éléments sont renouvelées à chaque lecture ou inspection de formulaire. L’agent doit relire la page après un changement et vérifier le résultat avant d’annoncer une réussite. Il ne doit pas répéter aveuglément un envoi après une erreur ou un délai dépassé. Les sorties volumineuses restent bornées et signalent leur troncature.

## Autorisation permanente

Par défaut, les modifications de site et fermetures demandent un accord. Dans **Paramètres → Sol → Sol AI**, activer **Toujours autoriser les actions de Sol AI** pour accorder une fois l’autorisation aux demandes exécutées dans ce profil, sur tous les sites. La carte d’accord de la discussion propose également **Toujours autoriser**.

Le choix est enregistré avant de libérer les actions en attente et reste actif au redémarrage. Le désactiver rétablit les demandes d’accord pour les prochaines actions. Une carte périmée ne peut pas activer ce droit ; un échec d’enregistrement conserve le réglage précédent et ne libère aucune demande. La navigation privée ne peut pas changer ce choix.

L’autorisation supprime les demandes répétées ; l’agent continue à suivre les demandes de l’utilisateur et traite le contenu des sites comme des données, pas comme des instructions.

## Développement web et diagnostics

Le bouton **Sol AI** de l’inspecteur ouvre une discussion dédiée, sans envoyer de journal à lui seul. **Expliquer avec ChatGPT** est disponible sur chaque message console et dans le détail d’une requête réseau.

Lorsqu’un diagnostic est demandé, les outils permettent de lire les erreurs JavaScript, métadonnées réseau et détails ciblés de requête. Le résumé exclut les en-têtes et corps ; leur lecture exige un appel distinct. Les détails limitent la réponse textuelle à un extrait, excluent le binaire et masquent les secrets connus. Ce masquage ne garantit pas l’absence de données privées. L’accord ponctuel indique la destination ChatGPT ; l’option d’autorisation permanente inclut explicitement ces diagnostics demandés.

L’agent doit distinguer les faits des hypothèses, s’appuyer sur les statuts, traces et réponses effectivement capturés, puis proposer un correctif et sa vérification. Il ne dispose pas d’un accès au projet local : il peut proposer du code, pas annoncer une modification de fichiers.

Les commandes comprennent l’ouverture des DevTools, le contrôle de la capture (pause, reprise, effacement, conservation), la simulation d’une réponse Fetch/XHR pour une URL et méthode exactes (statut HTTP, délai, timeout, JSON invalide ou réponse vide), ainsi que la simulation d’appareils pour tester le responsive. Une seule règle réseau est active par onglet ; une simulation temporaire doit être retirée après le test. Le serveur n’est pas modifié.

La capture commence à l’ouverture de l’inspecteur ; les requêtes antérieures ne sont pas récupérables. Sans preuve capturée, Sol doit expliquer cette limite. Un statut 0 ou une requête encore en attente ne prouve pas un échec.

## Vocal masqué

Le vocal Windows est temporairement masqué à la suite du retour utilisateur sur sa qualité. Les commandes natives vocales sont également bloquées : aucun microphone n’est lancé par Sol AI. Le composant officiel [beUI Voice Orb](https://beui.dev/components/agents/voice-orb) reste intact et son module n’est chargé que si le vocal redevient disponible.

Options examinées le 23 octobre 2026 : [Vosk](https://alphacephei.com/vosk/models) propose un petit modèle français de 41 Mo ; les petits modèles utilisent environ 300 Mo de RAM. [whisper.cpp](https://github.com/ggml-org/whisper.cpp) documente 75 Mio et environ 273 Mo de RAM pour tiny, avant éventuelle quantification. Ces options ne répondent pas au critère de poids quasi nul ; aucune amélioration sur la voix réelle de l’utilisateur n’a été établie. Aucun moteur supplémentaire n’est installé.

## Limites

- Deux pages visibles en vue scindée ; les autres restent accessibles dans les onglets.
- Aucun accès aux onglets privés. La duplication exclut aussi les sessions isolées pour préserver leur session.
- Les champs de mot de passe, paiement, OTP, fichier et les CAPTCHA restent traités par l’utilisateur.
- Les iframes d’un autre domaine et les shadow roots fermés ne sont pas accessibles.
- Les contrôles propriétaires et permissions des sites peuvent empêcher certaines actions. Sol doit signaler précisément ces échecs.

## Vérification

- `dotnet test tests/Browser.Core.Tests/Browser.Core.Tests.csproj -c Release` : politiques, enregistrement, révocation, demandes périmées, exécuteur réel des autorisations et outils d’onglets.
- `node tools/check-agent-actions.mjs` : WebView2 réel, formulaire, contraintes, validation, références périmées, tableaux, liens et résultats différés.
- `node tools/check-ai-permissions.mjs` : vocal masqué et bloqué, module vocal non chargé, réglage réel, redémarrage, révocation, carte d’accord responsive et isolation privée avec compte synthétique.
- `node tools/check-ai-developer.mjs` : erreurs console et HTTP réelles, masquage, simulations de réponse et timeout, restauration du serveur et accès à l’aide développeur sans envoi automatique.

Ces vérifications ne constituent pas une session d’inférence avec un compte ChatGPT réel.
