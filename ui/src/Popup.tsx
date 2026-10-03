import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { onMessage, send } from './bridge';

type Item = { url: string; title: string; kind?: 'search' | 'go' | 'history' | 'site' };
type Content =
  | { popup: 'suggestions'; items: Item[] }
  | { popup: 'library'; bookmarks: Item[]; history: Item[] };

// Contenu de la vue posée sur la page. L'hôte envoie quoi afficher ; la vue annonce sa hauteur.
export default function Popup() {
  const [content, setContent] = useState<Content | null>(null);
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const off = onMessage(data => setContent(data as Content));
    // Perdre le curseur (clic ailleurs) referme la vue.
    const close = () => send({ type: 'popup.close' });
    window.addEventListener('blur', close);
    const key = (e: KeyboardEvent) => e.key === 'Escape' && close();
    window.addEventListener('keydown', key);
    return () => {
      off();
      window.removeEventListener('blur', close);
      window.removeEventListener('keydown', key);
    };
  }, []);

  useLayoutEffect(() => {
    if (content && root.current) send({ type: 'popup.size', height: root.current.offsetHeight });
  }, [content]);

  if (!content) return null;

  // mousedown plutôt que click : le choix part avant que la barre d'adresse ne perde le curseur et referme la vue.
  if (content.popup === 'suggestions')
    return (
      <div ref={root} className="popup" role="listbox" aria-label="Suggestions">
        {content.items.map((item, index) => (
          <Row key={item.kind + item.url} item={item} onPick={() => send({ type: 'suggest.pick', index })} />
        ))}
      </div>
    );

  const open = (item: Item) => send({ type: 'tab.open', url: item.url });
  return (
    <div ref={root} className="popup">
      <Section title="Favoris" empty="Aucun favori. L'étoile de la barre d'adresse en ajoute un." items={content.bookmarks} onPick={open} />
      <Section title="Historique récent" empty="Aucune page visitée." items={content.history} onPick={open} />
    </div>
  );
}

function Section({ title, empty, items, onPick }: { title: string; empty: string; items: Item[]; onPick: (item: Item) => void }) {
  return (
    <section>
      <h2>{title}</h2>
      {items.length === 0 ? <p className="empty">{empty}</p> : items.map(item => <Row key={item.url} item={item} onPick={() => onPick(item)} />)}
    </section>
  );
}

function Row({ item, onPick }: { item: Item; onPick: () => void }) {
  return (
    <button className="row" role="option" aria-selected={false} onMouseDown={e => e.button === 0 && onPick()}>
      {item.kind === 'search' ? (
        <span className="row-title">Rechercher « {item.title} »</span>
      ) : item.kind === 'go' ? (
        <span className="row-title">Ouvrir {item.title}</span>
      ) : (
        <>
          <span className="row-title">{item.title || item.url}</span>
          <span className="row-url">{item.url.replace(/^https?:\/\/(www\.)?/, '')}</span>
        </>
      )}
    </button>
  );
}
