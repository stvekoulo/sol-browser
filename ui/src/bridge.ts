import { setLang } from './i18n';

// Canal avec l'hôte C#. Les types de commandes sont ceux validés par CommandParser côté hôte.
export type Tab = {
  id: string;
  url: string;
  title: string;
  pinned: boolean;
  groupId: string | null;
  asleep?: boolean; // déchargé (fermé dans un groupe, épinglé fermé, ou pas encore rouvert)
  device: string | null; // simulateur : nom de l'appareil affiché
  zoom: number; // en pour cent
  loading: boolean;
  canGoBack: boolean;
  canGoForward: boolean;
  bookmarked: boolean;
};

export type TabGroup = { id: string; name: string; color: string; icon: string; collapsed: boolean };

export type State = {
  tabs: Tab[];
  groups: TabGroup[];
  activeId: string | null;
  privateMode: boolean; // fenêtre de navigation privée
  shield: boolean; // blocage des publicités et des traqueurs en service
  // Raccourcis de la carte latérale ; tabId : l'onglet ouvert dans le raccourci, qui n'a pas de ligne dans la liste.
  shortcuts: { url: string; name: string; icon: string | null; tabId: string | null }[];
  // Paire d'onglets scindés ; visible quand l'onglet actif est l'un des deux.
  split: { primaryId: string | null; secondaryId: string | null; ratio: number; visible: boolean };
  profile: string; // nom affiché du profil de cette fenêtre
  maximized: boolean;
  downloads: { count: number; active: number; docked: boolean };
};

export type Command = {
  type: string;
  id?: string;
  url?: string;
  title?: string;
  input?: string;
  query?: string;
  index?: number;
  ratio?: number;
  x?: number;
  y?: number;
  width?: number;
  height?: number;
};

type WebView = {
  postMessage(message: string): void;
  addEventListener(type: 'message', handler: (e: MessageEvent) => void): void;
  removeEventListener(type: 'message', handler: (e: MessageEvent) => void): void;
};

const webview = (window as unknown as { chrome?: { webview?: WebView } }).chrome?.webview;

export const send = (command: Command) => webview?.postMessage(JSON.stringify(command));
export const painted = () => webview?.postMessage('"ui.painted"');

// L'hôte envoie soit l'état complet, soit un événement ponctuel { event: "…" }.
export type HostEvent = {
  event: string;
  id?: string;
  data?: string | null;
  text?: string;
  count?: number;
  open?: boolean;
  bg?: string;
  dark?: boolean;
  accent?: string | null;
  mode?: 'page' | 'light' | 'dark' | 'system';
  font?: string; // police de l'interface : default, system, inter, sf
  lang?: string; // langue de l'interface, choisie par l'hôte
  systemDark?: boolean; // thème de Windows, lu par l'hôte
  privateMode?: boolean;
  uiScale?: number;
  tint?: { topbar: boolean; sidebar: boolean; menus: boolean; background: boolean };
};

// Fonds neutres des thèmes imposés. En thème par défaut, le fond de l'interface est celui de la page.
// Le sombre est proche du noir des sites sombres : « Sombre » et « Système » rendent comme « Par défaut » sur un tel site.
const NEUTRAL = { light: 'rgb(243, 243, 246)', dark: 'rgb(9, 9, 11)' };
export const interfaceScale = () => Number(document.documentElement.style.getPropertyValue('--ui-scale')) || 1;

function applyTheme(ev: HostEvent) {
  const root = document.documentElement.style;
  if (location.hostname === 'ui.navigateur') {
    const scale = ev.uiScale === 0.8 || ev.uiScale === 0.9 ? ev.uiScale : 1;
    const changed = interfaceScale() !== scale;
    root.setProperty('--ui-scale', String(scale));
    document.body.style.zoom = String(scale);
    document.body.style.height = '100%';
    if (changed) window.dispatchEvent(new Event('ui-scale'));
  }
  document.documentElement.dataset.private = String(!!ev.privateMode);
  document.documentElement.dataset.font = ev.font ?? 'default';
  // L'accueil est clair par défaut : il ne suit pas la page ouverte derrière lui, seulement un thème choisi exprès.
  const welcome = location.hash === '#welcome';
  const mode = welcome && (ev.mode ?? 'page') === 'page' ? 'light' : ev.mode ?? 'page';
  if (welcome) ev = { ...ev, accent: null };
  const dark = mode === 'page' ? !!ev.dark : mode === 'system' ? !!ev.systemDark : mode === 'dark';
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  root.setProperty('--page-bg', ev.privateMode ? (dark ? '#171120' : '#eee7f7') : mode === 'page' || (dark && ev.dark) ? ev.bg! : dark ? NEUTRAL.dark : NEUTRAL.light);
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
  const site = ev.tint?.[zone] ?? true, chrome = zone !== 'menus' && (ev.tint?.background ?? true);
  if (ev.accent && site) root.setProperty('--site-accent', ev.accent);
  else root.removeProperty('--site-accent');
  if (ev.accent && chrome) root.setProperty('--site-chrome-accent', ev.accent);
  else root.removeProperty('--site-chrome-accent');
}

export function onMessage(callback: (data: unknown) => void) {
  const handler = (e: MessageEvent) => {
    const ev = e.data as HostEvent | null;
    if (ev && ev.event === 'theme' && ev.bg) {
      setLang(ev.lang);
      applyTheme(ev);
      return;
    }
    callback(e.data);
  };
  webview?.addEventListener('message', handler);
  return () => webview?.removeEventListener('message', handler);
}

export function onState(callback: (state: State) => void, onEvent?: (event: HostEvent) => void) {
  return onMessage(data => {
    if (typeof (data as HostEvent).event === 'string') onEvent?.(data as HostEvent);
    else callback(data as State);
  });
}

// Extrait le nom de domaine épuré (ex. "corn.tech") pour la pilule centrale de la barre du haut.
export const isStartPage = (url: string) => url === 'https://start.sol/index.html#start';
export function formatDomain(url: string): string {
  if (!url || url === 'about:blank' || isStartPage(url)) return '';
  try {
    const parsed = new URL(url);
    if (parsed.protocol === 'http:' || parsed.protocol === 'https:')
      return parsed.hostname.replace(/^www\./i, '');
  } catch {
    // URL non standard : affichage brut
  }
  return url;
}
