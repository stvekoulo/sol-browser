import { useEffect, useState, type MouseEvent, type ReactNode } from 'react';
import { onState, send, type State, type Tab } from './bridge';

export default function App() {
  const [state, setState] = useState<State | null>(null);

  useEffect(() => {
    const off = onState(setState, event => {
      if (event === 'focus-address') document.querySelector<HTMLInputElement>('.pane.focused input, .pane input')?.focus();
    });
    send({ type: 'ui.ready' });
    return off;
  }, []);

  if (!state) return null;
  const byId = (id: string | null) => state.tabs.find(t => t.id === id);
  const { split } = state;
  const panes = split.visible ? [byId(split.primaryId), byId(split.secondaryId)] : [byId(state.activeId)];

  return (
    <>
      <TitleBar state={state} />
      <nav className="toolbar">
        {panes.map((tab, i) => (
          <Pane
            key={tab?.id ?? i}
            tab={tab}
            focused={tab?.id === state.activeId}
            grow={!split.visible ? 1 : i === 0 ? split.ratio : 1 - split.ratio}
          />
        ))}
        <button
          className="icon"
          aria-label="Favoris et historique"
          title="Favoris et historique"
          onClick={e => {
            const box = e.currentTarget.getBoundingClientRect();
            send({ type: 'library.open', x: box.right, y: box.bottom + 6 });
          }}
        >
          <Icon d="M3 3.5h10M3 8h10M3 12.5h10" />
        </button>
        <button
          className={`icon${split.visible ? ' on' : ''}`}
          aria-label={split.visible ? 'Fermer la vue scindée' : 'Afficher deux pages côte à côte'}
          aria-pressed={split.visible}
          title={split.visible ? 'Fermer la vue scindée' : 'Afficher deux pages côte à côte'}
          onClick={() => send({ type: split.visible ? 'split.close' : 'split.open' })}
        >
          <Icon d="M2 3h12v10H2zM8 3v10" />
        </button>
      </nav>
    </>
  );
}

function TitleBar({ state }: { state: State }) {
  // Seul le fond de la barre déplace la fenêtre, pas les onglets ni les boutons.
  const drag = (e: MouseEvent) => {
    if (e.button === 0 && e.target === e.currentTarget) send({ type: 'window.drag' });
  };
  const maximize = (e: MouseEvent) => {
    if (e.target === e.currentTarget) send({ type: 'window.maximize' });
  };

  const { tabs, split, activeId } = state;
  const items: ReactNode[] = [];
  for (let i = 0; i < tabs.length; i++) {
    const tab = tabs[i], next = tabs[i + 1];
    // Les deux onglets d'une vue scindée s'affichent comme un seul onglet en deux moitiés.
    if (next && tab.id === split.primaryId && next.id === split.secondaryId) {
      items.push(
        <div key={tab.id} className={`tab-group${split.visible ? ' active' : ''}`}>
          <TabItem tab={tab} active={tab.id === activeId} />
          <TabItem tab={next} active={next.id === activeId} />
        </div>,
      );
      i++;
    } else {
      items.push(<TabItem key={tab.id} tab={tab} active={tab.id === activeId} />);
    }
  }

  return (
    <header className="titlebar" onMouseDown={drag} onDoubleClick={maximize}>
      <div className="lights">
        <button className="light close" aria-label="Fermer" onClick={() => send({ type: 'window.close' })} />
        <button className="light minimize" aria-label="Réduire" onClick={() => send({ type: 'window.minimize' })} />
        <button
          className="light maximize"
          aria-label={state.maximized ? 'Restaurer' : 'Agrandir'}
          onClick={() => send({ type: 'window.maximize' })}
        />
      </div>
      <div className="tabs" role="tablist" onMouseDown={drag} onDoubleClick={maximize}>
        {items}
        <button className="icon new-tab" aria-label="Nouvel onglet" onClick={() => send({ type: 'tab.open' })}>
          <Icon d="M8 3v10M3 8h10" />
        </button>
      </div>
    </header>
  );
}

