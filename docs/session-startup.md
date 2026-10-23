# Erreur « Session indisponible » après redémarrage

Une extension disposant de `notifications` peut apparaître dans les permissions
natives du profil avec une origine `chrome-extension://…/`. Sol tentait de
réinitialiser cette permission à partir de son fichier de consentement des sites.
WebView2 refuse cette opération avec `0x80004005`, interrompant la préparation du
profil et empêchant les onglets, même neufs, de charger une page.

La synchronisation et l’effacement des permissions sont maintenant limités aux
origines HTTP/HTTPS. Les droits des extensions restent gérés par leur manifeste ;
les choix d’autorisation et de refus des sites restent appliqués normalement.
Aucun effacement de cookies, de stockage ou de permissions enregistrées n’est requis.

Les échecs d’initialisation écrivent `session-start-errors.log` dans le profil Sol,
avec l’étape, le type d’exception, son code et la pile technique, sans message brut,
adresse de site, cookie ni jeton. Une fenêtre de navigation privée n’écrit pas ce journal.

`node tools/check-session-startup.mjs <Sol.exe>` installe une extension factice dans
un profil jetable : démarrage, redémarrage, nouvel onglet, rechargement, autorisations
web accordées/refusées, révocation et effacement web conservant le droit de
notification de l’extension. Le rapport reste dans `bin/sidebar-check-*` ; les
profils de test sont nettoyés après fermeture. La version 1.0.30 non corrigée
reproduit le blocage au premier redémarrage avec cette même extension.

## Reprise après un échec de préparation

Une initialisation moteur échouée ou annulée n’est plus conservée indéfiniment :
l’ouverture suivante peut la retenter sans changer de profil. Les onglets qui
démarrent ensemble partagent toujours la même tentative.

Les étapes facultatives (ancien bloqueur, zoom, synchronisation des extensions,
nettoyage des anciens profils privés) journalisent leur échec sans empêcher la
navigation. La synchronisation des permissions des sites reste obligatoire ;
aucune autorisation révoquée n’est ignorée pour faire démarrer un onglet.

Les régressions C# couvrent la tentative simultanée, l’échec transitoire,
l’annulation et l’isolation des préparations facultatives.
