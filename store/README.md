# Publication Microsoft Store

Produit réservé le 23 octobre 2026 : **Sol Navigateur**, ID Store `9PFVX21MMSBG`.

- Identity Name : `StevenKOULO.SolNavigateur`
- Publisher : `CN=7ACA79C7-6DB7-4E57-B74D-CA9D96852079`
- PublisherDisplayName : `Steven KOULO`
- PFN : `StevenKOULO.SolNavigateur_etqjp0m3zjaf2`
- Soumission brouillon : `1152921505702217009`
- [Partner Center](https://partner.microsoft.com/fr-fr/dashboard/products/9PFVX21MMSBG)

Les propriétés du brouillon sont enregistrées : Productivité, données personnelles, texte de confidentialité, IA générative et assistance `contact@stevencrdkoulo.space`. La description française est préparée dans `listing-fr.md` mais n’a pas été saisie dans le portail.

## Construction

Depuis PowerShell : `./tools/package-store.ps1`. Le script compile l’interface puis chaque architecture et utilise MakeAppx du SDK Windows installé, avec validation activée. Les sorties uniques et leurs journaux/empreintes sont placés dans `bin/store-<horodatage>/`. Pour x64 seulement : `./tools/package-store.ps1 -Arch x64`.

Les MSIX sont non signés, destinés à la soumission Partner Center. Le script ne les installe pas et ne modifie aucun certificat. Microsoft signe les paquets acceptés pour la distribution Store. Le build natif utilise `SolStoreBuild=true` : Velopack ne traite pas le démarrage, la recherche de mise à jour ne contacte pas GitLab et les paramètres indiquent la gestion des mises à jour par le Microsoft Store. Les associations HTTP/HTTPS et fichiers sont déclarées dans le manifeste ; Windows laisse le choix du navigateur par défaut à l’utilisateur.

## Vérifications du 23 octobre

- 490 tests Core réussis.
- Premier MSIX x64 validé par MakeAppx : `bin/store-20261023-200310/`.
- Premiers MSIX ARM64 et x86 validés par MakeAppx : `bin/store-20261023-200547/`.
- `tools/check-store-distribution.mjs <chemin-vers-Sol.exe>` : test natif WebView2 réussi sur le build x64 extrait, gestion Store et bouton de vérification masqué. Ce test utilise un profil éphémère et ne valide pas le lancement sous identité MSIX.
- Suppression des imports SF Pro et remplacement par Segoe UI, sans effacer les anciens fichiers sources. Les identifiants de préférences `sf` et `sf-bold` restent compatibles avec les profils existants. Les premiers MSIX précèdent cette correction : **ne pas les soumettre**.

## Suite attendue

Le nouveau logo et le fond sont intégrés aux sources, avec un Welcome monochrome beUI et des captures dans `bin/brand-review`. La validation visuelle reste attendue avant de construire et déposer les paquets et visuels définitifs. Aucun paquet n’a été téléversé, aucune certification demandée. Recompiler toutes les architectures après validation et vérifier l’absence de SF Pro dans les sorties.

Restent également : test réel du lancement/activation HTTP/HTTPS/fichiers en contexte MSIX et des données de profil, WebView2 sur machine propre, visuels de fiche, description, disponibilité/tarification et questionnaire IARC. Toute installation locale nécessitant une modification de configuration ou de confiance doit être explicitement autorisée. Les accords juridiques éventuels du portail doivent être présentés au titulaire du compte avant acceptation.

Références : [MSIX dans le Store](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/package-version-numbering), [extensions des applications empaquetées](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions), [licence des polices Apple](https://developer.apple.com/fonts/).
