# Démonstrations Sol AI 1.1.0 — actions enchaînées

Prompts à copier dans la discussion Sol AI. Gmail est exclu. Les scénarios décrivent les capacités des outils ; ils ne constituent pas des validations du comportement du modèle sur chaque site.

Pour une démonstration fluide, activer « Toujours autoriser les actions de Sol AI » dans Paramètres → Sol → Sol AI. Cette option inclut les diagnostics demandés transmis à ChatGPT ; elle est révocable. Les sites peuvent présenter des CAPTCHA ou des contrôles incompatibles. La vue scindée affiche deux pages.

## 1. Recherche croisée et dossier de synthèse

Préparer trois onglets contenant des articles ou documentations publiques sur le même sujet.

> Retrouve les trois onglets consacrés à ce sujet parmi mes onglets ouverts. Lis réellement leurs contenus, compare leurs arguments et identifie leurs désaccords. Crée une nouvelle note « Dossier comparatif » avec un tableau des points clés, les limites de chaque source et leurs liens. Regroupe uniquement ces trois ressources sous « Recherche », puis affiche la première ressource à côté de la note. Termine par les trois conclusions les mieux étayées et indique ce que les sources ne permettent pas de conclure.

Enchaînement : retrouver → lire trois pages → comparer → créer une note sourcée → regrouper → afficher côte à côte.

## 2. Formulaire complet avec contrôle des contraintes

Utiliser un formulaire de démonstration sans données personnelles réelles.

> Inspecte le formulaire de cet onglet avant de le remplir : champs obligatoires, formats, limites et options. Utilise ces données fictives : nom « Camille Démo », courriel « camille@example.com », ville « Cotonou », message « Je souhaite une démonstration de votre service ». Choisis les options correspondant à ces informations, laisse les champs non demandés inchangés et n’invente rien. Remplis les champs compatibles en une opération, inspecte à nouveau la validation et corrige les erreurs liées aux valeurs fournies. Ne soumets pas le formulaire. Crée une note « Contrôle du formulaire » indiquant ce qui est rempli, les champs restants et les contraintes rencontrées.

Enchaînement : découvrir → remplir → vérifier → corriger → documenter. Un champ non compatible ou une information manquante doit être signalé.

## 3. Comparaison de tableaux avec références réelles

Ouvrir deux ou trois pages présentant des tableaux HTML, par exemple des offres fictives ou des caractéristiques techniques.

> Lis les tableaux des trois onglets que je viens d’ouvrir. Compare uniquement les caractéristiques explicitement présentes, normalise les unités quand c’est possible et marque les données manquantes. Crée une note « Comparatif » avec un tableau commun, les liens des sources et deux choix correspondant à des besoins différents, sans inventer de prix ni de fonctionnalité. Regroupe ces ressources sous « Comparaison » et affiche les deux pages les plus utiles côte à côte.

Enchaînement : lire → extraire → normaliser → comparer → noter → organiser. Certaines pages utilisent des cartes ou des grilles propriétaires plutôt que des tableaux accessibles.

## 4. Diagnostic d’une application web

Utiliser sa propre application ou un site de test. Remplacer l’URL par son adresse réelle.

> Ouvre http://localhost:3000 et démarre la capture réseau et console. Recharge cette page pour reproduire son chargement. Examine les requêtes en échec et les erreurs JavaScript ; lis les détails des deux erreurs les plus pertinentes. Distingue les symptômes observés des causes probables, explique leurs liens éventuels et propose un correctif minimal avec du code. Crée une note « Diagnostic de démarrage » contenant les preuves, le correctif proposé et les étapes pour vérifier sa résolution. Ne prétends pas avoir modifié les fichiers du projet.

Enchaînement : ouvrir → capturer → reproduire → sélectionner les erreurs → lire leurs détails → expliquer → proposer du code → créer un rapport.

## 5. Tester la gestion d’une panne API et restaurer le réseau

Sur une application de démonstration dont le chargement déclenche une requête Fetch/XHR ; éviter une page contenant un formulaire non enregistré.

> Démarre la capture et recharge cette application. Identifie l’appel Fetch/XHR qui charge les données principales et lis ses détails. Simule ensuite une réponse HTTP 503 pour son URL exacte et sa méthode, puis recharge la page pour déclencher ce cas. Vérifie le message d’erreur réellement affiché. Remplace la simulation par un timeout, reproduis le chargement et vérifie la réaction de la page. Désactive ensuite toute simulation créée pendant ce test et recharge pour confirmer le retour au comportement réel. Crée une note « Résilience API » avec les résultats observés et les améliorations de code à proposer.

Enchaînement : observer → cibler une requête → simuler HTTP 503 → vérifier l’interface → simuler un timeout → vérifier → retirer la simulation → contrôler la restauration → noter.

Une seule règle URL/méthode peut être active par onglet. Ce scénario approche la limite de vingt tours d’outils : si elle est atteinte, demander « Continue le scénario en commençant par désactiver la simulation temporaire ».

## 6. Parcours de navigation et résultat différé

Sur un site de démonstration disposant d’un moteur de recherche interne.

> Examine la page et trouve son formulaire de recherche. Recherche « accessibilité », lance la recherche et attends un indicateur visible de résultat. Lis la page obtenue, collecte les liens correspondant au sujet et ouvre les deux ressources les plus pertinentes dans de nouveaux onglets. Lis-les et crée une note « Accessibilité — résultats » avec leurs points communs, leurs différences et leurs URL. Regroupe les deux ressources et la note sous « Accessibilité ». Si aucun résultat n’est trouvé, explique-le sans inventer de liens et ne répète pas l’envoi à l’aveugle.

Enchaînement : repérer → saisir → lancer → attendre → collecter les liens → ouvrir → lire → noter → regrouper.

## 7. Reprise d’un projet sans dupliquer les ressources

> Examine mes onglets ouverts et mes notes récentes pour retrouver ceux liés à [nom du projet]. Réutilise les ressources existantes plutôt que d’en ouvrir de nouvelles. Lis les deux pages les plus pertinentes et les deux notes les plus utiles. Crée une nouvelle note « Reprise — [nom du projet] » avec l’état des lieux, les décisions déjà présentes, les inconnues et cinq prochaines actions. Regroupe uniquement les onglets pertinents sous le nom du projet et affiche une ressource à côté de cette nouvelle note. Ne ferme aucun onglet.

Enchaînement : retrouver onglets et notes → lire → croiser → préparer une reprise → organiser → afficher côte à côte.

## 8. Copies de travail et vérification mobile

> Retrouve l’onglet de documentation [titre ou domaine] déjà ouvert et crée deux copies sans toucher à l’original. Liste les appareils de simulation disponibles, applique un téléphone à la première copie et une tablette à la seconde. Lis les deux pages et vérifie les contrôles accessibles, les éléments manquants et les différences constatables avec les outils. Crée une note « Vérification responsive » avec les résultats et les points à vérifier visuellement. Retire ensuite les simulations, épingle les deux copies et active l’original.

Enchaînement : retrouver → dupliquer → choisir des appareils → simuler → examiner → rapporter → restaurer → épingler. Les outils textuels ne constituent pas une inspection visuelle complète des pixels ou de toutes les règles CSS.

## Périmètre des démos

Ces scénarios n’impliquent ni paiement, ni téléversement, ni lecture de mot de passe ou de code OTP. Les correctifs de code sont proposés dans la discussion. Aucun scénario ne suppose une modification directe du projet local, un export PowerPoint ou une transcription audio. Préparer le site et ses données fictives avant un enregistrement public.
