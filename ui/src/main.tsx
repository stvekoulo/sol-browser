import { createRoot } from 'react-dom/client';
import App from './App';
import Popup from './Popup';
import Sidebar from './Sidebar';
import './theme.css';
import './app.css';
import './sidebar.css';
import './popup.css';

// Le même bundle sert la barre supérieure, la carte latérale (#sidebar) et les menus (#popup).
const hash = location.hash;
if (hash === '#popup') document.body.classList.add('popup-body');
if (hash === '#sidebar') document.body.classList.add('sidebar-body');

const root = createRoot(document.getElementById('root')!);
root.render(hash === '#popup' ? <Popup /> : hash === '#sidebar' ? <Sidebar /> : <App />);
