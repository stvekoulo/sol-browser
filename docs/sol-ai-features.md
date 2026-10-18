# Sol AI — liste proposée des fonctionnalités

18 octobre 2026. Sol AI utilise uniquement le modèle distant de ChatGPT, avec le forfait de l’utilisateur (Sign in with ChatGPT). Le moteur et les modèles locaux ont été retirés.

## Premier lot : connexion et actions sur le contenu — intégré, à valider avec un compte réel

1. **Connexion ChatGPT** : connexion, choix du compte, déconnexion et révocation ; aucun partage de clé API pour les comptes éligibles.
2. **Compte et consommation** : afficher le fournisseur utilisé, les modèles accessibles au compte, les limites rencontrées et le lien de gestion de l’usage ChatGPT.
3. **Résumé de page** : version courte ou détaillée, idées essentielles et liens vers les passages utilisés.
4. **Explication d’une sélection** : simplifier, définir les termes et expliquer un passage ou un extrait de code.
5. **Questions sur la page** : réponses ancrées dans le contenu choisi, avec références ; signaler une information absente au lieu de l’inventer.
6. **Traduction de sélection** : conserver le sens, les noms, le code et la mise en forme utile ; proposer plusieurs langues. La traduction Google de pages reste disponible.
7. **Aide à la rédaction** : corriger, reformuler, raccourcir, développer et changer le ton ; copier ou remplacer le texte seulement à la demande.

Points d’entrée : bouton Résumer de la barre d’adresse, « Sol AI… » du menu contextuel (page, sélection ou champ), panneau de discussion à droite (bouton de la barre d’outils) et Paramètres → Sol (connexion, usage, ton, longueur, classement). Interface en composants beUI (`streaming-response`, `agent-progress`, `prompt-input`, `alert`, `message-scroller`, `message`), réponse progressive et annulation. Pas encore fait : liens vers les passages du résumé (point 3), choix de modèle (point 2, le premier modèle listé pour le compte est utilisé). L’intégration ChatGPT doit suivre ses règles d’identification et d’usage. [Parcours officiel](https://developers.openai.com/siwc/token-sharing-open-source), [interface requise](https://developers.openai.com/siwc/ui-ux-guidelines).

## Ajouts intégrés

- **Discussion libre** : panneau à droite pour des questions rapides, conversation gardée en mémoire dans la fenêtre et renvoyée à chaque question (`store: false`).
- **Classement de l’historique et des favoris** : option désactivée par défaut. Le classement se fait **par site** (domaine), par lots de 250 et au plus 10 lots par jour : une grande bibliothèque (plusieurs milliers de liens) est traitée sur quelques jours, puis seuls les nouveaux sites sont envoyés. Seuls le domaine et un titre partent chez OpenAI. Au plus 12 catégories, affichées sur la page d’accueil (« Vos sujets ») avec leur nombre de liens et « Voir tout ». Bouton « Classer maintenant » et progression dans Paramètres → Sol.
- **Choix du modèle** : Paramètres → Sol propose les modèles disponibles sur le compte ChatGPT ; « Automatique » utilise le premier modèle recommandé par OpenAI.

- **Agent sur le site ouvert** : « Question » et le panneau de discussion consultent l'onglet en direct avec des outils exécutés par Sol (`inject/agent.js`) : lecture structurée (titres, texte, liens, boutons et champs avec références), recherche, lecture des liens (même site avec la session de l'utilisateur ; autres sites sans cookies et seulement pour les liens listés), mise en évidence. Cliquer, saisir et ouvrir une adresse demandent l'accord de l'utilisateur (carte beUI `approval-card`) ; les champs sensibles ne sont ni lus ni remplis. Trace des étapes avec `agent-activity`.
- **Notes assistées** : le panneau de discussion (bouton « Sol AI » des notes) cherche, lit, résume et crée des notes à partir d'une consigne.
- **Actions directes au clic droit** : Résumer la page, Expliquer, Traduire, Corriger ou Reformuler s'exécutent tout de suite ; « Sol AI… » garde le choix complet.

