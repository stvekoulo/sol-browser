# Identité Sol — 23 octobre 2026

Les PNG fournis sont conservés sans retouche ni recompression : `logo-original.png`, `wallpaper-original.png` et le nouveau `logo-transparent.png`. Seul le dernier sert désormais aux logos et icônes distribués.

- Logo SHA256 : `AB58EB89F09F98932BC293F2049BC9403D66C47F8B800EB4945927E0E9E1FC2A`
- Fond SHA256 : `2DA9ECAB92E6EC009B1C974A927A7B7AD089F9A275FD9BE2E080A429385DF8C9`
- Logo transparent fourni SHA256 : `68BD3AF9F056B0A643567C229407ED021B631770E6DA5B73106303A58A8F41BF`

`tools/sync-brand.ps1` synchronise les copies du logo transparent dans l’interface, les README, les assets du film et la landing, y compris ses icônes Next.js. Le fond est copié dans Sol et la landing. Les dérivés techniques ICO multirésolution et splash Windows sont générés depuis le PNG transparent fourni, sans nouvelle génération d’image.

Le Welcome repose sur les sources beUI existantes : Button, Input, Tabs, Card, Switch et RadioGroup. Il conserve les cinq étapes, les commandes natives et la réouverture de l’import. Le choix des polices utilise des cartes radio, six choix initiaux et une extension aux quatorze polices, avec navigation clavier.

Les nouveaux profils activent le fond Sol par défaut. Les choix de fond déjà enregistrés, notamment les images personnelles, restent respectés. Les couleurs des sites et celles choisies par l’utilisateur pour ses groupes ne sont pas des éléments du branding et restent configurables.

Captures réelles : `bin/brand-review/`. Les vues du Welcome et de l’accueil proviennent du build Windows WebView2 avec un profil de test supprimé ensuite ; les captures de la landing proviennent de sa build Next.js locale. Aucun compte réel ni import de mots de passe n’a été utilisé pour les validations.

Vérifications : 490 tests Core, builds UI/Windows/landing, parcours natif complet et persistance des préférences, grille de polices et clavier, largeur Welcome 420 px et landing 390 px sans débordement, HTTP 200 des pages et icônes. Les originaux/copies PNG sont vérifiés par SHA256. ESLint landing ne signale aucune erreur, avec les avertissements de performance `<img>` existants.

La publication est demandée pour la version 1.1.1. Elle doit utiliser exclusivement le PNG transparent fourni par l’utilisateur ; les essais de détourage génératifs refusés ne sont pas intégrés aux sources. La landing conserve sa composition d’origine avec le nouveau fond en arrière-plan.
