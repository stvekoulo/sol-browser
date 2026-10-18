# Revue de l’intégration Sol AI — 18 octobre 2026

Le panneau de discussion est conservé, à la demande explicite de l’utilisateur.
La base ajoutée par Claude comprend un compte ChatGPT, les actions contextuelles sur la page/sélection/champ, une discussion latérale et le classement facultatif des favoris et de l’historique. Le moteur local précédent a été remplacé par cette intégration distante : cette version exige une connexion réseau et un compte éligible.

## Corrections effectuées

- Identifiant d’hôte OAuth stable au format `urn:uuid:` ; migration des anciens identifiants opaques.
- Conservation de l’inscription du compte chiffrée par Windows, réutilisation du client délivré, des indices d’identité appropriés et validation du client/compte au retour.
- Un jeton renouvelé n’est rendu utilisable qu’après vérification de son identité. La rotation est conservée sans promouvoir un accès non vérifié. Les permissions sont relues avant l’inférence.
- La déconnexion annule une authentification en cours, les réponses et le classement. Les erreurs d’effacement/révocation sont présentées ; la révocation réseau a un délai maximal.
- Les onglets privés suivent les mêmes restrictions IA que les anciennes fenêtres privées : aucun contexte privé envoyé, panneau normal masqué, requêtes en cours annulées et commandes vérifiées côté natif.
- Le résumé de page exclut les champs et les brouillons `contenteditable`, en plus des champs de mot de passe. Les modifications d’un champ restent explicitement déclenchées par l’utilisateur.
- Panneau latéral adaptatif : discussion sur la page à faible largeur, boutons qui reviennent à la ligne, liens longs et blocs de code contenus. Les paramètres du compte et leur adresse restent lisibles.
- Erreurs et état de connexion visibles dans la discussion ; lien de gestion de l’usage accessible.
- Regroupement des fragments toutes les 50 ms pour limiter les envois et rendus ; historique borné en volume, génération bornée et délais réseau définis.
- Chargement différé des vues IA ; le bundle principal mesuré passe d’environ 788 à 716 ko minifiés (hors gzip).
- Classement annulable et verrou de fichier pour éviter des classements simultanés dans plusieurs fenêtres du profil.
- Confirmation de la première connexion et stockage des inscriptions séparé des jetons actifs. `store:false` n’est plus décrit comme une garantie d’absence de conservation chez OpenAI.
- Composants UI et styles concernés répartis en modules de moins de 301 lignes.

## Vérifications

- `npm run build` dans `ui` : réussi.
- `dotnet build src/Browser.App -c Release --no-restore` avec compilation limitée en mémoire : réussi ; quatre avertissements de nullabilité préexistants.
- `dotnet test tests/Browser.Core.Tests -c Release --no-restore` : **347 réussites**, zéro échec.
- Les tests utilisent le vrai client de compte et le stockage Windows avec un transport HTTP simulé : permission retirée, identité renouvelée invalide, rotation et terminaison normale/incomplète du flux. Aucun appel à OpenAI.
- `node tools/check-ai-review.mjs` : réussi sur le binaire WinUI/WebView2. Extraction sans brouillons, compte fictif, Markdown, largeurs de fenêtre 1200/640/420, thème sombre, taille d’interface et isolation privée.
- Captures et mesures : `bin/sidebar-check-1792317670704/` ; profils, caches, cookies et données du test supprimés après fermeture des processus. Les captures et rapports sont conservés.

## Limites explicites

La connexion à un vrai compte ChatGPT, son éligibilité/quota et l’inférence réelle ne sont pas validés par ces essais. Ils nécessitent que l’utilisateur complète lui-même la connexion dans le navigateur système puis envoie une question. Aucune promesse de disponibilité du service ni de fonctionnement hors connexion.

Le sélecteur de plusieurs comptes et le choix manuel d’un modèle ne sont pas ajoutés ici : Sol utilise le compte du profil et le premier modèle éligible du catalogue renvoyé pour ce compte.
