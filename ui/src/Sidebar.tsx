import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { onState, send, type State, type Tab } from './bridge';
import { CopyIcon, GoogleLogo, Icon, LockIcon, XLogo, YoutubeLogo } from './Icons';
import { startSlotDrag, useVerticalDrag } from './useVerticalDrag';

type Icons = Record<string, string | null | undefined>;

// Décalage de la vue Sidebar dans MainWindow.xaml (colonne gauche sous la barre de 52px).
const OFFSET_X = 0;
const OFFSET_Y = 52;

export default function Sidebar() {
  const [state, setState] = useState<State | null>(null);
  const [icons, setIcons] = useState<Icons>({});
  const [copied, setCopied] = useState(false);
  const [status, setStatus] = useState(''); // adresse du lien survolé dans la page
  const [motion, setMotion] = useState<{ way: '' | 'entering' | 'leaving'; run: number }>({ way: '', run: 0 });
  const [urlText, setUrlText] = useState('');
  const [editingUrl, setEditingUrl] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const off = onState(setState, e => {
      if (e.event === 'favicon' && e.id) setIcons(all => ({ ...all, [e.id!]: e.data }));
      if (e.event === 'status') setStatus(e.text ?? '');
      // L'hôte fait entrer ou sortir la carte : le mouvement est joué ici, par son contenu.
      // Flottante (au survol, ou le temps d'une animation) : la vue se réduit à la carte, sans marge autour.
      if (e.event === 'sidebar-enter' || e.event === 'sidebar-leave') document.body.classList.add('floating');
      if (e.event === 'sidebar-docked') document.body.classList.remove('floating');
      if (e.event === 'sidebar-enter') setMotion(m => ({ way: 'entering', run: m.run + 1 }));
      if (e.event === 'sidebar-leave') setMotion(m => ({ way: 'leaving', run: m.run + 1 }));
    });
    send({ type: 'ui.ready' });
    return off;
  }, []);

  const activeTab = state?.tabs.find(t => t.id === state.activeId);
  const activeUrl = !activeTab || activeTab.url === 'about:blank' ? '' : activeTab.url;

  useEffect(() => {
    if (!editingUrl) setUrlText(activeUrl);
  }, [activeUrl, editingUrl]);

  useVerticalDrag();

  if (!state) return null;
  const pinnedTabs = state.tabs.filter(t => t.pinned);
  const { tabs, split, activeId } = state;

  const copyUrl = () => {
    if (!activeUrl) return;
    navigator.clipboard?.writeText(activeUrl).catch(() => {});
    setCopied(true);
    setTimeout(() => setCopied(false), 1200);
  };

  // Lignes des onglets de rang « from » à « to » (avec les paires d'une vue scindée).
  const rows = (from: number, to: number) => {
    const items: ReactNode[] = [];
    for (let i = from; i <= to; i++) {
      const tab = tabs[i], next = i < to ? tabs[i + 1] : undefined;
      if (next && tab.id === split.primaryId && next.id === split.secondaryId) {
        items.push(
          <div key={tab.id} className={`sidebar-split-group${split.visible ? ' active' : ''}`} data-slot-id={tab.id} data-slot-first={i} data-slot-last={i + 1}>
            <SidebarTabRow tab={tab} index={i} icon={icons[tab.id]} active={tab.id === activeId} grouped />
            <SidebarTabRow tab={next} index={i + 1} icon={icons[next.id]} active={next.id === activeId} grouped />
          </div>,
        );
        i++;
      } else {
        items.push(<SidebarTabRow key={tab.id} tab={tab} index={i} icon={icons[tab.id]} active={tab.id === activeId} />);
      }
    }
    return items;
  };

  // Liste verticale des onglets non épinglés. Les onglets d'un groupe, toujours voisins, sont réunis sous
  // l'en-tête du groupe : un clic le replie ou le déplie, un clic droit ouvre son menu.
  const normalItems: ReactNode[] = [];
  for (let i = 0; i < tabs.length; i++) {
    if (tabs[i].pinned) continue;
    let end = i;
    const group = state.groups.find(g => g.id === tabs[i].groupId);
    if (!group) {
      if (tabs[i].id === split.primaryId && tabs[i + 1]?.id === split.secondaryId) end = i + 1;
      normalItems.push(...rows(i, end));
    } else {
      while (tabs[end + 1]?.groupId === group.id) end++;
      normalItems.push(
        <div key={group.id} className={`tab-group-block${group.collapsed ? ' collapsed' : ''}`} style={{ '--group': group.color } as CSSProperties}>
          <button
            className="tab-group-header"
            aria-expanded={!group.collapsed}
            title={group.collapsed ? `Déplier « ${group.name} »` : `Replier « ${group.name} »`}
            onClick={() => send({ type: 'group.toggle', id: group.id })}
            onContextMenu={e => {
              e.preventDefault();
              send({ type: 'group.menu', id: group.id, x: e.clientX + OFFSET_X, y: e.clientY + OFFSET_Y });
            }}
          >
            {group.icon ? <span className="tab-group-icon" aria-hidden>{group.icon}</span> : <span className="tab-group-dot" aria-hidden />}
            <span className="tab-group-name">{group.name}</span>
            <span className="tab-group-count">{end - i + 1}</span>
            <span className="tab-group-chevron" aria-hidden>
              <Icon d="M6 4l4 4-4 4" size={12} />
            </span>
          </button>
          {!group.collapsed && <div className="tab-group-tabs">{rows(i, end)}</div>}
        </div>,
      );
    }
    i = end;
  }

  return (
    <aside key={motion.run} className={`sidebar-card ${motion.way}`} aria-label="Panneau latéral des onglets">
      <div className="sidebar-url-pill">
        <LockIcon />
        <form
          className="sidebar-url-form"
          onSubmit={e => {
            e.preventDefault();
            if (activeId && urlText.trim()) send({ type: 'nav.go', id: activeId, input: urlText });
            inputRef.current?.blur();
          }}
        >
          <input
            ref={inputRef}
            aria-label="Adresse complète"
            placeholder="https://..."
            spellCheck={false}
            value={urlText}
            onChange={e => setUrlText(e.target.value)}
            onFocus={e => {
              setEditingUrl(true);
              e.target.select();
            }}
            onKeyDown={e => {
              if (e.key === 'Escape') {
                setUrlText(activeUrl);
                e.currentTarget.blur();
              }
            }}
            onBlur={() => setEditingUrl(false)}
          />
        </form>
        <button
          className={`sidebar-url-btn${copied ? ' copied' : ''}`}
          aria-label="Copier l'adresse"
          title={copied ? 'Adresse copiée' : "Copier l'adresse"}
          onClick={copyUrl}
        >
          {copied ? <Icon d="M3.5 8.5l3 3 6-6.5" size={14} /> : <CopyIcon />}
        </button>
        <button
          className="sidebar-url-btn"
          aria-label="Effacer ou saisir une adresse"
          title="Saisir une nouvelle adresse"
          onClick={() => {
            setEditingUrl(true);
            setUrlText('');
            inputRef.current?.focus();
          }}
        >
          <Icon d="M4.5 4.5l7 7M11.5 4.5l-7 7" size={13} />
        </button>
      </div>

      <PinnedStrip pinnedTabs={pinnedTabs} icons={icons} activeId={activeId} />

      <div className="sidebar-tabs" role="tablist" aria-orientation="vertical">
        {normalItems}
        <button className="sidebar-new-tab" onClick={() => send({ type: 'tab.open' })}>
          <Icon d="M8 3.5v9M3.5 8h9" size={14} />
          <span>Nouvel onglet</span>
        </button>
      </div>

      {/* Où mène le lien survolé : repère contre l'hameçonnage, à la place de la bulle noire du moteur. */}
      <div className={`sidebar-status${status ? ' shown' : ''}`} aria-live="off" title={status}>
        <Icon d="M6.5 9.5l3-3M7 4.5l1-1a2.5 2.5 0 0 1 3.5 3.5l-1 1M9 11.5l-1 1A2.5 2.5 0 0 1 4.5 9l1-1" size={12} />
        <span>{status.replace(/^https?:\/\/(www\.)?/, '')}</span>
      </div>
    </aside>
  );
}

