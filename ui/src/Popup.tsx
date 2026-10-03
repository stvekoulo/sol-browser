import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { onMessage, send } from './bridge';

type Item = { url: string; title: string; kind?: 'search' | 'go' | 'history' | 'site' };
type Content =
  | { popup: 'suggestions'; items: Item[] }
  | { popup: 'library'; bookmarks: Item[]; history: Item[] }
  | { popup: 'menu'; items: string[] }; // libellé vide = séparateur

// Contenu de la vue posée sur la page. L'hôte envoie quoi afficher ; la vue annonce sa hauteur.
export default function Popup() {
  const [content, setContent] = useState<Content | null>(null);
  const [selected, setSelected] = useState(-1); // suggestion surlignée au clavier
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const off = onMessage(data => {
      const select = (data as { select?: unknown }).select;
      if (typeof select === 'number') return setSelected(select);
      setSelected(-1);
      setContent(data as Content);
    });
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
          <Row key={index} item={item} selected={index === selected} onPick={() => send({ type: 'suggest.pick', index })} />
        ))}
      </div>
    );

  if (content.popup === 'menu')
    return (
      <div ref={root} className="popup" role="menu">
        {content.items.map((label, index) =>
          label === '' ? (
            <hr key={index} />
          ) : (
            <button key={index} className="row" role="menuitem" onMouseDown={e => e.button === 0 && send({ type: 'menu.pick', index })}>
              <span className="row-title">{label}</span>
            </button>
          ),
        )}
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

function Row({ item, selected = false, onPick }: { item: Item; selected?: boolean; onPick: () => void }) {
  return (
    <button className={`row${selected ? ' selected' : ''}`} role="option" aria-selected={selected} onMouseDown={e => e.button === 0 && onPick()}>
      {item.kind === 'search' ? (
        <>
          <SearchIcon />
          <span className="row-title">{item.title}</span>
        </>
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

function SearchIcon() {
  return (
    <svg className="row-icon" width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden>
      <circle cx="7" cy="7" r="4.5" />
      <path d="M10.5 10.5L14 14" />
    </svg>
  );
}
