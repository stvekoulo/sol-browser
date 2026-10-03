import { setLang } from './i18n';

// Canal avec l'hôte C#. Les types de commandes sont ceux validés par CommandParser côté hôte.
export type Tab = {
  id: string;
  url: string;
  title: string;
  pinned: boolean;
  groupId: string | null;
  device: string | null; // vue mobile : appareil simulé (« iPhone », « iPad »)
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
  lang?: string; // langue de l'interface, choisie par l'hôte
  systemDark?: boolean; // thème de Windows, lu par l'hôte
  tint?: { topbar: boolean; sidebar: boolean; menus: boolean; background: boolean };
};

// Fonds neutres des thèmes imposés. En thème par défaut, le fond de l'interface est celui de la page.
const NEUTRAL = { light: 'rgb(243, 243, 246)', dark: 'rgb(30, 30, 33)' };

function applyTheme(ev: HostEvent) {
  const root = document.documentElement.style;
  const mode = ev.mode ?? 'page';
  const dark = mode === 'page' ? !!ev.dark : mode === 'system' ? !!ev.systemDark : mode === 'dark';
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  root.setProperty('--page-bg', mode === 'page' ? ev.bg! : dark ? NEUTRAL.dark : NEUTRAL.light);
  // Couleur réelle de la page, quel que soit le thème : c'est elle que prennent les coins des vues flottantes,
  // pour se fondre dans la page qu'elles recouvrent.
  root.setProperty('--page-real', ev.bg!);

  // Couleur d'accent du site. Chaque vue ne la prend que si les réglages l'autorisent pour sa zone ;
  // sans elle, ses surfaces gardent la couleur du fond.
  const zone = location.hash === '#sidebar' ? 'sidebar' : location.hash ? 'menus' : 'topbar';
  if (ev.accent && (ev.tint?.[zone] ?? true)) root.setProperty('--site-accent', ev.accent);
  else root.removeProperty('--site-accent');
  // Arrière-plan : le fond de la barre et de la colonne se teinte légèrement (jamais celui des menus).
  if (ev.accent && zone !== 'menus' && (ev.tint?.background ?? true)) root.setProperty('--site-chrome-accent', ev.accent);
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
export function formatDomain(url: string): string {
  if (!url || url === 'about:blank') return '';
  try {
    const parsed = new URL(url);
    if (parsed.protocol === 'http:' || parsed.protocol === 'https:')
      return parsed.hostname.replace(/^www\./i, '');
  } catch {
    // URL non standard : affichage brut
  }
  return url;
}
