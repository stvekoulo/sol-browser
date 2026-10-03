import { createRoot } from 'react-dom/client';
import App from './App';
import Popup from './Popup';
import './theme.css';
import './app.css';

// Le même bundle sert la barre du navigateur et la petite vue posée sur la page (#popup).
const popup = location.hash === '#popup';
if (popup) document.body.classList.add('popup-body');
createRoot(document.getElementById('root')!).render(popup ? <Popup /> : <App />);
