# Logiciels tiers livrés avec le navigateur

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
