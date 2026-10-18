import { lazy, Suspense } from 'react';
import { createRoot } from 'react-dom/client';
import Workspace from './Workspace';
import Dock from './Dock';
const SolChat = lazy(() => import('./SolChat'));
import Panel from './Panel';
import Popup from './Popup';
import Sidebar from './Sidebar';
import Welcome from './Welcome';
import Start from './Start';
import NotesPage from './NotesPage';
import Capture from './Capture';
import { painted, installTooltips } from './bridge';
import '@fontsource-variable/inter'; // police Inter, livrée avec le navigateur (chargée seulement si elle est choisie)
// Les autres polices proposées (licence SIL Open Font) : même principe, leurs fichiers ne sont lus que si on les choisit.
import '@fontsource-variable/geist';
import '@fontsource-variable/manrope';
import '@fontsource-variable/dm-sans';
import '@fontsource-variable/plus-jakarta-sans';
import '@fontsource-variable/outfit';
import '@fontsource-variable/figtree';
import '@fontsource-variable/nunito';
import '@fontsource-variable/lora';
import './theme.css';
import './beui.css';
import './app.css';
import './sidebar.css';
import './sidebar-interactions.css';
import './workspace-chrome.css';
import './popup.css';
import './panel.css';
import './welcome.css';
import './motion.css';
import './beui-sol.css';
import './beui-workspace.css';

// Le même bundle sert la barre supérieure, la carte latérale (#sidebar), les menus (#popup) et l'encart (#dock).
const hash = location.hash;
if (location.hostname === 'ui.navigateur') installTooltips();
if (hash === '#popup' || hash === '#dock' || hash === '#capture') document.body.classList.add('popup-body');
if (!hash && location.hostname === 'ui.navigateur') document.body.classList.add('shell-body');
if (hash === '#sidebar') document.body.classList.add('sidebar-body');
if (hash === '#panel') document.body.classList.add('panel-body');
if (hash === '#welcome') document.body.classList.add('welcome-body');
if (hash === '#chat') document.body.classList.add('chat-body');

const container = document.getElementById('root')!;
// L'hôte ne dévoile la surface qu'une fois son contenu rendu, pour éviter le cadre vide du moteur.
const firstPaint = new MutationObserver(() => {
  if (!container.firstElementChild || (document.body.classList.contains('shell-body') && (!container.querySelector('.topbar') || !container.querySelector('.sidebar-card')))) return;
  firstPaint.disconnect();
  requestAnimationFrame(() => requestAnimationFrame(() => {
    document.documentElement.classList.add('painted');
    painted();
  }));
});
firstPaint.observe(container, { childList: true, subtree:true });
const root = createRoot(container);
const render = () => {
  const page = location.hash;
  document.body.classList.toggle('start-body', page === '#start');
  document.body.classList.toggle('notes-body', page === '#notes');
  root.render(<Suspense fallback={null}>{page === '#notes' ? <NotesPage /> : page === '#capture' ? <Capture /> : page === '#start' ? <Start /> : page === '#welcome' ? <Welcome /> : page === '#panel' ? <Panel /> : page === '#chat' ? <SolChat /> : page === '#popup' ? <Popup /> : page === '#dock' ? <Dock /> : page === '#sidebar' ? <Sidebar /> : <Workspace />}</Suspense>);
};
render();
if (location.hostname === 'start.sol') window.addEventListener('hashchange', render);
import './beui-panels.css';
import './notes-responsive.css';
import './beui-cards.css';
