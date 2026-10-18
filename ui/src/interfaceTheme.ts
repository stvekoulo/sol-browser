import type {HostEvent} from './bridge';
import {setFanSettings} from './components/bencho/fanSettings';

// Fonds neutres des thèmes imposés. En thème par défaut, le fond de l'interface est celui de la page.
// Le sombre est proche du noir des sites sombres : « Sombre » et « Système » rendent comme « Par défaut » sur un tel site.
const NEUTRAL = { light: 'rgb(243, 243, 246)', dark: 'rgb(9, 9, 11)' };
export const interfaceScale = () => Number(document.documentElement.style.getPropertyValue('--ui-scale')) || 0.92;
let latest:HostEvent|null=null;
const siteDark=(ev:HostEvent)=>ev.mode==='dark'||(ev.mode==='system'?!!ev.systemDark:ev.mode!=='light'&&!!ev.dark);
const background=(ev:HostEvent,dark:boolean)=>{
  if(ev.privateMode)return dark?'#171120':'#eee7f7';
  if((ev.mode??'page')==='page'){
    const rgb=ev.bg?.startsWith('#')?ev.bg.slice(1).match(/../g)?.map(v=>parseInt(v,16)):ev.bg?.match(/[\d.]+/g)?.slice(0,3).map(Number);
    // Les pages blanches prennent le blanc cassé des notes, sans changer leur contenu.
    if(!dark&&rgb?.length===3&&Math.min(...rgb)>=230&&Math.max(...rgb)-Math.min(...rgb)<=12)return '#eef0e2';
    return ev.bg!;
  }
  return dark&&ev.dark?ev.bg!:dark?NEUTRAL.dark:NEUTRAL.light;
};

export function sidebarTheme(element:HTMLElement|null){
  if(!element||!latest)return;
  const ev=latest,dark=siteDark(ev),accent=ev.privateMode?'#8b5cf6':ev.accent;
  element.dataset.surfaceTheme=dark?'dark':'light';
  element.style.setProperty('--page-bg',background(ev,dark));
  element.style.setProperty('--site-accent',accent&&(ev.tint?.sidebar??true)?accent:'initial');
  element.style.setProperty('--site-chrome-accent',accent&&(ev.tint?.background??true)?accent:'initial');
  element.style.setProperty('--tab-accent',accent??'initial');
}

export function applyTheme(ev: HostEvent) {
  latest=ev;
  if (ev.fan) setFanSettings(ev.fan);
  const root = document.documentElement.style;
  if (location.hostname === 'ui.navigateur' || (location.hostname === 'start.sol' && location.hash === '#notes')) {
    // Même facteur que les dimensions natives, fourni par l'hôte.
    const scale = ev.uiScale !== undefined && [0.86,0.875,0.89,0.905,0.92].includes(ev.uiScale) ? ev.uiScale : 0.92;
    const changed = interfaceScale() !== scale;
    root.setProperty('--ui-scale', String(scale));
    document.body.style.zoom = String(scale);
    document.body.style.height = '100%';
    if (changed) window.dispatchEvent(new Event('ui-scale'));
  } else if (location.hostname === 'start.sol') {
    root.removeProperty('--ui-scale');
    document.body.style.zoom = '';
    document.body.style.height = '';
  }
  document.documentElement.dataset.private = String(!!ev.privateMode);
  document.documentElement.dataset.font = ev.font ?? 'default';
  // L'accueil est clair par défaut : il ne suit pas la page ouverte derrière lui, seulement un thème choisi exprès.
  const welcome = location.hash === '#welcome';
  const mode = welcome && (ev.mode ?? 'page') === 'page' ? 'light' : ev.mode ?? 'page';
  if (welcome) ev = { ...ev, accent: null };
  const dark = siteDark({...ev,mode});
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  root.setProperty('--page-bg',background({...ev,mode},dark));
  if (ev.privateMode) ev = { ...ev, accent: '#8b5cf6' };
  if (ev.accent) root.setProperty('--tab-accent', ev.accent);
  else root.removeProperty('--tab-accent');
  // Couleur réelle de la page, quel que soit le thème : c'est elle que prennent les coins des vues flottantes,
  // pour se fondre dans la page qu'elles recouvrent.
  root.setProperty('--page-real', ev.bg!);

  // Couleur d'accent du site. Chaque vue ne la prend que si les réglages l'autorisent pour sa zone ;
  // sans elle, ses surfaces gardent la couleur du fond.
  const zone = location.hash === '#sidebar' ? 'sidebar' : location.hash ? 'menus' : 'topbar';
  // Arrière-plan : le fond de la barre et de la colonne se teinte légèrement (jamais celui des menus).
  // Posée d'un coup : animer cette couleur redessinerait toute l'interface à chaque image.
  const site = ev.tint?.[zone]??true, chrome = zone!=='menus'&&(ev.tint?.background??true);
  if (ev.accent && site) root.setProperty('--site-accent', ev.accent);
  else root.removeProperty('--site-accent');
  if (ev.accent && chrome) root.setProperty('--site-chrome-accent', ev.accent);
  else root.removeProperty('--site-chrome-accent');
  document.querySelectorAll<HTMLElement>('[data-surface-theme]').forEach(sidebarTheme);
}
