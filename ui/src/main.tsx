import { createRoot } from 'react-dom/client';
import App from './App';
import Dock from './Dock';
import Panel from './Panel';
import Popup from './Popup';
import Sidebar from './Sidebar';
import './theme.css';
import './app.css';
import './sidebar.css';
import './popup.css';
import './panel.css';
import './motion.css';

// Le même bundle sert la barre supérieure, la carte latérale (#sidebar), les menus (#popup) et l'encart (#dock).
const hash = location.hash;
if (hash === '#popup' || hash === '#dock') document.body.classList.add('popup-body');
if (hash === '#sidebar') document.body.classList.add('sidebar-body');
if (hash === '#panel') document.body.classList.add('panel-body');

const root = createRoot(document.getElementById('root')!);
root.render(hash === '#panel' ? <Panel /> : hash === '#popup' ? <Popup /> : hash === '#dock' ? <Dock /> : hash === '#sidebar' ? <Sidebar /> : <App />);
