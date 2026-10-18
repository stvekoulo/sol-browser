import type {IconName} from './AnimatedIcon';
import {menuPaths} from './PopupIcons';
const paths:Record<string,IconName>={
  [menuPaths.save]:'save',[menuPaths.group]:'group',[menuPaths.groupAdd]:'groupAdd',[menuPaths.ungroup]:'ungroup',[menuPaths.tab]:'tab',[menuPaths.edit]:'edit',[menuPaths.session]:'session',[menuPaths.select]:'select',[menuPaths.undo]:'undo',[menuPaths.redo]:'redo',[menuPaths.sleep]:'sleep',[menuPaths.expand]:'expand',[menuPaths.collapse]:'collapse',[menuPaths.tools]:'tools',
  'M2.5 5.5v-3h3M10.5 2.5h3v3M13.5 10.5v3h-3M5.5 13.5h-3v-3':'select',
  'M2.5 12.5h2v-3h-2zM7 12.5h2v-6H7zM11.5 12.5h2v-9h-2z':'network',
  'M4 2.5h5.5L12 5v8.5H4zM6 7.5h4M6 10h4':'ai',
  'M8 2.5v7.5M5 7l3 3 3-3M3.5 13h9':'download',
  'M8 2.5v8M4.5 7.5L8 11l3.5-3.5M3 13.5h10':'download',
  'M4.5 4.5l7 7M11.5 4.5l-7 7':'close','M4 4l8 8M12 4l-8 8':'close',
  'M9.5 4L5.5 8l4 4':'back','M10 3L5 8l5 5':'back','M6.5 4l4 4-4 4':'forward',
  'M8 3.5v9M3.5 8h9':'plus','M8 3v10M3 8h10':'plus',
  'M3.5 8.5l3 3 6-6.5':'check','M3 8l3 3 7-7':'check',
  'M4 2h8v12H4zM6 5h4M6 8h4':'notes',
  'M4 2.5h8v11H4zM6 5h4M6 8h4M6 11h2':'notes',
  'M2 7a6 6 0 1 1 1.5 5M2 3v4h4M8 4.5V8l2.5 1.5':'history',
  'M12.8 8a4.8 4.8 0 1 1-1.4-3.4M12.8 3.2v3h-3':'reload',
  'M5.5 3.5h7v7h-7zM3.5 6.5v6h6':'copy',
  'M6 11l5-5a2 2 0 0 0-3-3L3 8a4 4 0 0 0 6 6l5-5':'attach',
  'M2.5 5h4M9.5 5h4M2.5 11h7M12.5 11h1M8 3.5a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3M11 9.5a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3':'settings',
  'M8 3.5v4.5l3 1.8M13.5 8a5.5 5.5 0 1 1-11 0 5.5 5.5 0 0 1 11 0z':'history',
};
export function iconNameForPath(path:string):IconName|undefined{
  if(paths[path])return paths[path];
  if(path.includes('a4 4')&&(path.includes('l3 3')||path.includes('M10 10')||path.includes('M10.5 10.5')))return 'search';
  return undefined;
}
