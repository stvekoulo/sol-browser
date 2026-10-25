# Installation des extensions volumineuses

Sol ne fixe plus de plafond de taille pour les paquets du Chrome Web Store ni
pour leur contenu décompressé. Les anciens seuils de 32 Mio téléchargés et
128 Mio extraits ont été retirés.

Le paquet est téléchargé dans un fichier provisoire sur disque, puis vérifié et
extrait hors du fil de l’interface. Un tampon de 64 Kio suffit au transfert et
au calcul de signature ; le paquet entier n’est plus dupliqué en mémoire.
Le fichier CRX provisoire est supprimé après réussite comme après échec.

Le délai de 60 secondes concerne une connexion ou un transfert bloqué :
chaque bloc reçu renouvelle le délai, ce qui laisse un gros téléchargement
continuer tant qu’il progresse. L’espace disque disponible et les limites
propres au moteur ou au système restent applicables.

Les contrôles CRX3 (identité, signature), les chemins d’archive, les doublons
et la limite structurelle du nombre d’entrées restent vérifiés. L’utilisateur
confirme toujours les accès de l’extension avant son installation.

La régression `LargeExtensionPackageTests` vérifie un paquet signé de plus de
32 Mio contenant un fichier de 129 Mio, extrait depuis un flux fichier.
Les tests existants vérifient également le refus des signatures altérées et
des chemins qui sortent du dossier de destination.
