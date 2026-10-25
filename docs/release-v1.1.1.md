# Sol 1.1.1

Sol adopte une nouvelle identité monochrome, avec un nouveau logo et un fond d’accueil fourni par son créateur.

- Nouveau logo pour l’application Windows, l’installateur, l’interface et la documentation.
- Configuration de première ouverture entièrement repensée avec les composants beUI : cinq étapes, import des données, apparence, préférences et confirmation.
- Sélection des polices sous forme de cartes avec aperçu, sélection visible, accès aux quatorze choix et navigation au clavier.
- Nouveau fond pour l’accueil, activé par défaut pour les nouveaux profils. Les préférences déjà enregistrées restent respectées.
- Remplacement des polices Apple embarquées par Segoe UI, avec conservation des identifiants des anciennes préférences.
- Préparation d’une édition Microsoft Store avec identité MSIX et mises à jour gérées par Microsoft. Cette préparation ne signifie pas que Sol est déjà disponible dans le Store.

Les fonctions Sol AI de la version 1.1.0 sont conservées : actions enchaînées, autorisation durable facultative et diagnostics de développement demandés. Le mode vocal reste masqué.

Validation : 490 tests Core, compilation Windows et de l’interface, parcours de configuration natif WebView2 avec persistance des choix, changement des polices au clavier, réouverture de l’import et vérification à 420 px. Les imports de données réelles et le lancement sous identité MSIX ne sont pas couverts par ces tests.
