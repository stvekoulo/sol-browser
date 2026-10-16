import { setLang } from './i18n';
import {applyTheme} from './interfaceTheme';
export {interfaceScale} from './interfaceTheme';

// Canal avec l'hôte C#. Les types de commandes sont ceux validés par CommandParser côté hôte.
export type Tab = {
  id: string;
  url: string;
  title: string;
  pinned: boolean;
  groupId: string | null;
  sessionId: string;
  sessionName: string;
  asleep?: boolean; // déchargé (fermé dans un groupe, épinglé fermé, ou pas encore rouvert)
  device: string | null; // simulateur : nom de l'appareil affiché
  zoom: number; // en pour cent
  loading: boolean;
  canGoBack: boolean;
  canGoForward: boolean;
  bookmarked: boolean;
  storeDetail?: boolean;
  translated?: boolean;
  reader?: boolean;
  ai?: boolean;
  storeInstalled?: boolean; // fiche du store d'une extension déjà installée
};

export type TabGroup = { id: string; name: string; color: string; icon: string; collapsed: boolean };

export type State = {
  tabs: Tab[];
  groups: TabGroup[];
  activeId: string | null;
  privateMode: boolean; // fenêtre de navigation privée
  shield: boolean; // blocage des publicités et des traqueurs en service
  extensions?: { id: string; name: string; icon: string | null }[];
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

// Le nom sort de la petite vue web de la barre : une infobulle Windows ne sera pas coupée à 52 px.
export function installTooltips() {
  const namedFromTitle = new WeakMap<Element, string>();
  const captureTitle = (element: Element) => {
    const title = element.getAttribute('title');
    const previousTitle = element.getAttribute('data-sol-tooltip');
    const previousLabel = namedFromTitle.get(element);
    if (previousLabel !== undefined && element.getAttribute('aria-label') !== previousLabel) namedFromTitle.delete(element);
    if (title) {
      if (!element.hasAttribute('aria-description') || element.getAttribute('aria-description') === previousTitle) element.setAttribute('aria-description', title);
      element.setAttribute('data-sol-tooltip', title);
      if (namedFromTitle.has(element) || (!element.hasAttribute('aria-label') && !element.textContent?.trim())) {
        element.setAttribute('aria-label', title);
        namedFromTitle.set(element, title);
      }
      // Un titre vide neutralise l'infobulle du moteur et reste observable si React le retire.
      element.setAttribute('title', '');
    } else if (title === null) {
      if (previousTitle && element.getAttribute('aria-description') === previousTitle) element.removeAttribute('aria-description');
      element.removeAttribute('data-sol-tooltip');
      if (namedFromTitle.has(element)) element.removeAttribute('aria-label');
      namedFromTitle.delete(element);
    }
  };
  const captureTree = (element: Element) => {
    if (element.hasAttribute('title')) captureTitle(element);
    element.querySelectorAll('[title]').forEach(captureTitle);
  };
  captureTree(document.documentElement);
  new MutationObserver(records => {
    for (const record of records) {
      if (record.type === 'attributes') captureTitle(record.target as Element);
      else record.addedNodes.forEach(node => { if (node instanceof Element) captureTree(node); });
    }
  }).observe(document.documentElement, { subtree: true, childList: true, attributes: true, attributeFilter: ['title'] });
  let timer = 0;
  let hovered: HTMLElement | null = null;
  const hide = () => { clearTimeout(timer); hovered = null; send({ type: 'ui.tooltip' }); };
  document.addEventListener('pointerover', e => {
    const button = (e.target as Element).closest<HTMLElement>('button, [role="button"], [data-sol-tooltip]');
    if (button === hovered) return;
    hide();
    if (!button) return;
    const title = button.getAttribute('data-sol-tooltip') || button.getAttribute('aria-label');
    if (!title) return;
    hovered = button;
    timer = window.setTimeout(() => {
      const box = button.getBoundingClientRect();
      send({ type: 'ui.tooltip', title, x: box.x, y: box.y, width: box.width, height: box.height });
    }, 500);
  });
  document.addEventListener('pointerout', e => {
    if (hovered && (!(e.relatedTarget instanceof Node) || !hovered.contains(e.relatedTarget))) hide();
  });
  document.addEventListener('pointerdown', hide, true);
  document.addEventListener('wheel', hide, { passive: true });
  document.addEventListener('keydown', hide);
  window.addEventListener('blur', hide);
}

// L'hôte envoie soit l'état complet, soit un événement ponctuel { event: "…" }.
export type HostEvent = {
  event: string;
  id?: string;
  data?: string | null;
  text?: string;
  count?: number;
  open?: boolean;
  width?: number;
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
    const event = data as HostEvent;
    if (event.event === 'invoke-action' && event.data) {
      Array.from(document.querySelectorAll<HTMLButtonElement>('button[data-action]')).find(button => button.dataset.action === event.data)?.click();
      return;
    }
    if (typeof (data as HostEvent).event === 'string') onEvent?.(data as HostEvent);
    else callback(data as State);
  });
}

// Extrait le nom de domaine épuré (ex. "corn.tech") pour la pilule centrale de la barre du haut.
export const isStartPage = (url: string) => url === 'https://start.sol/index.html#start';
export function formatDomain(url: string): string {
  if (url.startsWith('sol://')) return url;
  if (url === 'https://start.sol/index.html#notes') return 'Notes';
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

export function displayAddress(url: string): string {
  return url === 'https://start.sol/index.html#notes' ? 'sol://notes' : url;
}

// Polices de l'interface, dans l'ordre du réglage (voir Fonts dans BrowserController.cs).
export const fontLabels = (fallback: string, system: string) =>
  [fallback, system, 'Inter', 'Inter Bold', 'SF Pro', 'SF Pro Bold', 'Geist', 'Manrope', 'DM Sans', 'Plus Jakarta Sans', 'Outfit', 'Figtree', 'Nunito', 'Lora'];
