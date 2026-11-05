# Conservation du profil au démarrage

## Défaut reproduit en 1.2.6

La restauration des onglets attendait la commande `ui.ready` de l’interface web.
Un rafraîchissement du contrôleur avant cette commande pouvait déjà démarrer
le minuteur d’enregistrement. Après deux secondes, il écrivait une session vide.
La restauration relisait alors ce fichier vide et ouvrait simplement l’accueil.
Fermer la fenêtre avant `ui.ready` pouvait aussi enregistrer cet état vide.

Les lectures JSON ignoraient également les erreurs d’accès : un fichier verrouillé
pouvait donner des valeurs par défaut ou un fichier de secours plus ancien,
susceptibles d’être réenregistrés ensuite. Ce mécanisme concernait aussi les raccourcis.
Le déclencheur exact de l’incident personnel ne peut pas être établi sans journal
de l’exécution concernée ; les deux mécanismes ont été vérifiés séparément.

## Correction en 1.2.7

Les réglages et la session sont lus une seule fois avant de construire l’interface.
Ces lectures demandent `requireReadable: true` : une erreur d’accès bloque
l’ouverture et conserve les fichiers, même si un ancien secours est disponible.
Un JSON invalide peut être repris depuis un secours valide. Si aucune copie
n’est exploitable, Sol conserve les originaux et affiche une erreur explicite.
Un profil réellement neuf conserve les réglages par défaut habituels.

L’enregistrement et son minuteur restent désactivés jusqu’au retour réussi de
la restauration. Ils sont aussi désactivés pendant la restauration d’un espace.
La navigation privée conserve son exclusion habituelle des fichiers de session.

## Vérification

- `ProfileReadSafetyTests` : accès verrouillé avec et sans secours, corruption
  irrécupérable, ancienne quarantaine, nouveau profil et secours valide.
- `check-session-persistence.mjs` : retarde `ui.ready` dans une compilation de
  test dédiée, provoque un rafraîchissement, vérifie que les octets de la session
  restent intacts, puis contrôle onglets, groupes, épingles et raccourcis.
  Ferme aussi Sol avant `ui.ready` et contrôle le redémarrage suivant.
- `check-restored-pins.mjs` : vérifie sur deux démarrages les épingles, les
  raccourcis, leur chargement au clic et le maintien en veille des autres pages.

Les tests natifs utilisent exclusivement des profils jetables hors écran.
La préparation d’une récupération personnelle doit conserver les onglets
actuels et une copie de sécurité avant toute application au profil.
