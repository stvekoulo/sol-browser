import {useEffect,useLayoutEffect,useRef,useState} from 'react';
import {interfaceScale,onMessage,send} from './bridge';
import {useLang} from './i18n';
import type {Content} from './PopupTypes';
import PopupViews from './PopupViews';
export default function Popup() {
  const [content, setContent] = useState<Content | null>(null);
  const [selected, setSelected] = useState(-1);
  const [settingsSection, setSettingsSection] = useState('general');
  const root = useRef<HTMLDivElement>(null);
  const fresh = useRef(false);
  const current = useRef<Content | null>(null);
  useLang();
  const [opening, setOpening] = useState(0); // change à chaque ouverture : rejoue l'apparition

  useEffect(() => {
    const off = onMessage(data => {
      const select = (data as { select?: unknown }).select;
      if (typeof select === 'number') return setSelected(select);
      // « fresh » précède le contenu d'une nouvelle ouverture : celui-ci jouera son apparition.
      if ((data as { fresh?: boolean }).fresh) return void (fresh.current = true);
      const isFresh = fresh.current;
      if (isFresh) setOpening(n => n + 1);
      fresh.current = false;
      const next = data as Content;
      current.current = next;
      setSelected(-1);
      if (next.popup === 'settings' && next.section && isFresh) setSettingsSection(next.section);
      setContent(next);
    });
    const close = () => send({ type: 'popup.close' });
    // Perdre le curseur referme la vue, mais pas tout de suite : un clic dans la vue lui fait perdre puis
    // reprendre le curseur, et une fermeture immédiate avalerait ce clic.
    let leaving = 0;
    const blurred = () => {
      window.clearTimeout(leaving);
      leaving = window.setTimeout(() => current.current?.popup !== 'ai-action' && !document.hasFocus() && close(), 150);
    };
    window.addEventListener('blur', blurred);
    const key = (e: KeyboardEvent) => e.key === 'Escape' && close();
    window.addEventListener('keydown', key);
    return () => {
      off();
      window.clearTimeout(leaving);
      window.removeEventListener('blur', blurred);
      window.removeEventListener('keydown', key);
    };
  }, []);

  // La vue annonce sa hauteur à chaque changement, y compris quand l'hôte vient de changer sa largeur :
  // le contenu se remet alors en page et sa hauteur n'est plus celle du premier rendu.
  useLayoutEffect(() => {
    const el = root.current;
    if (!content || !el) return;
    const report = () => send({ type: 'popup.size', height: el.offsetHeight * interfaceScale() });
    window.addEventListener('ui-scale', report);
    report();
    const observer = new ResizeObserver(report);
    observer.observe(el);
    return () => { observer.disconnect(); window.removeEventListener('ui-scale', report); };
  }, [content, settingsSection]);


return content?<PopupViews content={content} rootRef={root} opening={opening} selected={selected} settingsSection={settingsSection} setSettingsSection={setSettingsSection}/>:null;
}
