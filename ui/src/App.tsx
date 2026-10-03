import { useEffect, useLayoutEffect, useRef, useState, type MouseEvent, type ReactNode } from 'react';
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

// Glisser d'onglet en cours : l'onglet suit la souris, les autres glissent pour lui faire place.
// grab : où l'onglet a été saisi ; x : dernière position de la souris ; pending : place demandée à l'hôte.
let drag: { id: string; grab: number; start: number; x: number; pending: number; moved: boolean } | null = null;

const SLIDE = 'transform 160ms cubic-bezier(0.2, 0, 0, 1)';
// Une « place » de la barre : un onglet seul, ou les deux onglets d'une vue scindée, qui se déplacent ensemble.
// data-slot-first / data-slot-last : rangs du premier et du dernier onglet de la place.
const tabElements = () => [...document.querySelectorAll<HTMLElement>('[data-slot-id]')];

// Fait glisser un onglet jusqu'à sa place, puis rend la main aux transitions de la feuille de style.
function slideHome(el: HTMLElement) {
  el.style.transition = SLIDE;
  el.style.transform = '';
  el.addEventListener('transitionend', () => (el.style.transition = ''), { once: true });
}

// Place l'onglet saisi sous la souris. Les positions sont celles de la mise en page (offsetLeft),
// que les transformations en cours ne modifient pas.
function followMouse() {
  const mine = drag && tabElements().find(el => el.dataset.slotId === drag!.id);
  const strip = mine?.offsetParent as HTMLElement | null;
  if (!drag || !mine || !strip) return null;
  const left = Math.max(0, Math.min(drag.x - drag.grab - strip.getBoundingClientRect().left, strip.clientWidth - mine.offsetWidth));
  mine.style.transition = 'none';
  mine.style.transform = `translateX(${left - mine.offsetLeft}px)`;
  mine.classList.add('dragging');
  return { mine, left };
}

