# Outils développeur

## Comparer et synchroniser les maquettes

Dans le menu d’un onglet, choisir « Comparer plusieurs écrans », puis
« Synchroniser clics, saisie et défilement ». Les interactions sont répliquées
entre les maquettes de la même origine. Les champs mot de passe, fichier et
zones éditables riches ne sont jamais partagés. Désactiver cette option pour
tester les écrans indépendamment. Les clics peuvent déclencher les mêmes
requêtes de formulaire dans chaque maquette : utiliser un environnement de test.

« Basculer le thème des maquettes » simule `prefers-color-scheme` sur tous les
écrans. Les boutons de thème présents sur un site sont aussi cliquables en
synchronisation ; le résultat dépend de l’implémentation du site.

## Rejeu et réponses simulées

Dans le panneau Réseau, sélectionner une requête puis « Modifier et renvoyer ».
Modifier URL, méthode, en-têtes et corps, puis envoyer explicitement. Le rejeu
n’utilise pas automatiquement les cookies de la page et ne suit pas les
redirections. Les valeurs masquées doivent être remplacées manuellement.

Les préréglages 401 et 500 préparent un statut et un corps JSON ; ils ne lancent
aucune requête. « Activer la simulation » intercepte les Fetch/XHR de cette URL
et de cette méthode, uniquement dans l’onglet courant. Désactiver la règle ou
fermer le panneau la retire. Les service workers peuvent servir une réponse
avant l’interception. Aucun backend n’est modifié par une simulation.

## Aperçu localhost par QR

Sur une adresse localhost, cliquer sur l’icône QR dans la barre d’adresse.
Choisir l’interface réseau, scanner le QR ou copier le lien. Le QR est généré
localement et conserve le port, le chemin, la query et le fragment.
Le PC et le téléphone doivent partager un réseau ; le serveur doit écouter sur
une interface accessible (par exemple 0.0.0.0) et son port être autorisé par le
pare-feu. Sol ne démarre aucun proxy et ne modifie pas le pare-feu.

## Espaces de travail

Ouvrir « Espaces de travail enregistrés » dans la palette de commandes, ou
« Espaces de travail… » dans le menu des appareils. Nommer puis enregistrer la
disposition : onglets non privés, groupes, maquettes/orientations, appareils
comparés, paire scindée et ratio sont conservés dans le profil courant.
Restaurer ajoute les onglets à la fenêtre sans fermer le travail existant.
Les onglets inactifs restent endormis. La suppression demande une confirmation
dans le panneau. Les cookies, mots de passe et corps de formulaire ne sont
pas inclus dans un espace enregistré.

## Vérification

Après build UI et Release x64 : `node tools/check-developer-workspace.mjs`.
Le banc lance Sol hors écran dans un profil jetable, vérifie QR, saisie,
exclusion du mot de passe, clics, défilement, thème et sauvegarde/restauration.
`node tools/check-navigation.mjs <Sol.exe> --network-lot` couvre les requêtes,
simulations, délais, CORS et isolement. Rapports/captures restent dans `bin/` ;
profils et données de test sont supprimés après fermeture.
