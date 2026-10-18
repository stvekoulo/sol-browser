# Sol AI avec Sign in with ChatGPT — faisabilité

Vérification documentaire : 18 octobre 2026. Cette étude n’implémente aucun OAuth ni appel d’inférence et ne valide pas les travaux d’intégration menés séparément.

## Conclusion

Le parcours officiel couvre les applications open source et exécutées localement. Sol possède une licence MIT et une architecture native Windows/React compatible avec un client HTTP OAuth. Cette compatibilité est une conclusion d’architecture ; l’éligibilité réelle se vérifie par une connexion et une requête terminée sur le compte utilisateur.

Les utilisateurs ChatGPT Plus et Pro éligibles peuvent autoriser les requêtes prises en charge avec leur abonnement, sans clé API. L’autorisation d’identité et celle d’utiliser le forfait sont distinctes. Une application payante ou hébergée à distance relève du parcours d’intérêt/approbation décrit par OpenAI. [Présentation officielle](https://developers.openai.com/siwc/token-sharing-open-source), [tutoriel OpenAI du 28 septembre 2026](https://developers.openai.com/cookbook/articles/sign-in-with-chatgpt).

## Adaptation à Sol

- Réutiliser `BrowserAi.cs`, l’extraction Readability, le texte sélectionné et les actions contextuelles existantes : résumer, expliquer, questions sur la page et traduire une sélection.
- Sol AI n’est disponible qu’une fois le compte connecté avec l’autorisation `chatgpt.tokens.use.direct` ; le moteur local a été retiré.
- Ajouter le fournisseur distant dans la couche C# ; conserver les jetons hors de React, des pages web et des extensions.
- Aucun modèle n’est téléchargé ni chargé sur l’ordinateur ; Internet est nécessaire. Le délai de réponse reste à mesurer.
- Préserver les pilules, menus, thèmes et outils Sol prévus dans `roadmap-sol.md`. Cette étude ne modifie pas la direction produit.

## Parcours minimal (implémenté dans `ChatGptAccount.cs` et `ChatGptOAuth.cs`)

1. Préparer un identifiant d’hôte stable et ouvrir le parcours OAuth dans le navigateur système, avec callback limité à `127.0.0.1`, `state`, `nonce` et PKCE.
2. Effectuer le premier enregistrement avec `dynamic_agent_client`, puis conserver le `client_id` délivré pour le compte et l’espace concernés. Utiliser le nom réel de Sol et réutiliser l’identifiant d’hôte.
3. Échanger le code ; valider la signature, l’émetteur, l’audience, la durée de validité et le nonce de l’ID token. Vérifier la permission `chatgpt.tokens.use.direct` avant d’activer l’inférence.
4. Protéger localement les identifiants avec les mécanismes Windows et les isoler par compte. Gérer renouvellement, déconnexion et révocation ; supprimer les jetons locaux à la déconnexion.
5. Découvrir les modèles accessibles au compte avec `GET https://api.openai.com/v1/models`. Utiliser le modèle choisi parmi cette liste.
6. Envoyer le contexte nécessaire à `POST https://api.openai.com/v1/responses`, avec `store: false`, `stream: true` et un tableau `input`. Gérer l’annulation et les erreurs jusqu’à `response.completed`, y compris une limite de quota signalée après le début du flux.
7. Présenter la confirmation initiale d’utilisation du forfait, l’indication ChatGPT à proximité de l’action et un lien de gestion de l’usage, suivant les règles d’interface officielles.

Références : [connexion et enregistrement](https://developers.openai.com/siwc/token-sharing-open-source/sign-in), [comptes et sécurité](https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions), [modèles et inférence](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference), [règles d’interface](https://developers.openai.com/siwc/ui-ux-guidelines).

## Limites et données

- L’accès ne donne pas les anciennes conversations ChatGPT. Les quotas et les modèles dépendent du compte et des règles de son espace. Pour Plus, la fenêtre de cinq heures est partagée entre les applications utilisant le forfait ; Pro ne suit pas cette même fenêtre. [Comptes et usage](https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions).
- Dans cette préversion, Computer Use natif, génération d’images, Code Interpreter, recherche de fichiers et MCP hébergé sont exclus de ce parcours. Les fonctions locales personnalisées restent possibles selon le format documenté. La recherche web dépend du modèle et du compte. [Limites de la préversion](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations).
- Les champs de l’API classique ne sont pas tous acceptés : notamment `temperature`, `max_output_tokens` et `previous_response_id` sur HTTP. Envoyer explicitement l’historique nécessaire dans `input` si une fonction de questions successives est retenue. [Limites de la préversion](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations).
- Seule la page ou la sélection affichée dans la vue Sol AI est transmise, après un clic. Le contexte de page reste une donnée non fiable. L’ancienne adaptation à l’historique et aux favoris a été retirée.
- En navigation privée, Sol AI est indisponible.
- Les actions sur les sites demandent des outils Sol et une confirmation adaptée aux effets réels. Commencer par les actions de lecture du contenu.

## Validation restante

Connexion/consentement réels, catalogue propre au compte, première réponse complète, annulation, quota épuisé, renouvellement concurrent, révocation, isolation des profils privés et mesures sur petit PC. Aucun jeton utilisateur n’a été demandé ou lu pour cette étude.
