import { useEffect, useState, type MouseEvent, type ReactNode } from 'react';
import { onMessage, onState, send, type State, type Tab } from './bridge';

export default function App() {
  const [state, setState] = useState<State | null>(null);
  const [icons, setIcons] = useState<Record<string, string | null | undefined>>({});

  useEffect(() => {
    const off = onState(setState, e => {
      if (e.event === 'focus-address') document.querySelector<HTMLInputElement>('.pane.focused input, .pane input')?.focus();
      if (e.event === 'favicon' && e.id) setIcons(all => ({ ...all, [e.id!]: e.data }));
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
      <TitleBar state={state} icons={icons} />
      <nav className="toolbar">
        {panes.map((tab, i) => (
          <Pane
            key={tab?.id ?? i}
            tab={tab}
            focused={tab?.id === state.activeId}
            grow={!split.visible ? 1 : i === 0 ? split.ratio : 1 - split.ratio}
          />
        ))}
        {state.downloads.count > 0 && (
          <button
            className={`icon${state.downloads.active > 0 ? ' on' : ''}`}
            aria-label={state.downloads.active > 0 ? 'Téléchargements (en cours)' : 'Téléchargements'}
            title="Téléchargements"
            onClick={e => {
              const box = e.currentTarget.getBoundingClientRect();
              send({ type: 'downloads.open', x: box.right, y: box.bottom + 6 });
            }}
          >
            <Icon d="M8 2.5v8M4.5 7.5L8 11l3.5-3.5M3 13.5h10" />
          </button>
        )}
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

type Icons = Record<string, string | null | undefined>;

// Onglet en cours de glisser-déposer (un seul à la fois).
let draggedTab: string | null = null;

function TitleBar({ state, icons }: { state: State; icons: Icons }) {
  // Seul le fond de la barre déplace la fenêtre, pas les onglets ni les boutons.
  const drag = (e: MouseEvent) => {
    if (e.button === 0 && e.target === e.currentTarget) send({ type: 'window.drag' });
  };
  const maximize = (e: MouseEvent) => {
    if (e.target === e.currentTarget) send({ type: 'window.maximize' });
  };

  // Réordonner : tant que le bouton reste enfoncé, l'onglet saisi prend la place de celui dont la souris
  // dépasse le milieu. (Le glisser-déposer HTML ne fonctionne pas dans ce type de vue.)
  useEffect(() => {
    const move = (e: globalThis.MouseEvent) => {
      if (!draggedTab) return;
      if (e.buttons !== 1) return void (draggedTab = null);
      const all = [...document.querySelectorAll<HTMLElement>('[data-tab-id]')];
      const mine = all.find(el => el.dataset.tabId === draggedTab);
      if (!mine) return void (draggedTab = null);
      const from = Number(mine.dataset.tabIndex);
      for (const el of all) {
        const to = Number(el.dataset.tabIndex);
        const box = el.getBoundingClientRect();
        const middle = box.left + box.width / 2;
        const crossed = to > from ? e.clientX > middle && e.clientX < box.right + 40 : e.clientX < middle && e.clientX > box.left - 40;
        if (to !== from && crossed) return void send({ type: 'tab.move', id: draggedTab, index: to });
      }
    };
    const stop = () => (draggedTab = null);
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', stop);
    return () => {
      window.removeEventListener('mousemove', move);
      window.removeEventListener('mouseup', stop);
    };
  }, []);

  const { tabs, split, activeId } = state;
  const items: ReactNode[] = [];
  for (let i = 0; i < tabs.length; i++) {
    const tab = tabs[i], next = tabs[i + 1];
    // Les deux onglets d'une vue scindée s'affichent comme un seul onglet en deux moitiés.
    if (next && tab.id === split.primaryId && next.id === split.secondaryId) {
      items.push(
        <div key={tab.id} className={`tab-group${split.visible ? ' active' : ''}`}>
          <TabItem tab={tab} index={i} icon={icons[tab.id]} active={tab.id === activeId} />
          <TabItem tab={next} index={i + 1} icon={icons[next.id]} active={next.id === activeId} />
        </div>,
      );
      i++;
    } else {
      items.push(<TabItem key={tab.id} tab={tab} index={i} icon={icons[tab.id]} active={tab.id === activeId} />);
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

function TabItem({ tab, index, icon, active }: { tab: Tab; index: number; icon: string | null | undefined; active: boolean }) {
  const label = tab.title && tab.title !== 'about:blank' ? tab.title : 'Nouvel onglet';
  return (
    <div
      role="tab"
      aria-selected={active}
      className={`tab${active ? ' active' : ''}`}
      title={label}
      data-tab-id={tab.id}
      data-tab-index={index}
      onMouseDown={e => {
        if (e.button === 0) {
          send({ type: 'tab.activate', id: tab.id });
          draggedTab = tab.id; // le glisser est suivi dans TitleBar
        }
        if (e.button === 1) send({ type: 'tab.close', id: tab.id });
      }}
    >
      {tab.loading ? <span className="spinner" aria-hidden /> : icon ? <img className="favicon" src={icon} alt="" draggable={false} /> : null}
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
  // Suggestions affichées pour ce champ : leur nombre, et celle surlignée au clavier (-1 = aucune).
  const [count, setCount] = useState(0);
  const [selected, setSelected] = useState(-1);

  // L'adresse suit la page tant que l'utilisateur n'est pas en train de taper.
  useEffect(() => {
    if (!editing) setText(url);
  }, [url, editing]);

  const id = tab?.id;
  useEffect(
    () =>
      onMessage(data => {
        const e = data as { event?: string; id?: string; count?: number };
        if (e.event !== 'suggestions' || e.id !== id) return;
        setCount(e.count ?? 0);
        setSelected(-1);
      }),
    [id],
  );

  const move = (step: number) => {
    if (count === 0) return;
    // De -1 (le champ) à count-1, en boucle.
    const next = ((selected + 1 + step + count + 1) % (count + 1)) - 1;
    setSelected(next);
    send({ type: 'suggest.select', index: next });
  };
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
          if (id && selected >= 0) send({ type: 'suggest.pick', index: selected });
          else if (id && text.trim()) send({ type: 'nav.go', id, input: text });
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
          onKeyDown={e => {
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
              e.preventDefault();
              move(e.key === 'ArrowDown' ? 1 : -1);
            } else if (e.key === 'Escape') {
              setText(url);
              e.currentTarget.blur();
            }
          }}
          onBlur={() => {
            setEditing(false);
            setCount(0);
            setSelected(-1);
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