// Rangée de pilules épinglées (ou raccourcis Google / X / Youtube par défaut comme sur la maquette).
function PinnedStrip({ pinnedTabs, icons, activeId }: { pinnedTabs: Tab[]; icons: Icons; activeId: string | null }) {
  if (pinnedTabs.length === 0) {
    const go = (url: string, middle: boolean) =>
      middle || !activeId ? send({ type: 'tab.open', url }) : send({ type: 'nav.go', id: activeId, input: url });
    return (
      <div className="pinned-row" aria-label="Accès rapides">
        <button className="pinned-pill circle" title="Google" onMouseDown={e => e.button <= 1 && go('https://www.google.com', e.button === 1)}>
          <GoogleLogo />
        </button>
        <button className="pinned-pill circle" title="X" onMouseDown={e => e.button <= 1 && go('https://x.com', e.button === 1)}>
          <XLogo />
        </button>
        <button className="pinned-pill wide" title="YouTube" onMouseDown={e => e.button <= 1 && go('https://www.youtube.com', e.button === 1)}>
          <YoutubeLogo />
          <span>Youtube</span>
        </button>
      </div>
    );
  }

  return (
    <div className="pinned-row" aria-label="Onglets épinglés">
      {pinnedTabs.map((tab, idx) => {
        const wide = idx === pinnedTabs.length - 1 && pinnedTabs.length <= 3;
        const label = tab.title && tab.title !== 'about:blank' ? tab.title.split(' - ')[0].split(' · ')[0] : 'Épinglé';
        const icon = icons[tab.id];
        return (
          <button
            key={tab.id}
            className={`pinned-pill ${wide ? 'wide' : 'circle'}${tab.id === activeId ? ' active' : ''}`}
            title={tab.title || tab.url}
            onContextMenu={e => {
              e.preventDefault();
              send({ type: 'tab.menu', id: tab.id, x: e.clientX + OFFSET_X, y: e.clientY + OFFSET_Y });
            }}
            onMouseDown={e => {
              if (e.button === 0) send({ type: 'tab.activate', id: tab.id });
              if (e.button === 1) send({ type: 'tab.close', id: tab.id });
            }}
          >
            {icon ? <img className="favicon" src={icon} alt="" draggable={false} /> : <span className="favicon letter">{label[0]?.toUpperCase()}</span>}
            {wide && <span>{label}</span>}
          </button>
        );
      })}
    </div>
  );
}

