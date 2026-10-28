export const menuPaths={
  save:'M3 2h8l2 2v10H3zM5 2v4h5V2M5 14V9h6v5',
  group:'M2 2h9v9H2zM5 14h9V5M4 6.5h5M6.5 4v5',
  groupAdd:'M2 2h9v9H2zM5 14h9V5M4 6.5h5M7 4.5l2 2-2 2',
  ungroup:'M2 5V3h5M9 3h5v2M2 11v2h5M9 13h5v-2M5 8H1M11 8h4',
  tab:'M2 3h12v10H2zM2 6h12M6 9h4M8 7v4',
  edit:'M10.5 3.5l2 2-6.5 6.5-2.5.5.5-2.5z',
  trash:'M3 4h10M6 2h4v2M4 4l.5 10h7L12 4M6.5 7v4M9.5 7v4',
  note:'M4 2h8v12H4zM6 5h4M6 8h4',
  session:'M3 4h10v8H3zM5 2v2m6-2v2M3 7h10',
  clear:'M3 9l6-6 4 4-6 6H3zM7 13h7M5 7l4 4',
  print:'M4 5V2h8v3M4 11H2V6h12v5h-2M4 9h8v5H4zM11 7h1',
  cut:'M4 3l8 10M12 3L7 9M5 12a2 2 0 1 1-4 0 2 2 0 0 1 4 0M15 12a2 2 0 1 1-4 0 2 2 0 0 1 4 0',
  select:'M2 5V2h3M11 2h3v3M14 11v3h-3M5 14H2v-3M5 5h6v6H5z',
  undo:'M6 4L2 7l4 3M2 7h7a4 4 0 0 1 4 4',
  redo:'M10 4l4 3-4 3M14 7H7a4 4 0 0 0-4 4',
  sleep:'M10 2a6 6 0 1 0 4 9 6 6 0 0 1-4-9z',
  expand:'M3 5l5-3 5 3M3 11l5 3 5-3',
  collapse:'M3 2l5 3 5-3M3 14l5-3 5 3',
  extension:'M3 3h4a2 2 0 1 1 2 0h4v4a2 2 0 1 1 0 2v4H9a2 2 0 1 0-2 0H3V9a2 2 0 1 0 0-2z',
  tools:'M2 3h12v10H2zM4 6l2 2-2 2M8 10h3',
  spell:'M2 11l3-8 3 8M3 8h4M9 11l2 2 3-4',
  preview:'M1.5 8S4 3.5 8 3.5 14.5 8 14.5 8 12 12.5 8 12.5 1.5 8 1.5 8zM8 10a2 2 0 1 0 0-4 2 2 0 0 0 0 4',
  ask:'M2.5 3.5h11v7.5H7.5l-3 2.5V11h-2zM6.6 6.1a1.4 1.4 0 1 1 1.9 1.3c-.3.1-.5.4-.5.8M8 9.4h.01',
  sync:'M2.5 7a5.5 5.5 0 0 1 9.6-2.6M12.5 2v3h-3M13.5 9a5.5 5.5 0 0 1-9.6 2.6M3.5 14v-3h3',
  theme:'M8 2.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11M8 2.5v11M8 5h3.5M8 8h5.5M8 11h3.5',
  workspace:'M2.5 2.5h5v6h-5zM9.5 2.5h4v3h-4zM9.5 7.5h4v6h-4zM2.5 10.5h5v3h-5z',
  search:'M7 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8M10 10l3 3',
};
const normalized=(label:string)=>label.normalize('NFD').replace(/\p{M}/gu,'').toLowerCase().trim();
export function menuIconPath(label: string): string {
  const l = normalized(label).split(/[«“"]/)[0];
  const has = (...words: string[]) => words.some(w => new RegExp(`(?:^|[^a-z0-9])${w}(?:$|[^a-z0-9])`).test(l));
  if(has('enregistrer','sauvegarder','save as','save link','save image','save page'))return menuPaths.save;
  if(has('telecharger','telechargements','download','downloads','export'))return 'M8 2.5v7.5M5 7l3 3 3-3M3.5 13h9';
  if(has('nouveau groupe','new group','new tab group'))return menuPaths.group;
  if(has('ajouter au groupe','add to group'))return menuPaths.groupAdd;
  if(has('retirer du groupe','remove from group','dissocier','ungroup'))return menuPaths.ungroup;
  if(has('rouvrir','reouvrir','reopen','historique','history'))return 'M2 7a6 6 0 1 1 1.5 5M2 3v4h4M8 4.5V8l2.5 1.5';
  if(has('replier','collapse'))return menuPaths.collapse;
  if(has('deplier','expand'))return menuPaths.expand;
  if(has('veille','sleep'))return menuPaths.sleep;
  if(has('modifier','renommer','edit','rename'))return menuPaths.edit;
  if(has('vider','effacer','clear'))return menuPaths.clear;
  if(has('supprimer','retirer','delete','remove'))return menuPaths.trash;
  if (has('epingler', 'detacher', 'pin','unpin')) return 'M5.5 2.5h5l.5 4 2 2v1h-9v-1l2-2zM8 9.5v4.5';
  if (isClosing(label)) return 'M4.5 4.5l7 7M11.5 4.5l-7 7';
  if(has('apercu','preview'))return menuPaths.preview;
  if(has('espaces de travail','workspace','workspaces'))return menuPaths.workspace;
  if(has('poser une question','ask about','ask'))return menuPaths.ask;
  if(has('rechercher','search'))return menuPaths.search;
  if(has('synchroniser','synchronisees','sync','synchronized'))return menuPaths.sync;
  if(has('theme','color scheme'))return menuPaths.theme;
  if(has('scindee','split','a cote','beside'))return 'M2 3h12v10H2zM8 3v10';
  if(has('nouvel onglet','new tab'))return menuPaths.tab;
  if(has('note','notes'))return menuPaths.note;
  if(has('session'))return menuPaths.session;
  if(has('extension','extensions'))return menuPaths.extension;
  if(has('imprimer','print'))return menuPaths.print;
  if(has('couper','cut'))return menuPaths.cut;
  if(has('tout selectionner','selectionner tout','select all'))return menuPaths.select;
  if(has('annuler','undo'))return menuPaths.undo;
  if(has('retablir','redo'))return menuPaths.redo;
  if(has('sol ai'))return 'M4 2.5h5.5L12 5v8.5H4zM6 7.5h4M6 10h4';
  if(has('orthographe','orthographique','spelling','spell check','spellcheck'))return menuPaths.spell;
  if(has('devtools'))return menuPaths.tools;
  if(has('design'))return menuPaths.edit;
  if (has('pivoter', 'rotate')) return 'M12.8 8a4.8 4.8 0 1 1-1.4-3.4M12.8 3.2v3h-3';
  if (has('comparer', 'compare', 'comparaison', 'comparison', 'ecrans', 'screens')) return 'M1.5 4.5h3.5v7H1.5zM6.5 3.5h3v9h-3zM11 5.5h3.5v5H11z';
  if (has('macbook', 'imac')) return 'M3.5 3.5h9v6.5h-9zM1.5 12.5h13';
  if (has('ordinateur', 'desktop')) return 'M2.5 3.5h11v7h-11zM6 13h4M8 10.5V13';
  if (has('mobile', 'iphone', 'ipad', 'appareil', 'device')) return 'M5 2.5h6v11H5zM7.3 11.5h1.4';
  if (has('zone', 'area')) return 'M2.5 5.5v-3h3M10.5 2.5h3v3M13.5 10.5v3h-3M5.5 13.5h-3v-3';
  if (has('visible', 'entiere', 'capturer', 'capture'))
    return 'M2.5 5.5h2l1-1.5h5l1 1.5h2v7h-11zM8 11a2 2 0 1 0 0-4 2 2 0 0 0 0 4';
  if (has('copier', 'copy','dupliquer','duplicate')) return 'M5.5 3.5h7v7h-7zM3.5 6.5v6h6';
  if (has('coller', 'paste')) return 'M5 3h6v2H5zM3.5 4.5h9v9h-9z';
  if (has('recharger', 'actualiser', 'reload', 'refresh')) return 'M12.8 8a4.8 4.8 0 1 1-1.4-3.4M12.8 3.2v3h-3';
  if (has('precedent', 'retour', 'back')) return 'M9.5 4L5.5 8l4 4';
  if (has('suivant', 'avancer', 'forward')) return 'M6.5 4l4 4-4 4';
  if (has('reseau', 'network')) return 'M2.5 12.5h2v-3h-2zM7 12.5h2v-6H7zM11.5 12.5h2v-9h-2z';
  if (has('inspecter', 'source', 'inspect')) return 'M5.5 5L2.5 8l3 3M10.5 5l3 3-3 3';
  return '';
}

export const isClosing = (label: string) => /^(fermer|close)\b/.test(normalized(label));
