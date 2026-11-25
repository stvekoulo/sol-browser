# Identité de Sol

Les quatre PNG d’origine sont conservés à l’identique.

- `logo-transparent.png` alimente le logo du nouvel onglet et l’image de l’installateur.
- `logo-original.png` fournit le visuel sur fond argenté à l’ouverture de la configuration.
- `wallpaper-original.png` et `wallpaper-dark-original.png` sont les fonds Sol clair et sombre du nouvel onglet.

`tools/sync-brand.ps1 -BrowserOnly` régénère les visuels du navigateur, les vignettes des réglages, les icônes multirésolution et l’image de l’installateur. `Sol.ico`, `SolFile.ico` et les ressources du paquet Microsoft Store utilisent le symbole agrandi sur le fond argenté arrondi de `logo-windows.png`. Les associations du Store déclarent l’ICO dédié et des PNG de repli.

Sans `-BrowserOnly`, le script synchronise aussi le logo des README et du film de lancement. `-LandingDirectory` permet de synchroniser les logos, les icônes et le fond du site dans son dépôt séparé.
