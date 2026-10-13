# beUI dans Sol

Intégration des composants beUI aux réglages, menus, téléchargements, notes et au rangement des onglets. Les commandes du pont C#, les profils et les données restent gérés par leurs services existants.

Les sources sont installées depuis le registre public officiel `https://beui.dev/r/registry.json`, par `shadcn add @beui/select @beui/switch @beui/checkbox @beui/button-base @beui/context-menu`.

- `button-base` : actions et choix compacts des réglages.
- `switch` : options générales et teinte des zones de l'interface.
- `select` : police et moteur de recherche, avec navigation au clavier dans `SolSelect`.
- `checkbox` : sélection des écrans du menu de comparaison, avec le nombre autorisé inchangé.
- `context-menu` : actions de clic droit et menus de la sidebar, avec flèches, Home/End et recherche par lettre.
- `notification-stack` : pile de téléchargements qui se déplie au survol, au focus ou au clic, avec pause, reprise, annulation et fichiers terminés. Les cartes restent des contrôles indépendants, sans boutons imbriqués. La hauteur est mesurée et bornée à la fenêtre native ; la liste dépliée défile.
- `tabs` : sections des paramètres, vues et modes de rédaction des notes, avec indicateur animé et navigation par flèches.
- `input`, `card`, `collapsible`, `shared-layout-bg` : recherche, titre, cartes, navigation et détails des notes.
- `overflow-actions` : actions complémentaires de la note, accessibles au clavier et sur une petite vue.
- `morphing-modal` : formulaires des tags, centrés dans la page, avec focus initial, confinement du focus, fermeture par Échap et restitution du focus.
- `command-palette` : recherche de commandes et d'onglets, reliée aux actions natives. Le rendu `embedded` utilise le cadre de Sol et laisse le backend ouvrir le panneau suivant.
- `sortable-stack` : ordre, transitions de position et rangement au clavier des onglets/groupes. Le geste de souris conserve les règles natives d'épinglage, d'entrée/sortie des groupes et de déplacement des paires. Ctrl+Maj+↑/↓ déplace l'élément ciblé ; le focus est conservé.

`beui.css` charge les utilitaires Tailwind sans Preflight et relie les tokens aux couleurs adaptatives de Sol. `beui-sol.css` conserve la densité, les pilules et les contrastes de Sol. Les animations respectent la préférence système de réduction du mouvement.

Les grands fichiers du registre sont répartis en modules de moins de 300 lignes. Les chemins publics et les exports des composants sont conservés.

Adaptation native du menu : le mode `embedded` rend le contenu dans le flux de la WebView2 déjà placée par `PopupHost`. La taille reste mesurée par le composant `Popup`, et les changements de taille du cadre ne ferment pas le menu. Le clic souris est transmis dès l'appui ; Entrée et Espace activent aussi l'action. Le backend conserve la responsabilité de fermer le menu ou d'ouvrir le formulaire suivant.

Les icônes animées des actions courantes proviennent du registre officiel lucide-animated. Leur contrôleur est déclenché depuis le bouton entier, au survol, au focus ou à la pression ; le mouvement réduit est lu à chaque activation et lors d'un changement système. Les favicons et les glyphes propres à Sol gardent leur rôle visuel.

Les licences MIT originales sont conservées dans `ui/licenses/beui-MIT.txt` et `ui/licenses/lucide-animated-MIT.txt`, puis ajoutées aux mentions livrées dans `ui/dist/licenses/third-party.txt`.

Vérification native : `tools/check-beui.mjs` contrôle les réglages persistés, le clavier, le focus, la superposition des listes, les thèmes, les trois tailles d'interface, le défilement en petite fenêtre, les transitions menu/formulaire et les limites de la comparaison. Les tests `check-notes.mjs` et `check-browser-controls.mjs` couvrent les notes, les suggestions et les téléchargements.

`check-beui-workspace.mjs` couvre le rangement au clavier, la palette, les modals, les actions de notes et les icônes. `check-sidebar.mjs` vérifie les gestes de souris, les groupes, les épingles et les paires sur les trois tailles d'interface. `check-sidebar-sort.mjs` contrôle les permutations envoyées au backend.

La barre de mise en forme de l'éditeur riche utilise le sprite SVG livré avec Vditor. Son installation depuis la ressource empaquetée évite le chargeur de script inline de la bibliothèque, bloqué par la CSP de Sol. Le test natif vérifie le dessin des icônes et l'action de mise en forme.

`check-download-stack.mjs` vérifie quatre téléchargements simultanés, leur affichage complet et l'accès au pied de la pile dans une petite fenêtre native.
