# Agent navigateur et voix Windows

Sol AI utilise les outils du navigateur pour exécuter les demandes de l'utilisateur. La connexion ChatGPT existante fournit le raisonnement ; les opérations restent exécutées dans Sol.

## Actions prises en charge

- Lire une page, chercher un passage, consulter un lien, afficher un élément.
- Cliquer, remplir un champ ou un formulaire entier, choisir une option, modifier une case à cocher.
- Utiliser le clavier et le survol, faire défiler la page.
- Ouvrir des URL dans de nouveaux onglets, changer d'onglet, lire un autre onglet, regrouper les ressources.
- Afficher deux pages côte à côte, revenir à une seule page, naviguer en arrière/avant, recharger, fermer et rouvrir des onglets, régler le zoom.
- Chercher, lire et créer des notes ; préparer un groupe de ressources avec une note ouverte pour travailler.

Les actions sur les sites et les fermetures d'onglets affichent une demande d'accord. Le remplissage groupé ne soumet jamais le formulaire. L'agent doit relire la page pour vérifier le résultat d'une action. Les onglets existants et les brouillons de notes sont préservés lors de la préparation d'un espace.

## Mode vocal

Dans le panneau Sol AI, cliquer sur le micro, puis sur **Parler**. Après une pause, Windows termine la reconnaissance, libère le micro et transmet le texte à Sol AI. Les réponses sont lues avec une voix Windows de la langue de l'interface. Une phrase reconnue avec une confiance insuffisante reste disponible pour correction, sans être envoyée automatiquement.

Lorsqu'une action attend une autorisation, le bouton **Répondre oui ou non** utilise une grammaire restreinte. L'accord ne vaut que pour l'action affichée au début de cette écoute. Les boutons habituels restent utilisables.

Le bouton **Arrêter** coupe l'écoute ou la lecture. **Arrêter la tâche** annule la réponse et les actions encore en attente. Quitter le mode vocal, fermer la discussion, se déconnecter ou passer en navigation privée libère le service vocal. Il faut cliquer sur **Parler** pour une nouvelle écoute.

Le composant visuel est le `VoiceOrb` officiel de [beUI](https://beui.dev/components/agents/voice-orb), installé depuis son registre, sans modification de ses sources. Son activité suit le niveau du microphone ou les événements de lecture Windows. Il respecte la préférence de réduction des animations.

L'implémentation de bureau utilise `System.Speech` (SAPI), compatible avec Sol non empaqueté. Elle nécessite un moteur de reconnaissance et une voix de la langue choisie, ainsi qu'un microphone autorisé par Windows. Un lien vers les paramètres vocaux est proposé si ces éléments manquent. Aucun fichier audio n'est enregistré ; OpenAI reçoit le texte, pas le son.

La connexion [Sign in with ChatGPT exclut actuellement l'entrée audio et la transcription](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations). Le mode Windows ne nécessite pas de clé API audio OpenAI.

## Limites

- Deux pages simultanées en vue scindée ; les autres restent dans le groupe.
- Aucun accès aux onglets privés.
- Les mots de passe, paiements, codes OTP, fichiers à téléverser et CAPTCHA doivent être traités par l'utilisateur.
- Les iframes d'un autre domaine et les shadow roots fermés ne sont pas lisibles par ces outils.
- Certains contrôles propriétaires ou sites peuvent refuser une action. Le modèle doit signaler l'échec et ne pas annoncer une réussite sans preuve.
- Le moteur de reconnaissance Windows dépend de la langue installée, du microphone et de la qualité du signal. La fluidité reste à évaluer avec la voix réelle de l'utilisateur.

## Vérification

- `dotnet test tests/Browser.Core.Tests/Browser.Core.Tests.csproj -c Release`
- `node tools/check-agent-actions.mjs` : formulaire réel dans WebView2, événements, iframe, shadow DOM, clavier, survol, défilement et références périmées.
- `dotnet run --project tools/voice-check/VoiceCheck.csproj -c Release` : synthèse et reconnaissance françaises réelles sur des flux WAV en mémoire, annulation et rejet des callbacks périmés, sans microphone ni réseau.
- `node tools/check-voice-ui.mjs` : entrée/sortie du mode vocal, orbe WebGL, thèmes clair/sombre, largeurs 220/380/520 px et isolation privée, avec un compte synthétique sans inférence.

Ces tests ne remplacent pas une session complète avec le microphone et un compte ChatGPT réel.