function TabItem({ tab, active }: { tab: Tab; active: boolean }) {
  const label = tab.title && tab.title !== 'about:blank' ? tab.title : 'Nouvel onglet';
  return (
    <div
      role="tab"
      aria-selected={active}
      className={`tab${active ? ' active' : ''}`}
      title={label}
      onMouseDown={e => {
        if (e.button === 0) send({ type: 'tab.activate', id: tab.id });
        if (e.button === 1) send({ type: 'tab.close', id: tab.id });
      }}
    >
      {tab.loading && <span className="spinner" aria-hidden />}
      <span className="tab-title">{label}</span>
      <button
        className="icon tab-close"
        aria-label={`Fermer ${label}`}
        onMouseDown={e => e.stopPropagation()}
        onClick={() => send({ type: 'tab.close', id: tab.id })}
      >
        <Icon d="M4 4l8 8M12 4l-8 8" />
      </button>
    </div>
  );
}

// Navigation et adresse d'une page. En vue scindée, il y en a une par page, alignée au-dessus d'elle.
function Pane({ tab, focused, grow }: { tab: Tab | undefined; focused: boolean; grow: number }) {
  const url = !tab || tab.url === 'about:blank' ? '' : tab.url;
  const [text, setText] = useState(url);
  const [editing, setEditing] = useState(false);

  // L'adresse suit la page tant que l'utilisateur n'est pas en train de taper.
  useEffect(() => {
    if (!editing) setText(url);
  }, [url, editing]);

  const id = tab?.id;
  return (
    <div className={`pane${focused ? ' focused' : ''}`} style={{ flexGrow: grow }}>
      <button className="icon" aria-label="Précédent" disabled={!tab?.canGoBack} onClick={() => id && send({ type: 'nav.back', id })}>
        <Icon d="M10 3L5 8l5 5" />
      </button>
      <button className="icon" aria-label="Suivant" disabled={!tab?.canGoForward} onClick={() => id && send({ type: 'nav.forward', id })}>
        <Icon d="M6 3l5 5-5 5" />
      </button>
      {tab?.loading ? (
        <button className="icon" aria-label="Arrêter" onClick={() => id && send({ type: 'nav.stop', id })}>
          <Icon d="M4 4l8 8M12 4l-8 8" />
        </button>
      ) : (
        <button className="icon" aria-label="Recharger" disabled={!id} onClick={() => id && send({ type: 'nav.reload', id })}>
          <Icon d="M13 8a5 5 0 1 1-1.5-3.5M13 3v3h-3" />
        </button>
      )}
      <form
        className="address"
        onSubmit={e => {
          e.preventDefault();
          if (id && text.trim()) send({ type: 'nav.go', id, input: text });
          (document.activeElement as HTMLElement | null)?.blur();
        }}
      >
        <input
          aria-label="Adresse ou recherche"
          placeholder="Rechercher ou saisir une adresse"
          spellCheck={false}
          value={text}
          onChange={e => {
            setText(e.target.value);
            // Les suggestions s'affichent dans une vue posée sous le champ : l'hôte a besoin de sa position.
            const box = e.target.getBoundingClientRect();
            if (id) send({ type: 'suggest', id, query: e.target.value, x: box.left, y: box.bottom + 6, width: box.width });
          }}
          onFocus={e => {
            setEditing(true);
            e.target.select();
            if (id && !focused) send({ type: 'tab.activate', id });
          }}
          onBlur={() => {
            setEditing(false);
            // Requête vide : l'hôte referme les suggestions, sans toucher à un autre panneau.
            if (id) send({ type: 'suggest', id, query: '', x: 0, y: 0, width: 0 });
          }}
        />
      </form>
      <button
        className={`icon${tab?.bookmarked ? ' on' : ''}`}
        aria-label={tab?.bookmarked ? 'Retirer des favoris' : 'Ajouter aux favoris'}
        aria-pressed={!!tab?.bookmarked}
        title={tab?.bookmarked ? 'Retirer des favoris' : 'Ajouter aux favoris'}
        disabled={!url}
        onClick={() => tab && send(tab.bookmarked ? { type: 'bookmark.remove', url: tab.url } : { type: 'bookmark.add', url: tab.url, title: tab.title })}
      >
        <Icon d="M8 2.2l1.8 3.7 4 .6-2.9 2.8.7 4L8 11.4l-3.6 1.9.7-4L2.2 6.5l4-.6z" fill={!!tab?.bookmarked} />
      </button>
    </div>
  );
}

function Icon({ d, fill = false }: { d: string; fill?: boolean }) {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill={fill ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <path d={d} />
    </svg>
  );
}