function TitleBar({ state, icons }: { state: State; icons: Icons }) {
  // Seul le fond de la barre déplace la fenêtre, pas les onglets ni les boutons.
  const dragWindow = (e: MouseEvent) => {
    if (e.button === 0 && e.target === e.currentTarget) send({ type: 'window.drag' });
  };
  const maximize = (e: MouseEvent) => {
    if (e.target === e.currentTarget) send({ type: 'window.maximize' });
  };

  // Réordonner : l'onglet saisi suit la souris en continu ; il change de place dans la liste quand son
  // centre dépasse le milieu d'un voisin. (Le glisser-déposer HTML ne fonctionne pas dans ce type de vue.)
  useEffect(() => {
    const stop = () => {
      const mine = drag && tabElements().find(el => el.dataset.slotId === drag!.id);
      drag = null;
      if (!mine) return;
      slideHome(mine);
      mine.classList.remove('dragging');
    };
    const move = (e: globalThis.MouseEvent) => {
      if (!drag) return;
      if (!(e.buttons & 1)) return stop();
      // Un simple clic ne déclenche pas de glisser : il faut bouger de quelques pixels.
      if (!drag.moved && Math.abs(e.clientX - drag.start) < 5) return;
      drag.moved = true;
      drag.x = e.clientX;
      const placed = followMouse();
      if (!placed) return void (drag = null);

      const { mine, left } = placed;
      const from = Number(mine.dataset.slotFirst);
      if (drag.pending !== -1) return; // une demande est en route : on attend la réponse de l'hôte
      const center = left + mine.offsetWidth / 2;
      // Rang visé : après le dernier onglet d'une place dépassée vers la droite, avant le premier vers la gauche.
      // Pour une vue scindée, l'hôte déplace le premier onglet puis ramène le second à sa suite.
      let target = from;
      for (const el of tabElements()) {
        if (el === mine) continue;
        const first = Number(el.dataset.slotFirst), last = Number(el.dataset.slotLast);
        const middle = el.offsetLeft + el.offsetWidth / 2;
        if (first > from && center > middle) target = Math.max(target, last);
        if (first < from && center < middle) target = Math.min(target, first);
      }
      if (target !== from) {
        drag.pending = target;
        send({ type: 'tab.move', id: drag.id, index: target });
      }
    };
    // Fin du glisser : bouton relâché, ou souris sortie de la barre (le relâchement n'arriverait pas ici).
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', stop);
    document.documentElement.addEventListener('mouseleave', stop);
    window.addEventListener('blur', stop);
    return () => {
      window.removeEventListener('mousemove', move);
      window.removeEventListener('mouseup', stop);
      document.documentElement.removeEventListener('mouseleave', stop);
      window.removeEventListener('blur', stop);
    };
  }, []);

  // Après chaque nouvel ordre reçu de l'hôte : l'onglet saisi reste sous la souris, et ceux qui ont changé
  // de place y glissent depuis leur ancienne position au lieu de sauter.
  const lefts = useRef(new Map<string, number>());
  useLayoutEffect(() => {
    if (drag) drag.pending = -1;
    const still = matchMedia('(prefers-reduced-motion: reduce)').matches;
    for (const el of tabElements()) {
      const id = el.dataset.slotId!;
      const before = lefts.current.get(id);
      const now = el.offsetLeft;
      lefts.current.set(id, now);
      if (drag?.moved && id === drag.id) followMouse();
      else if (drag?.moved && !still && before !== undefined && before !== now) {
        el.style.transition = 'none';
        el.style.transform = `translateX(${before - now}px)`;
        el.getBoundingClientRect(); // applique la position de départ avant de lancer la transition
        slideHome(el);
      }
    }
  });

  const { tabs, split, activeId } = state;
  const items: ReactNode[] = [];
  for (let i = 0; i < tabs.length; i++) {
    const tab = tabs[i], next = tabs[i + 1];
    // Les deux onglets d'une vue scindée s'affichent comme un seul onglet en deux moitiés.
    if (next && tab.id === split.primaryId && next.id === split.secondaryId) {
      items.push(
        <div key={tab.id} className={`tab-group${split.visible ? ' active' : ''}`} data-slot-id={tab.id} data-slot-first={i} data-slot-last={i + 1}>
          <TabItem tab={tab} index={i} icon={icons[tab.id]} active={tab.id === activeId} grouped />
          <TabItem tab={next} index={i + 1} icon={icons[next.id]} active={next.id === activeId} grouped />
        </div>,
      );
      i++;
    } else {
      items.push(<TabItem key={tab.id} tab={tab} index={i} icon={icons[tab.id]} active={tab.id === activeId} />);
    }
  }

  return (
    <header className="titlebar" onMouseDown={dragWindow} onDoubleClick={maximize}>
      <div className="lights">
        <button className="light close" aria-label="Fermer" onClick={() => send({ type: 'window.close' })} />
        <button className="light minimize" aria-label="Réduire" onClick={() => send({ type: 'window.minimize' })} />
        <button
          className="light maximize"
          aria-label={state.maximized ? 'Restaurer' : 'Agrandir'}
          onClick={() => send({ type: 'window.maximize' })}
        />
      </div>
      <div className="tabs" role="tablist" onMouseDown={dragWindow} onDoubleClick={maximize}>
        {items}
        <button className="icon new-tab" aria-label="Nouvel onglet" onClick={() => send({ type: 'tab.open' })}>
          <Icon d="M8 3v10M3 8h10" />
        </button>
      </div>
    </header>
  );
}

function TabItem({ tab, index, icon, active, grouped = false }: { tab: Tab; index: number; icon: string | null | undefined; active: boolean; grouped?: boolean }) {
  const label = tab.title && tab.title !== 'about:blank' ? tab.title : 'Nouvel onglet';
  // Un onglet seul est une place à lui seul ; dans une vue scindée, la place est le groupe qui l'entoure.
  const slot = grouped ? {} : { 'data-slot-id': tab.id, 'data-slot-first': index, 'data-slot-last': index };
  return (
    <div
      role="tab"
      aria-selected={active}
      className={`tab${active ? ' active' : ''}`}
      title={label}
      {...slot}
      onMouseDown={e => {
        if (e.button === 0) {
          send({ type: 'tab.activate', id: tab.id });
          // Le glisser est suivi dans TitleBar ; saisir une moitié de vue scindée saisit les deux.
          const el = e.currentTarget.closest<HTMLElement>('[data-slot-id]');
          drag = el && { id: el.dataset.slotId!, grab: e.clientX - el.getBoundingClientRect().left, start: e.clientX, x: e.clientX, pending: -1, moved: false };
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
