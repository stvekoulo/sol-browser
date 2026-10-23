# Aperçu de lien

Dans une page, **Alt + clic** sur un lien ouvre un aperçu navigable au-dessus
de l’onglet. L’action « Aperçu du lien » est aussi disponible dans l’éventail
et dans le menu du clic droit. Les clics ordinaires gardent leur comportement.

L’aperçu reprend la composition de la référence fournie : grande carte
arrondie, page assombrie derrière et commandes flottantes à droite.
Les boutons de 32 px utilisent des icônes de 18 px dessinées en traits, animées au
survol et au clavier. Sur une zone étroite, les commandes passent au-dessus
de la page. Les animations respectent la préférence système de mouvement réduit.

La page d’origine conserve sa position et ses formulaires. La navigation dans
l’aperçu n’ajoute pas d’onglet ; un lien demandant une autre fenêtre continue
dans cet aperçu. Échap, la croix ou un clic sur le fond le ferment. Changer
d’onglet masque seulement son aperçu : au retour, la page, sa saisie, son
défilement et son historique sont conservés sans rechargement. Chaque onglet
peut conserver son propre aperçu. Fermer ou faire naviguer l’onglet source
ferme son aperçu ; l’économiseur mémoire ne décharge pas un onglet qui en possède un.

Les boutons « Nouvel onglet » et « À côté » ouvrent l’adresse actuellement
visitée dans l’aperçu, dans la même session que l’onglet source. Cette ouverture
recharge l’adresse : une saisie non envoyée dans l’aperçu n’est pas transférée.

Il s’agit d’une WebView2 réelle partageant le profil du site source, sans iframe
ni proxy distant. Les protections du moteur restent actives : permissions
sensibles demandées et certificats invalides refusés. Seuls HTTP/HTTPS sont
acceptés. Pour télécharger un fichier, ouvrir l’aperçu dans un onglet.

## Vérifier

`node tools/check-link-preview.mjs` lance Sol hors écran avec un profil jetable.
Le banc vérifie la session, les en-têtes anti-iframe, la saisie, l’historique,
la promotion d’adresse en onglet/vue scindée, Alt+clic et Échap.
Ajouter `--visual` pour conserver une capture de la fenêtre native complète.
Les profils sont supprimés après arrêt ; captures et rapport restent dans `bin/`.
