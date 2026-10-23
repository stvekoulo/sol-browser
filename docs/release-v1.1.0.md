# Sol 1.1.0

Sol AI dispose de davantage d’outils pour enchaîner des actions sur les pages et aider au développement web.

- **Actions plus complètes** : inspection des formulaires et de leur validation, extraction de tableaux, collecte de liens réels, attente d’un résultat visible, recherche, duplication et épinglage d’onglets.
- **Accord permanent facultatif** : disponible dans les paramètres Sol AI et sur les cartes d’autorisation, conservé dans le profil au redémarrage et révocable dans les paramètres.
- **Aide aux développeurs** : boutons « Expliquer avec ChatGPT » dans la console et les détails réseau, lecture ciblée des diagnostics demandés, propositions de correctifs et étapes de vérification.
- **Outils de diagnostic** : capture réseau, pause, reprise, effacement et conservation ; simulations Fetch/XHR (statut HTTP, délai, timeout, réponse vide ou JSON invalide), ouverture des DevTools et simulation d’appareils.
- **Vocal temporairement masqué** : le microphone de Sol AI est désactivé ; le composant beUI Voice Orb est conservé pour un futur moteur vocal.

Les références périmées doivent être renouvelées avant une nouvelle action ; les résultats sont vérifiés sur la page. Les sorties volumineuses sont bornées, les erreurs signalées et les brouillons préservés.

Les diagnostics restent locaux tant qu’aucune analyse n’est demandée. Pour une analyse, les éléments utiles sont transmis à ChatGPT après accord ponctuel ou permanent, avec masquage des secrets connus. Des données privées peuvent toutefois subsister. Sol propose du code dans la discussion ; il ne modifie pas les fichiers d’un projet local.

Validation : **490 tests automatisés**, compilation de l’interface et de Sol Windows, tests WebView2 des formulaires, tableaux, liens, autorisations persistantes et révoquées, vocal masqué, erreurs console/HTTP, simulations et restauration réseau. L’inférence complète avec un compte ChatGPT réel et les parcours de services tiers tels que Gmail restent à valider.

[Guide des actions et diagnostics](https://gitlab.com/stevencrdkoulo/sol-browser/-/blob/v1.1.0/docs/sol-ai-browser-agent.md) · [Scénarios de démonstration](https://gitlab.com/stevencrdkoulo/sol-browser/-/blob/v1.1.0/docs/demo-sol-ai-v1.1.0.md)