function SidebarTabRow({ tab, index, icon, active, grouped = false }: { tab: Tab; index: number; icon: string | null | undefined; active: boolean; grouped?: boolean }) {
  const label = tab.title && tab.title !== 'about:blank' ? tab.title : 'Nouvel onglet';
  const slot = grouped ? {} : { 'data-slot-id': tab.id, 'data-slot-first': index, 'data-slot-last': index };

  return (
    <div
      role="tab"
      aria-selected={active}
      className={`sidebar-tab${active ? ' active' : ''}`}
      title={label}
      {...slot}
      onContextMenu={e => {
        e.preventDefault();
        send({ type: 'tab.menu', id: tab.id, x: e.clientX + OFFSET_X, y: e.clientY + OFFSET_Y });
      }}
      onMouseDown={e => {
        if (e.button === 0) {
          send({ type: 'tab.activate', id: tab.id });
          startSlotDrag(e.currentTarget.closest<HTMLElement>('[data-slot-id]'), e.clientY);
        }
        if (e.button === 1) send({ type: 'tab.close', id: tab.id });
      }}
    >
      {tab.loading ? (
        <span className="spinner" aria-hidden />
      ) : icon ? (
        <img className="favicon" src={icon} alt="" draggable={false} />
      ) : (
        <span className="favicon letter" aria-hidden>{label[0]?.toUpperCase()}</span>
      )}
      <span className="sidebar-tab-title">{label}</span>
      <button
        className="sidebar-tab-close"
        aria-label={`Fermer ${label}`}
        onMouseDown={e => e.stopPropagation()}
        onClick={e => {
          e.stopPropagation();
          send({ type: 'tab.close', id: tab.id });
        }}
      >
        <Icon d="M4.5 4.5l7 7M11.5 4.5l-7 7" size={12} />
      </button>
    </div>
  );
}
