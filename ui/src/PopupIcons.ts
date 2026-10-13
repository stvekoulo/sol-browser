export function menuIconPath(label: string): string {
  const l = label.toLowerCase();
  const has = (...words: string[]) => words.some(w => l.includes(w));
  if (has('épingler', 'détacher', 'pin')) return 'M5.5 2.5h5l.5 4 2 2v1h-9v-1l2-2zM8 9.5v4.5';
  if (isClosing(label)) return 'M4.5 4.5l7 7M11.5 4.5l-7 7';
  if (has('pivoter', 'rotate')) return 'M12.8 8a4.8 4.8 0 1 1-1.4-3.4M12.8 3.2v3h-3';
  if (has('comparer', 'compare', 'écrans', 'screens')) return 'M1.5 4.5h3.5v7H1.5zM6.5 3.5h3v9h-3zM11 5.5h3.5v5H11z';
  if (has('macbook', 'imac')) return 'M3.5 3.5h9v6.5h-9zM1.5 12.5h13';
  if (has('mobile', 'iphone', 'ipad', 'appareil', 'device')) return 'M5 2.5h6v11H5zM7.3 11.5h1.4';
  if (has('ordinateur', 'desktop')) return 'M2.5 3.5h11v7h-11zM6 13h4M8 10.5V13';
  if (has('zone', 'area')) return 'M2.5 5.5v-3h3M10.5 2.5h3v3M13.5 10.5v3h-3M5.5 13.5h-3v-3';
  if (has('visible', 'entière', 'capturer', 'capture'))
    return 'M2.5 5.5h2l1-1.5h5l1 1.5h2v7h-11zM8 11a2 2 0 1 0 0-4 2 2 0 0 0 0 4';
  if (has('scindée', 'split')) return 'M2 3h12v10H2zM8 3v10';
  if (has('nouvel onglet', 'new tab')) return 'M8 3.5v9M3.5 8h9';
  if (has('copier', 'copy')) return 'M5.5 3.5h7v7h-7zM3.5 6.5v6h6';
  if (has('coller', 'paste')) return 'M5 3h6v2H5zM3.5 4.5h9v9h-9z';
  if (has('recharger', 'actualiser', 'reload', 'refresh')) return 'M12.8 8a4.8 4.8 0 1 1-1.4-3.4M12.8 3.2v3h-3';
  if (has('précédent', 'retour', 'back')) return 'M9.5 4L5.5 8l4 4';
  if (has('suivant', 'avancer', 'forward')) return 'M6.5 4l4 4-4 4';
  if (has('réseau', 'network')) return 'M2.5 12.5h2v-3h-2zM7 12.5h2v-6H7zM11.5 12.5h2v-9h-2z';
  if (has('inspecter', 'source', 'inspect')) return 'M5.5 5L2.5 8l3 3M10.5 5l3 3-3 3';
  return 'M4 8h8M8 4l4 4-4 4';
}

export const isClosing = (label: string) => /fermer|close/i.test(label);
