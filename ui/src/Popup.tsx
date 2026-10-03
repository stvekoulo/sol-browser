import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { formatDomain, onMessage, send } from './bridge';
import { Icon } from './Icons';

type Item = { url: string; title: string; kind?: 'search' | 'go' | 'history' | 'site' };
type Content =
  | { popup: 'suggestions'; items: Item[] }
  | { popup: 'library'; bookmarks: Item[]; history: Item[] }
  | { popup: 'menu'; items: string[] }
  | { popup: 'downloads'; items: Download[] };

type Download = { id: string; name: string; received: number; total: number | null; state: 'InProgress' | 'Interrupted' | 'Completed' };

const size = (bytes: number) =>
  bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} Ko` : `${(bytes / 1024 / 1024).toFixed(1)} Mo`;

function status(d: Download) {
  if (d.state === 'Completed') return `Terminé · ${size(d.received)}`;
  if (d.state === 'Interrupted') return 'Interrompu';
  return d.total ? `${Math.round((d.received / d.total) * 100)} % · ${size(d.total)}` : size(d.received);
}

// Associe une icône vectorielle contextuelle à chaque libellé de menu.
function menuIconPath(label: string): string {
  const l = label.toLowerCase();
  if (l.includes('épingler') || l.includes('détacher')) return 'M5.5 2.5h5l.5 4 2 2v1h-9v-1l2-2zM8 9.5v4.5';
  if (l.includes('fermer')) return 'M4.5 4.5l7 7M11.5 4.5l-7 7';
  if (l.includes('zone')) return 'M2.5 5.5v-3h3M10.5 2.5h3v3M13.5 10.5v3h-3M5.5 13.5h-3v-3';
  if (l.includes('visible') || l.includes('entière') || l.includes('capturer'))
    return 'M2.5 5.5h2l1-1.5h5l1 1.5h2v7h-11zM8 11a2 2 0 1 0 0-4 2 2 0 0 0 0 4';
  if (l.includes('scindée')) return 'M2 3h12v10H2zM8 3v10';
  if (l.includes('nouvel onglet')) return 'M8 3.5v9M3.5 8h9';
  if (l.includes('copier')) return 'M5.5 3.5h7v7h-7zM3.5 6.5v6h6';
  if (l.includes('coller')) return 'M5 3h6v2H5zM3.5 4.5h9v9h-9z';
  if (l.includes('recharger') || l.includes('actualiser')) return 'M12.8 8a4.8 4.8 0 1 1-1.4-3.4M12.8 3.2v3h-3';
  if (l.includes('précédent') || l.includes('retour')) return 'M9.5 4L5.5 8l4 4';
  if (l.includes('suivant') || l.includes('avancer')) return 'M6.5 4l4 4-4 4';
  if (l.includes('inspecter') || l.includes('source')) return 'M5.5 5L2.5 8l3 3M10.5 5l3 3-3 3';
  return 'M4 8h8M8 4l4 4-4 4';
}

export default function Popup() {
  const [content, setContent] = useState<Content | null>(null);
  const [selected, setSelected] = useState(-1);
  const [libTab, setLibTab] = useState<'bookmarks' | 'history'>('bookmarks');
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const off = onMessage(data => {
      const select = (data as { select?: unknown }).select;
      if (typeof select === 'number') return setSelected(select);
      const next = data as Content;
      setSelected(-1);
      if (next.popup === 'library')
        setLibTab(next.bookmarks.length > 0 ? 'bookmarks' : 'history');
      setContent(next);
    });
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
  }, [content, libTab]);

  if (!content) return null;

  if (content.popup === 'suggestions')
    return (
      <div ref={root} className="popup" role="listbox" aria-label="Suggestions">
        {content.items.map((item, index) => (
          <Row
            key={index}
            item={item}
            selected={index === selected}
            onPick={() => send({ type: 'suggest.pick', index })}
          />
        ))}
      </div>
    );

  if (content.popup === 'menu')
    return (
      <div ref={root} className="popup" role="menu">
        {content.items.map((label, index) => {
          if (label === '') return <hr key={index} />;
          const danger = label.toLowerCase().includes('fermer');
          return (
            <button
              key={index}
              className={`row${danger ? ' danger' : ''}`}
              role="menuitem"
              onMouseDown={e => e.button === 0 && send({ type: 'menu.pick', index })}
            >
              <span className="row-badge" aria-hidden>
                <Icon d={menuIconPath(label)} size={13} />
              </span>
              <span className="row-title">{label}</span>
            </button>
          );
        })}
      </div>
    );

  if (content.popup === 'downloads')
    return (
      <div ref={root} className="popup">
        <div className="popup-header">
          <span className="popup-header-title">Téléchargements</span>
          <span className="popup-badge">{content.items.length}</span>
        </div>
        <div className="popup-list">
          {content.items.map(d => {
            const pct = d.total && d.total > 0 ? Math.round((d.received / d.total) * 100) : null;
            return (
              <div key={d.id} className="download-card">
                <div className="download-top">
                  <span className="row-badge" aria-hidden>
                    <Icon d="M8 2.5v7.5M5 7l3 3 3-3M3.5 13h9" size={13} />
                  </span>
                  <div className="download-text">
                    <span className="row-title">{d.name}</span>
                    <span className="row-sub">{status(d)}</span>
                  </div>
                  <div className="download-actions">
                    {d.state === 'InProgress' && (
                      <button className="pill-action" onClick={() => send({ type: 'download.cancel', id: d.id })}>
                        Annuler
                      </button>
                    )}
                    {d.state === 'Completed' && (
                      <>
                        <button className="pill-action primary" onClick={() => send({ type: 'download.open', id: d.id })}>
                          Ouvrir
                        </button>
                        <button className="pill-action" onClick={() => send({ type: 'download.reveal', id: d.id })}>
                          Dossier
                        </button>
                      </>
                    )}
                  </div>
                </div>
                {d.state === 'InProgress' && pct !== null && (
                  <div className="download-bar">
                    <div className="download-bar-fill" style={{ width: `${pct}%` }} />
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </div>
    );

  const open = (item: Item) => send({ type: 'tab.open', url: item.url });
  const items = libTab === 'bookmarks' ? content.bookmarks : content.history;

  return (
    <div ref={root} className="popup">
      <div className="popup-segment" role="tablist">
        <button
          role="tab"
          aria-selected={libTab === 'bookmarks'}
          className={`popup-seg-btn${libTab === 'bookmarks' ? ' active' : ''}`}
          onMouseDown={e => {
            e.preventDefault();
            setLibTab('bookmarks');
          }}
        >
          <Icon d="M8 2.4l1.7 3.5 3.8.5-2.8 2.7.7 3.8L8 11.1l-3.4 1.8.7-3.8-2.8-2.7 3.8-.5z" size={13} />
          <span>Favoris</span>
          <span className="popup-seg-count">{content.bookmarks.length}</span>
        </button>
        <button
          role="tab"
          aria-selected={libTab === 'history'}
          className={`popup-seg-btn${libTab === 'history' ? ' active' : ''}`}
          onMouseDown={e => {
            e.preventDefault();
            setLibTab('history');
          }}
        >
          <Icon d="M8 3.5v4.5l3 1.8M13.5 8a5.5 5.5 0 1 1-11 0 5.5 5.5 0 0 1 11 0z" size={13} />
          <span>Historique</span>
          <span className="popup-seg-count">{content.history.length}</span>
        </button>
      </div>

      <div className="popup-list">
        {items.length === 0 ? (
          <div className="empty-pill">
            {libTab === 'bookmarks'
              ? "Aucun favori enregistré pour l'instant."
              : 'Aucune page visitée récemment.'}
          </div>
        ) : (
          items.map((item, idx) => (
            <Row
              key={`${item.url}-${idx}`}
              item={{ ...item, kind: libTab === 'bookmarks' ? 'site' : 'history' }}
              onPick={() => open(item)}
            />
          ))
        )}
      </div>
    </div>
  );
}

function Row({ item, selected = false, onPick }: { item: Item; selected?: boolean; onPick: () => void }) {
  const domain = formatDomain(item.url);
  const iconD =
    item.kind === 'search'
      ? 'M10.5 10.5L14 14M11.5 7a4.5 4.5 0 1 1-9 0 4.5 4.5 0 0 1 9 0z'
      : item.kind === 'go'
        ? 'M3.5 8h9M9 4.5L12.5 8 9 11.5'
        : item.kind === 'site'
          ? 'M8 2.4l1.7 3.5 3.8.5-2.8 2.7.7 3.8L8 11.1l-3.4 1.8.7-3.8-2.8-2.7 3.8-.5z'
          : 'M8 3.5v4.5l3 1.8M13.5 8a5.5 5.5 0 1 1-11 0 5.5 5.5 0 0 1 11 0z';

  return (
    <button
      className={`row${selected ? ' selected' : ''}`}
      role="option"
      aria-selected={selected}
      onMouseDown={e => e.button === 0 && onPick()}
    >
      <span className="row-badge" aria-hidden>
        <Icon d={iconD} size={13} />
      </span>
      {item.kind === 'search' ? (
        <>
          <span className="row-title">{item.title}</span>
          <span className="row-pill">Recherche</span>
        </>
      ) : item.kind === 'go' ? (
        <>
          <span className="row-title">Ouvrir {item.title}</span>
          <span className="row-pill">Aller</span>
        </>
      ) : (
        <>
          <span className="row-title">{item.title || domain || item.url}</span>
          {domain && <span className="row-pill">{domain}</span>}
        </>
      )}
    </button>
  );
}
