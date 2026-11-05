# Sol 1.2.7

- Conservation des onglets, groupes et épingles pendant un démarrage lent et lors d’une fermeture avant la fin du chargement de l’interface.
- Lecture unique des onglets et réglages avant l’ouverture de l’interface ; les sauvegardes de session attendent la fin de la restauration.
- Un profil verrouillé ou illisible n’est plus remplacé silencieusement par des réglages par défaut ou une session vide. Sol conserve les fichiers et signale le problème au démarrage.
- Récupération depuis le fichier de secours lorsqu’il est valide ; les fichiers corrompus restent disponibles pour le diagnostic.
