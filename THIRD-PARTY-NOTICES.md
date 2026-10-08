# Logiciels tiers livrés avec le navigateur

## Rendu Markdown et formules

React Markdown, remark-gfm/math, rehype-raw/sanitize/highlight/katex, highlight.js et KaTeX fournissent le rendu CommonMark/GFM, le nettoyage du HTML, la coloration du code et les formules. Le parseur est chargé à la demande. Les licences des dépendances de l’interface sont collectées après chaque build et livrées dans `ui/licenses/third-party.txt`.

Sources : <https://github.com/remarkjs/react-markdown>, <https://github.com/remarkjs/remark-gfm>, <https://github.com/remarkjs/remark-math>, <https://github.com/rehypejs/rehype-sanitize>, <https://github.com/rehypejs/rehype-highlight>, <https://github.com/KaTeX/KaTeX>.

## Sol AI — Microsoft Foundry Local

- Module auxiliaire facultatif, livré sur x64 et ARM64 ; aucun modèle inclus dans l’installateur.
- SDK : Microsoft.AI.Foundry.Local 2.1.0 et son runtime natif, licence MIT.
- Sources et conditions : <https://github.com/microsoft/Foundry-Local>.
- ONNX Runtime / ONNX Runtime GenAI : licence MIT, <https://github.com/microsoft/onnxruntime> et <https://github.com/microsoft/onnxruntime-genai>.
- Les textes de licence des dépendances du module sont livrés dans `ai/licenses`.
- Chaque modèle téléchargé a sa propre licence, affichée avant son installation. Il n’est pas redistribué avec Sol.

## uBlock Origin Lite

- Rôle : blocage des publicités et des traqueurs, installé par défaut.
- Auteur : Raymond Hill et les contributeurs d'uBlock Origin.
- Licence : GNU General Public License, version 3 (GPLv3). Le texte complet est livré avec l'extension
  (`extensions/ubol/LICENSE.txt`).
- Code source : <https://github.com/uBlockOrigin/uBOL-home> (paquets) et <https://github.com/gorhill/uBlock> (sources).
- Livré tel quel, sans modification. Le paquet est récupéré par `tools/get-ublock.ps1` depuis les publications
  officielles ; le navigateur le met ensuite à jour depuis ce même dépôt.

Le navigateur et l'extension sont deux programmes distincts : l'extension s'exécute dans le moteur web, par l'interface
d'extensions de celui-ci.
