import { useEffect, useState, type MouseEvent } from 'react';
import { onState, send, type State, type Tab } from './bridge';

export default function App() {
  const [state, setState] = useState<State | null>(null);

  useEffect(() => {
    const off = onState(setState, event => {
      if (event === 'focus-address') document.querySelector<HTMLInputElement>('.address input')?.focus();
    });
    send({ type: 'ui.ready' });
    return off;
  }, []);

  if (!state) return null;
  const index = state.tabs.findIndex(t => t.id === state.activeId);
  // La vue scindée place l'onglet voisin à droite de l'onglet actif.
  const neighbour = state.tabs[index + 1] ?? state.tabs[index - 1];

  return (
    <>
      <TitleBar state={state} />
      <Toolbar tab={state.tabs[index]} split={state.split.secondaryId !== null} neighbourId={neighbour?.id} />
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
        {state.tabs.map(tab => (
          <TabItem
            key={tab.id}
            tab={tab}
            active={tab.id === state.activeId}
            secondary={tab.id === state.split.secondaryId}
          />
        ))}
        <button className="icon new-tab" aria-label="Nouvel onglet" onClick={() => send({ type: 'tab.open' })}>
          <Icon d="M8 3v10M3 8h10" />
        </button>
      </div>
    </header>
  );
}

function TabItem({ tab, active, secondary }: { tab: Tab; active: boolean; secondary: boolean }) {
  const label = tab.title && tab.title !== 'about:blank' ? tab.title : 'Nouvel onglet';
  return (
    <div
      role="tab"
      aria-selected={active}
      className={`tab${active ? ' active' : ''}${secondary ? ' secondary' : ''}`}
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

function Toolbar({ tab, split, neighbourId }: { tab: Tab | undefined; split: boolean; neighbourId: string | undefined }) {
  const url = !tab || tab.url === 'about:blank' ? '' : tab.url;
  const [text, setText] = useState(url);
  const [editing, setEditing] = useState(false);

  // L'adresse suit la page tant que l'utilisateur n'est pas en train de taper.
  useEffect(() => {
    if (!editing) setText(url);
  }, [url, editing, tab?.id]);

  const id = tab?.id;
  return (
    <nav className="toolbar">
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
          onChange={e => setText(e.target.value)}
          onFocus={e => {
            setEditing(true);
            e.target.select();
          }}
          onBlur={() => setEditing(false)}
        />
      </form>
      <button
        className={`icon${split ? ' on' : ''}`}
        aria-label={split ? 'Fermer la vue scindée' : 'Afficher deux pages côte à côte'}
        aria-pressed={split}
        title={split ? 'Fermer la vue scindée' : 'Afficher deux pages côte à côte'}
        disabled={!split && !neighbourId}
        onClick={() => send(split ? { type: 'split.close' } : { type: 'split.open', id: neighbourId })}
      >
        <Icon d="M2 3h12v10H2zM8 3v10" />
      </button>
    </nav>
  );
}

function Icon({ d }: { d: string }) {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <path d={d} />
    </svg>
  );
}
