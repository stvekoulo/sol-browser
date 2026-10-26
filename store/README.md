# Publication Microsoft Store

Produit réservé le 23 octobre 2026 : **Sol Navigateur**, ID Store `9PFVX21MMSBG`.

- Identity Name : `StevenKOULO.SolNavigateur`
- Publisher : `CN=7ACA79C7-6DB7-4E57-B74D-CA9D96852079`
- PublisherDisplayName : `Steven KOULO`
- PFN : `StevenKOULO.SolNavigateur_etqjp0m3zjaf2`
- Soumission brouillon : `1152921505702217009`
- [Partner Center](https://partner.microsoft.com/fr-fr/dashboard/products/9PFVX21MMSBG)

Les propriétés et les descriptions française et anglaise sont enregistrées : Productivité, données personnelles, texte de confidentialité, IA générative et assistance `contact@stevencrdkoulo.space`.

## Construction

Depuis PowerShell : `./tools/package-store.ps1`. Le script compile l’interface puis chaque architecture et utilise MakeAppx du SDK Windows installé, avec validation activée. Les sorties uniques et leurs journaux/empreintes sont placés dans `bin/store-<horodatage>/`. Pour x64 seulement : `./tools/package-store.ps1 -Arch x64`.

`-StoreVersion 1.1.2.0` permet une révision du paquet Store indépendante de la version de l’exécutable. Le dernier nombre reste zéro, conformément aux règles du Store. Le script renomme l’index WinUI `Sol.pri` en `resources.pri`, requis sous identité MSIX, puis exécute `tools/check-store-package.ps1` sur chaque paquet.

`tools/prepare-store-icons.ps1` fournit les variantes `targetsize` normales, `altform-unplated` et `altform-lightunplated` aux 14 tailles Windows. MakePRI fusionne leur index avec les ressources WinUI existantes. Sans ces variantes indexées, Windows peut ajouter une plaque bleue même si le PNG et le manifeste sont transparents. Le paquet final x64 1.1.2.0 a été installé et activé sans élévation ; le Welcome et l’icône sans plaque ont été vérifiés visuellement. Captures : `bin/store-certification-20261026/final-installed-welcome.png` et `taskbar-transparent.png`.

Les MSIX sont non signés, destinés à la soumission Partner Center. Le script ne les installe pas et ne modifie aucun certificat. Microsoft signe les paquets acceptés pour la distribution Store. Le build natif utilise `SolStoreBuild=true` : Velopack ne traite pas le démarrage, la recherche de mise à jour ne contacte pas GitLab et les paramètres indiquent la gestion des mises à jour par le Microsoft Store. Les associations HTTP/HTTPS et fichiers sont déclarées dans le manifeste ; Windows laisse le choix du navigateur par défaut à l’utilisateur.

## Vérifications du 23 octobre

- 490 tests Core réussis.
- Premier MSIX x64 validé par MakeAppx : `bin/store-20261023-200310/`.
- Premiers MSIX ARM64 et x86 validés par MakeAppx : `bin/store-20261023-200547/`.
- `tools/check-store-distribution.mjs <chemin-vers-Sol.exe>` : test natif WebView2 réussi sur le build x64 extrait, gestion Store et bouton de vérification masqué. Ce test utilise un profil éphémère et ne valide pas le lancement sous identité MSIX.
- Suppression des imports SF Pro et remplacement par Segoe UI, sans effacer les anciens fichiers sources. Les identifiants de préférences `sf` et `sf-bold` restent compatibles avec les profils existants. Les premiers MSIX précèdent cette correction : **ne pas les soumettre**.

## Échec de certification du 26 octobre et correction

La soumission 1.1.1 a été refusée pour crash au démarrage (10.1.2.10). Le journal Microsoft signale `Microsoft.UI.Xaml.dll`, exception `0xc000027b`, offset `0x3a7515`. Le même événement a été reproduit localement après installation et activation AUMID du paquet original x64. Une variante où seul `Sol.pri` est renommé en `resources.pri` ouvre la fenêtre `Sol — store-cert-20261026`. Les preuves sont conservées dans `bin/store-certification-20261026/` (ignoré par Git).

Le paquet de test, son profil et son certificat temporaire ont été retirés ; l’absence du certificat a été vérifiée dans les deux magasins utilisateur et dans `LocalMachine/TrustedPeople`. La validation MakeAppx seule ne couvre pas le lancement installé. Le lancement natif ARM64 et x86, les associations HTTP/HTTPS/fichiers et une machine sans WebView2 restent des validations distinctes. Toute installation locale nécessitant une modification de configuration ou de confiance doit être explicitement autorisée. Les accords juridiques éventuels du portail doivent être présentés au titulaire du compte avant acceptation.

Les trois paquets `1.1.2.0` ont remplacé les paquets refusés dans Partner Center le 26 octobre 2026. La soumission a été renvoyée ; le portail affiche **En cours de certification**, avec prétraitement actif. Cela ne constitue pas une certification réussie. Preuve : `bin/store-certification-20261026/store-resubmitted.png`. Les notes Microsoft détaillent la cause, la correction et les limites des tests matériels.

Références : [MSIX dans le Store](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/package-version-numbering), [extensions des applications empaquetées](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions), [licence des polices Apple](https://developer.apple.com/fonts/).
