// Canal avec l'hôte C#. Les types de commandes sont ceux validés par CommandParser côté hôte.
export type Tab = {
  id: string;
  url: string;
  title: string;
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
export function onState(callback: (state: State) => void, onEvent?: (name: string) => void) {
  return onMessage(data => {
    const event = (data as { event?: unknown }).event;
    if (typeof event === 'string') onEvent?.(event);
    else callback(data as State);
  });
}
