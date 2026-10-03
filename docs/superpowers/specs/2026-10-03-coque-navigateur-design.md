# Coque du navigateur — design (chantier 1)

Date : 2026-10-03
Nom de travail : `projet-navigateur`

## Contexte

Navigateur Windows destiné à être distribué. Moteur : Blink via WebView2 (runtime
Edge déjà présent sur Windows 11), choisi parce que la machine de développement a
moins de 5 Go libres et qu'aucun moteur ne peut y être compilé. Priorités produit :
design à la Apple, outils de développement, légèreté. L'IA est hors périmètre.

Le projet est découpé en cinq chantiers, chacun avec sa spec et son plan :

1. **Coque** (ce document)
2. Légèreté : mise en veille des onglets, démarrage rapide
3. Outils dev : multi-viewports, « Voir en mobile », confort localhost, réseau/API, profils isolés
4. Design final (repris par l'auteur du projet)
5. Distribution : installateur, mises à jour, signature

## Objectif du chantier 1

Un navigateur utilisable au quotidien, sobre, sur lequel les chantiers suivants se
branchent sans réécriture.

## Architecture

Une fenêtre, trois couches.

**Hôte (C# / .NET)** — fenêtre sans bordure Windows. Crée, dimensionne et empile les
vues web. Détient toute la logique et tout l'état : onglets, disposition, historique,
favoris, téléchargements, session, réglages. Ne dessine aucune interface.

**Vue d'interface (WebView2 + React/Vite)** — charge uniquement des fichiers locaux
du projet. Dessine la barre de titre (trois boutons ronds : fermer, réduire,
agrandir), la barre d'onglets, la barre d'adresse, les menus et les panneaux
(historique, favoris, téléchargements). Sans état propre durable : elle affiche
l'état que l'hôte lui envoie.

**Vues de pages (une WebView2 par onglet)** — les sites visités.

### Canal interface ↔ hôte

Messages JSON par `postMessage` WebView2, uniquement sur la vue d'interface.

- Interface → hôte : des commandes (`tab.open`, `tab.close`, `tab.move`,
  `nav.go`, `nav.back`, `nav.forward`, `nav.reload`, `split.open`, `split.close`,
  `split.resize`, `bookmark.add`, `download.cancel`, `window.minimize`, etc.).
- Hôte → interface : l'état complet à l'initialisation, puis des événements
  (`tab.updated` avec titre, URL, favicon, progression ; `download.updated` ; etc.).

L'hôte valide chaque commande reçue (type connu, champs attendus, identifiants
existants) et ignore le reste.

### Séparation de sécurité

- La vue d'interface ne navigue jamais vers une URL distante ; toute tentative est
  annulée par l'hôte.
- Les vues de pages n'ont aucun objet hôte exposé et leurs messages ne sont pas
  écoutés.
- Le script injecté dans les pages (flou, menu contextuel) ne reçoit aucune donnée
  de l'hôte autre que ses réglages d'affichage.

### Disposition des pages

L'hôte ne suppose pas « un onglet = une vue plein cadre ». La zone de contenu
affiche une **disposition** : une page seule, ou deux pages côte à côte avec un
séparateur déplaçable. Chaque côté a sa propre navigation ; la barre d'adresse agit
sur le côté actif. Limité à deux pages dans ce chantier.

## Fonctionnalités

- Onglets : ouvrir, fermer, réordonner par glisser, rouvrir le dernier fermé.
- Navigation : barre d'adresse (URL ou recherche), précédent, suivant, recharger,
  arrêter, indicateur de chargement.
- Vue scindée : deux pages côte à côte.
- Historique : enregistrement, panneau de consultation, recherche, suggestions dans
  la barre d'adresse.
- Favoris : ajouter, retirer, panneau de consultation.
- Téléchargements : progression, annulation, ouverture du fichier ou du dossier.
- Session : restauration des onglets et de la disposition au redémarrage.
- Raccourcis clavier standards : Ctrl+T, Ctrl+W, Ctrl+Shift+T, Ctrl+L, Ctrl+R,
  Ctrl+Tab, Alt+Gauche/Droite, F12 (DevTools Chromium).
- Menu contextuel maison sur les pages (retour, recharger, copier, ouvrir le lien
  dans un nouvel onglet ou en vue scindée, inspecter), extensible pour « Voir en
  mobile » au chantier 3.
- Flou progressif en haut et en bas des pages : bandes fixes injectées dans chaque
  page par l'hôte. Désactivable dans les réglages, coupé sur les onglets non
  visibles. Non appliqué aux pages qui refusent l'injection (lecteur PDF, pages
  internes).
- Thème : toutes les couleurs, rayons, espacements et polices dans un seul fichier
  de variables CSS. Police Inter et icônes libres ; aucune ressource sous licence
  Apple (San Francisco, SF Symbols).

## Stockage

Dossier de données utilisateur dans `%LOCALAPPDATA%`.

- **SQLite** : historique, favoris.
- **JSON** : réglages, session.
- Données des sites (cookies, cache) : dossier de données WebView2, un seul profil
  dans ce chantier.

## Gestion des erreurs

- Échec de navigation (DNS, hors ligne, certificat) : page d'erreur locale avec
  l'adresse et un bouton réessayer.
- Plantage du processus d'une page : l'onglet affiche un état « page plantée » avec
  rechargement ; les autres onglets ne sont pas touchés.
- Plantage de la vue d'interface : l'hôte la recrée et lui renvoie l'état complet.
- Session ou réglages illisibles : démarrage avec les valeurs par défaut, fichier
  fautif conservé sous un autre nom.
- Runtime WebView2 absent : message clair au lancement avec le lien d'installation.

## Risques à lever en premier

Deux prototypes jetables, avant tout autre code :

1. **Menus de l'interface par-dessus la page.** Superposer la vue d'interface, avec
   fond transparent, au-dessus d'une vue de page. Candidat : WinUI 3, où WebView2
   est composée et non une fenêtre fille. Si la superposition transparente ne
   fonctionne pas, repli : les menus s'ouvrent dans une petite vue web dédiée,
   opaque, positionnée au-dessus de la page.
2. **Flou progressif injecté.** Mesurer le coût au défilement sur la machine de
   développement, sur trois sites lourds. Si le défilement devient saccadé, repli :
   un flou simple à une seule couche avec dégradé d'opacité.

Le type de fenêtre de l'hôte est fixé par le résultat du prototype 1.

## Tests

- Logique de l'hôte (onglets, disposition, session, validation des messages,
  stockage) : tests unitaires .NET, sans WebView2.
- Interface React : tests des composants sur l'état reçu et les commandes émises.
- Parcours complets (ouvrir, naviguer, scinder, redémarrer et retrouver la session) :
  vérification manuelle par liste de contrôle à chaque fin d'étape.

## Hors périmètre

Mise en veille des onglets, outils dev maison, profils multiples, extensions,
synchronisation, gestionnaire de mots de passe, navigation privée, installateur,
mises à jour, signature, IA.
