// Canal avec l'hôte C#. Les types de commandes sont ceux validés par CommandParser côté hôte.
export type Tab = {
  id: string;
  url: string;
  title: string;
  loading: boolean;
  canGoBack: boolean;
  canGoForward: boolean;
};

export type State = {
  tabs: Tab[];
  activeId: string | null;
  split: { secondaryId: string | null; ratio: number };
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
};

type WebView = {
  postMessage(message: string): void;
  addEventListener(type: 'message', handler: (e: MessageEvent) => void): void;
  removeEventListener(type: 'message', handler: (e: MessageEvent) => void): void;
};

const webview = (window as unknown as { chrome?: { webview?: WebView } }).chrome?.webview;

export const send = (command: Command) => webview?.postMessage(JSON.stringify(command));

// L'hôte envoie soit l'état complet, soit un événement ponctuel { event: "…" }.
export function onState(callback: (state: State) => void, onEvent?: (name: string) => void) {
  const handler = (e: MessageEvent) => {
    if (typeof e.data?.event === 'string') onEvent?.(e.data.event);
    else callback(e.data as State);
  };
  webview?.addEventListener('message', handler);
  return () => webview?.removeEventListener('message', handler);
}
