// Canal avec l'hôte C#. Les types de commandes sont ceux validés par CommandParser côté hôte.
export type Tab = {
  id: string;
  url: string;
  title: string;
  pinned: boolean;
  zoom: number; // en pour cent
  loading: boolean;
  canGoBack: boolean;
  canGoForward: boolean;
  bookmarked: boolean;
};

export type State = {
  tabs: Tab[];
  activeId: string | null;
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
  tint?: { topbar: boolean; sidebar: boolean; menus: boolean; background: boolean };
};

export function onMessage(callback: (data: unknown) => void) {
  const handler = (e: MessageEvent) => {
    const ev = e.data as HostEvent | null;
    if (ev && ev.event === 'theme' && ev.bg) {
      document.documentElement.style.setProperty('--page-bg', ev.bg);
      document.documentElement.dataset.theme = ev.dark ? 'dark' : 'light';
      // Couleur d'accent du site. Chaque vue ne la prend que si les réglages l'autorisent pour sa zone ;
      // sans elle, ses surfaces gardent la couleur du fond.
      const zone = location.hash === '#sidebar' ? 'sidebar' : location.hash ? 'menus' : 'topbar';
      const root = document.documentElement.style;
      if (ev.accent && (ev.tint?.[zone] ?? true)) root.setProperty('--site-accent', ev.accent);
      else root.removeProperty('--site-accent');
      // Arrière-plan : le fond de la barre et de la colonne se teinte légèrement (pas celui des menus, qui
      // doit rester celui de la page pour que leurs coins s'y fondent).
      if (ev.accent && zone !== 'menus' && (ev.tint?.background ?? true)) root.setProperty('--site-chrome-accent', ev.accent);
      else root.removeProperty('--site-chrome-accent');
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
