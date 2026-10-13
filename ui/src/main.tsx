import { createRoot } from 'react-dom/client';
import App from './App';
import Dock from './Dock';
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
import './popup.css';
import './panel.css';
import './welcome.css';
import './motion.css';
import './beui-sol.css';
import './beui-workspace.css';

// Le même bundle sert la barre supérieure, la carte latérale (#sidebar), les menus (#popup) et l'encart (#dock).
const hash = location.hash;
if (location.hostname === 'ui.navigateur') installTooltips();
if (hash === '#start') document.body.classList.add('start-body');
if (hash === '#notes') document.body.classList.add('notes-body');
if (hash === '#popup' || hash === '#dock' || hash === '#capture') document.body.classList.add('popup-body');
if (hash === '#sidebar') document.body.classList.add('sidebar-body');
if (hash === '#panel') document.body.classList.add('panel-body');
if (hash === '#welcome') document.body.classList.add('welcome-body');

const container = document.getElementById('root')!;
// L'hôte ne dévoile la surface qu'une fois son contenu rendu, pour éviter le cadre vide du moteur.
const firstPaint = new MutationObserver(() => {
  if (!container.firstElementChild) return;
  firstPaint.disconnect();
  requestAnimationFrame(() => requestAnimationFrame(() => {
    document.documentElement.classList.add('painted');
    painted();
  }));
});
firstPaint.observe(container, { childList: true });
const root = createRoot(container);
root.render(hash === '#notes' ? <NotesPage /> : hash === '#capture' ? <Capture /> : hash === '#start' ? <Start /> : hash === '#welcome' ? <Welcome /> : hash === '#panel' ? <Panel /> : hash === '#popup' ? <Popup /> : hash === '#dock' ? <Dock /> : hash === '#sidebar' ? <Sidebar /> : <App />);
