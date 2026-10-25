# Sol 1.0.32

Sol AI peut désormais préparer un espace de travail et agir sur les pages à partir d'une demande écrite ou dictée.

- Ouvrir des URL et organiser les onglets en groupes ou en vue côte à côte.
- Remplir un formulaire en une opération, choisir des options, modifier des cases à cocher, utiliser le clavier, le survol et le défilement.
- Naviguer en arrière ou en avant, recharger, fermer et rouvrir des onglets, régler le zoom.
- Préparer des ressources avec une note ouverte pour travailler.
- Dicter une demande et écouter la réponse grâce aux voix Windows, avec le composant Voice Orb officiel de beUI.
- Autoriser ou refuser une action à voix haute avec une reconnaissance restreinte à « oui » et « non ».

Les actions sur les sites et les fermetures d'onglets demandent un accord. Le remplissage ne soumet pas automatiquement le formulaire. Les brouillons et la navigation privée restent protégés.

La voix nécessite un moteur de reconnaissance, une voix et un microphone compatibles dans Windows. ChatGPT reçoit le texte reconnu ; aucune clé API audio supplémentaire n'est nécessaire. Les CAPTCHA et champs sensibles restent manuels. La vue scindée affiche deux pages.

Validation : 466 tests automatisés, builds de l'interface et de Sol, contrôles WebView2 des formulaires et du mode vocal, synthèse et reconnaissance françaises sur des flux audio en mémoire. L'essai complet avec un microphone et un compte ChatGPT réel reste à faire.

[Guide des actions et du mode vocal](sol-ai-browser-agent.md).