- **Groupes d’onglets** : à partir de six onglets hors groupe, une carte de la barre latérale propose « Ranger avec Sol AI » ; les groupes proposés (nom, couleur, icône) s’appliquent seulement après « Appliquer ». L’éditeur de groupe propose un nom, une couleur et une icône d’après ses onglets, et la couleur se choisit avec le sélecteur beUI. Un sujet de « Vos sujets » s’ouvre en groupe d’onglets depuis l’éventail de sa carte.
- **Mascotte** : la balle aux yeux (bloc « Eye tracker » de Bencho, MIT) représente Sol AI dans la vue, la discussion, les paramètres, les notes et la barre d’outils.
- **Éventail dans les pages** : au coin du texte sélectionné (Copier, Sol AI, et « Plus » qui ouvre les actions du clic droit), des liens en forme de carte (nouvel onglet, vue scindée, copier le lien), des images (ouvrir, copier le lien, ajouter à une note) et des blocs de code (copier, expliquer avec Sol AI, ajouter à une note). Isolé dans un Shadow DOM fermé ; chaque clic porte une clé que la page ne peut pas imiter.
- **Éventail d’actions** : au coin des favoris (nouvel onglet, vue scindée, copier), des sujets (ouvrir en groupe, voir tout), des groupes (afficher, modifier) des lignes d’historique de l’accueil, des réponses de la discussion (copier, en faire une note) et des notes (épingler, dupliquer, archiver, résumer avec Sol AI), d’après le bloc « Action node » de Bencho. Jamais d’action irréversible dans un éventail.

## Deuxième lot : notes, plusieurs pages et outils de développement

8. **Notes assistées** : résumer une note, proposer un titre, structurer des idées et produire une liste d’actions ; enregistrer uniquement après choix de l’utilisateur.
9. **Comparaison de pages choisies** : comparer produits, documents ou sources dans un tableau, relever les différences et conserver leurs liens.
10. **Extraction structurée** : récupérer les informations utiles d’une page ou d’un tableau, puis copier ou exporter le résultat en Markdown/CSV.
11. **Compréhension des documents** : résumer et expliquer le texte extrait ou sélectionné d’un PDF ou d’une documentation.
12. **Assistant code et CSS** : expliquer le code sélectionné, les styles inspectés et proposer des corrections à copier.
13. **Diagnostic réseau et console** : analyser une requête ou une erreur choisie ; expliquer la cause probable et proposer des vérifications après masquage des secrets.

## Troisième lot : capacités avancées

14. **Recherche web assistée** : compléter une question par des sources actuelles et citer les pages, si le modèle et les règles du compte l’autorisent.
15. **Analyse de capture ou d’image** : expliquer une interface ou une erreur visuelle, comparer des captures et lire du texte selon les capacités du modèle sélectionné. Déclenchement explicite.
16. **Actions locales dans le navigateur** : trouver un élément, naviguer, sélectionner, préparer une saisie et exécuter un petit parcours validé. Développer des outils Sol dédiés ; demander une confirmation adaptée avant un effet réel.
17. **Recherche sémantique facultative** : retrouver une idée dans des notes ou un historique choisi ; étudier séparément le moteur d’indexation, le coût matériel et le consentement. Exclure la navigation privée de l’index persistant.

La recherche web et les entrées image/fichier dépendent du compte et du modèle. Le parcours ChatGPT actuel exclut Computer Use natif, génération d’images, Code Interpreter, recherche de fichiers et MCP hébergé. Les actions du point 16 utilisent les fonctions locales de Sol. [Modèles accessibles](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference), [limites actuelles](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations).

## Études séparées

- **Transcription et traduction vocale** : le partage de forfait documenté ne prend pas en charge l’audio/vidéo ni l’API de transcription ; hors périmètre tant que ce parcours ne l’autorise pas.
- **Lecture des réponses à voix haute** : réutiliser les voix locales et le lecteur Sol déjà présents.
- **OCR** : privilégier les capacités disponibles du système pour extraire du texte ; utiliser l’analyse multimodale uniquement si nécessaire et autorisée.

## Règles communes

- Fonction facultative, démarrée à la demande ; calcul distant par ChatGPT uniquement. Mesurer la consommation réelle et le délai réseau.
- Choisir la page, la sélection, la capture ou les notes à envoyer. Aucun envoi automatique de mots de passe, de cookies ou de jetons ; historique et favoris seulement si le classement quotidien est activé, sous forme de titres et domaines.
- Le contenu d’un site est une donnée non fiable et n’autorise aucune action.
- Sol AI est indisponible en navigation privée. Les jetons sont chiffrés pour l’utilisateur Windows (DPAPI) et restent hors de React, des pages et des extensions.
- Conserver la navigation, les notes, les captures et les outils ordinaires lorsque l’IA est désactivée, indisponible ou limitée.

Ajouter les deuxième et troisième lots après validation du premier avec un compte ChatGPT Plus ou Pro. Voir aussi `sol-ai-chatgpt-feasibility.md` pour les exigences OAuth et les points restant à tester.
