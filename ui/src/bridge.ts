// Canal avec l'hôte C#. Les types de commandes sont ceux validés par CommandParser côté hôte.
export type Tab = {
  id: string;
  url: string;
  title: string;
  pinned: boolean;
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
  maximized: boolean;
  downloads: { count: number; active: number };
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

export function onMessage(callback: (data: unknown) => void) {
  const handler = (e: MessageEvent) => callback(e.data);
  webview?.addEventListener('message', handler);
  return () => webview?.removeEventListener('message', handler);
}

// L'hôte envoie soit l'état complet, soit un événement ponctuel { event: "…" }.
export type HostEvent = { event: string; id?: string; data?: string | null };

export function onState(callback: (state: State) => void, onEvent?: (event: HostEvent) => void) {
  return onMessage(data => {
    if (typeof (data as HostEvent).event === 'string') onEvent?.(data as HostEvent);
    else callback(data as State);
  });
}
