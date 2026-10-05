import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { interfaceScale, isStartPage, formatDomain, onState, send, type State, type Tab } from './bridge';
import { t, useLang } from './i18n';
import { CopyIcon, Icon, LockIcon } from './Icons';
import { startSlotDrag, useVerticalDrag } from './useVerticalDrag';
import logo from './assets/logo.png';

type Icons = Record<string, string | null | undefined>;

// Décalage de la vue Sidebar dans MainWindow.xaml (colonne gauche sous la barre de 52px).
const OFFSET_X = 0;
const OFFSET_Y = () => 52 * interfaceScale();

export default function Sidebar() {
  const [state, setState] = useState<State | null>(null);
  const [icons, setIcons] = useState<Icons>({});
  const [copied, setCopied] = useState(false);
  const [status, setStatus] = useState(''); // adresse du lien survolé dans la page
  const [motion, setMotion] = useState<{ way: '' | 'entering' | 'leaving'; run: number }>({ way: '', run: 0 });
  const [urlText, setUrlText] = useState('');
  const [editingUrl, setEditingUrl] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  useLang();

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
  const activeUrl = !activeTab || (activeTab.url === 'about:blank' || isStartPage(activeTab.url)) ? '' : activeTab.url;

  useEffect(() => {
    if (!editingUrl) setUrlText(activeUrl);
  }, [activeUrl, editingUrl]);

  useVerticalDrag();

  if (!state) return null;
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
            title={group.collapsed ? t(`Déplier « ${group.name} »`, `Expand “${group.name}”`) : t(`Replier « ${group.name} »`, `Collapse “${group.name}”`)}
            onClick={() => send({ type: 'group.toggle', id: group.id })}
            onContextMenu={e => {
              e.preventDefault();
              send({ type: 'group.menu', id: group.id, x: e.clientX + OFFSET_X, y: e.clientY + OFFSET_Y() });
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
    <aside className={`sidebar-card ${motion.way}`} onAnimationEnd={() => setMotion(m => ({ ...m, way: '' }))} aria-label={t('Panneau latéral des onglets', 'Tabs sidebar')}>
      <div className="sidebar-heading">
        <button className="sidebar-profile" title={t(`Profil : ${state.profile}`, `Profile: ${state.profile}`)} onClick={e => {
          const box = e.currentTarget.getBoundingClientRect();
          send({ type: 'profile.menu', x: box.right + OFFSET_X, y: box.bottom + OFFSET_Y() + 6 });
        }}>
          <Icon d="M8 8a2.6 2.6 0 1 0 0-5.2A2.6 2.6 0 0 0 8 8M3 13.2c.6-2.2 2.6-3.4 5-3.4s4.4 1.2 5 3.4" size={14} />
          <span>{state.privateMode ? t('Privé', 'Private') : state.profile}</span>
        </button>
        <span className="sidebar-brand"><img src={logo} alt="" /><strong>Sol</strong></span>
      </div>
      <div className="sidebar-url-pill" onClick={e => {
        if (!(e.target as Element).closest('button')) inputRef.current?.focus();
      }}>
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
            aria-label={t('Adresse ou recherche', 'Address or search')}
            placeholder={t('Rechercher ou saisir une adresse', 'Search or enter an address')}
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
          aria-label={t("Copier l'adresse", 'Copy address')}
          title={copied ? t('Adresse copiée', 'Address copied') : t("Copier l'adresse", 'Copy address')}
          onClick={copyUrl}
        >
          {copied ? <Icon d="M3.5 8.5l3 3 6-6.5" size={14} /> : <CopyIcon />}
        </button>
        <button
          className="sidebar-url-btn"
          aria-label={t('Effacer ou saisir une adresse', 'Clear or enter an address')}
          title={t('Saisir une nouvelle adresse', 'Enter a new address')}
          onClick={() => {
            setEditingUrl(true);
            setUrlText('');
            inputRef.current?.focus();
          }}
        >
          <Icon d="M4.5 4.5l7 7M11.5 4.5l-7 7" size={13} />
        </button>
      </div>

      <PinnedStrip shortcuts={state.shortcuts} tabs={tabs} icons={icons} activeId={activeId} />

      <div className="sidebar-tabs" role="tablist" aria-orientation="vertical">
        {normalItems}
        <button className="sidebar-new-tab" onClick={() => send({ type: 'tab.open' })}>
          <Icon d="M8 3.5v9M3.5 8h9" size={14} />
          <span>{t('Nouvel onglet', 'New tab')}</span>
        </button>
      </div>

      <div className="sidebar-quick-actions" role="group" aria-label={t('Actions rapides', 'Quick actions')}>
        {[
          { type: 'window.private', label: t('Navigation privée', 'Private browsing'), path: 'M6.5 8a2.5 2.5 0 1 1-5 0 2.5 2.5 0 0 1 5 0M14.5 8a2.5 2.5 0 1 1-5 0 2.5 2.5 0 0 1 5 0M6.5 8h3' },
          { type: 'history.open', label: t('Historique', 'History'), path: 'M2 7a6 6 0 1 1 1.5 5M2 3v4h4M8 4.5V8l2.5 1.5' },
          { type: 'downloads.open', label: t('Téléchargements', 'Downloads'), path: 'M8 2.5v8M4.5 7.5L8 11l3.5-3.5M3 13.5h10' },
        ].map(action => <button key={action.type} className={`sidebar-quick-btn${action.type === 'window.private' ? ' private-action' : ''}`} aria-label={action.label} title={action.label} onClick={e => {
          const box = e.currentTarget.getBoundingClientRect();
          send({ type: action.type, x: box.right + OFFSET_X, y: box.bottom + OFFSET_Y() + 6 });
        }}><Icon d={action.path} size={16} /></button>)}
      </div>

      {/* Où mène le lien survolé : repère contre l'hameçonnage, à la place de la bulle noire du moteur. */}
      <div className={`sidebar-status${status ? ' shown' : ''}`} aria-live="off" title={status}>
        <Icon d="M6.5 9.5l3-3M7 4.5l1-1a2.5 2.5 0 0 1 3.5 3.5l-1 1M9 11.5l-1 1A2.5 2.5 0 0 1 4.5 9l1-1" size={12} />
        <span>{status.replace(/^https?:\/\/(www\.)?/, '')}</span>
      </div>
    </aside>
  );
}

const GAP = 8; // écart entre deux pilules (sidebar.css)
const NAMED_PILL = 104; // largeur à partir de laquelle une pilule affiche son nom lisiblement

// Deux rangées de pilules : les raccourcis (modifiables et retirables par clic droit), puis les onglets épinglés.
function PinnedStrip({ shortcuts, tabs, icons, activeId }: { shortcuts: State['shortcuts']; tabs: Tab[]; icons: Icons; activeId: string | null }) {
  const pinnedTabs = tabs.filter(tab => tab.pinned && !shortcuts.some(s => s.tabId === tab.id));

  // Les pilules suivent la largeur de la carte, que l'utilisateur règle en tirant son bord :
  // - assez de place pour tous les noms : chaque pilule affiche le sien, à largeur égale ;
  // - sinon, des ronds, et le nom sur la dernière pilule d'une rangée de trois au plus ;
  // - une rangée de ronds seuls se partage toute la largeur.
  const strip = useRef<HTMLDivElement>(null);
  const [room, setRoom] = useState(0);
  useEffect(() => {
    const el = strip.current;
    if (!el) return;
    const observer = new ResizeObserver(() => setRoom(el.clientWidth));
    observer.observe(el);
    return () => observer.disconnect();
  }, []);
  const named = (count: number) => count > 0 && (room - GAP * (count - 1)) / count >= NAMED_PILL;
  const wideAt = (index: number, count: number) => named(count) || (index === count - 1 && count <= 3);
  const rowClass = (count: number) => `pinned-row${!named(count) && count > 3 ? ' even' : ''}`;

  return (
    <div ref={strip} className="pinned-strip">
      {shortcuts.length > 0 && (
        <div className={rowClass(shortcuts.length)} aria-label={t('Accès rapides', 'Shortcuts')}>
          {shortcuts.map((shortcut, index) => (
            <ShortcutPill
              key={`${index}-${shortcut.url}`}
              shortcut={shortcut}
              index={index}
              wide={wideAt(index, shortcuts.length)}
              active={!!shortcut.tabId && shortcut.tabId === activeId}
            />
          ))}
        </div>
      )}
      {pinnedTabs.length > 0 && (
        <div className={rowClass(pinnedTabs.length)} aria-label={t('Onglets épinglés', 'Pinned tabs')}>
          {pinnedTabs.map((tab, idx) => {
            const wide = wideAt(idx, pinnedTabs.length);
            const label = tab.title && tab.title !== 'about:blank' ? tab.title.split(' - ')[0].split(' · ')[0] : t('Épinglé', 'Pinned');
            const icon = icons[tab.id];
            return (
              <button
                key={tab.id}
                className={`pinned-pill ${wide ? 'wide' : 'circle'}${tab.id === activeId ? ' active' : ''}${tab.asleep ? ' asleep' : ''}`}
                title={tab.title || tab.url}
                onContextMenu={e => {
                  e.preventDefault();
                  send({ type: 'tab.menu', id: tab.id, x: e.clientX + OFFSET_X, y: e.clientY + OFFSET_Y() });
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
      )}
    </div>
  );
}

// Pilule d'un raccourci. Sa page ouverte, son contour prend la couleur du site (lue dans son icône) : léger tant
// qu'elle est en arrière-plan, franc quand c'est la page affichée.
function ShortcutPill({ shortcut, index, wide, active }: { shortcut: State['shortcuts'][number]; index: number; wide: boolean; active: boolean }) {
  const { name, url, icon, tabId } = shortcut;
  const label = name || formatDomain(url);
  const look = useIconLook(icon);
  return (
    <button
      className={`pinned-pill ${wide ? 'wide' : 'circle'}${active ? ' active' : ''}${tabId ? ' loaded' : ''}`}
      style={look.color ? ({ '--site': look.color } as CSSProperties) : undefined}
      title={label}
      onContextMenu={e => {
        e.preventDefault();
        send({ type: 'shortcut.menu', index, x: e.clientX + OFFSET_X, y: e.clientY + OFFSET_Y() });
      }}
      onMouseDown={e => {
        // La page s'ouvre dans le raccourci lui-même, sans ligne dans la liste des onglets. Clic du milieu : elle se ferme.
        if (e.button === 0) send({ type: 'shortcut.open', index });
        if (e.button === 1 && tabId) send({ type: 'tab.close', id: tabId });
      }}
    >
      {icon ? <img className={`favicon${look.tone}`} src={icon} alt="" draggable={false} /> : <span className="favicon letter">{label[0]?.toUpperCase()}</span>}
      {wide && <span>{label}</span>}
    </button>
  );
}

// Ce que l'icône d'un site dit de lui, mesuré une fois :
// - sa couleur : la moyenne de ses pixels colorés (aucune pour une icône grise, noire ou blanche) ;
// - son ton : une icône toute sombre est illisible sur le thème sombre, une toute claire sur le thème clair ;
//   la feuille de style l'inverse dans le thème où elle disparaîtrait.
function useIconLook(src: string | null) {
  const [look, setLook] = useState<{ tone: string; color: string | null }>({ tone: '', color: null });
  useEffect(() => {
    setLook({ tone: '', color: null });
    if (!src) return;
    const img = new Image();
    img.onload = () => {
      const canvas = document.createElement('canvas');
      canvas.width = canvas.height = 16;
      const ctx = canvas.getContext('2d');
      if (!ctx) return;
      ctx.drawImage(img, 0, 0, 16, 16);
      const px = ctx.getImageData(0, 0, 16, 16).data;
      let light = 0, count = 0, vivid = 0;
      const sum = [0, 0, 0];
      for (let i = 0; i < px.length; i += 4) {
        if (px[i + 3] < 128) continue; // pixels transparents ignorés
        const [r, g, b] = [px[i], px[i + 1], px[i + 2]];
        light += 0.299 * r + 0.587 * g + 0.114 * b;
        count++;
        const max = Math.max(r, g, b);
        if (max > 60 && (max - Math.min(r, g, b)) / max > 0.35) {
          sum[0] += r; sum[1] += g; sum[2] += b;
          vivid++;
        }
      }
      const mean = count ? light / count : 128;
      setLook({
        tone: mean < 60 ? ' tone-dark' : mean > 225 ? ' tone-light' : '',
        color: vivid > count * 0.08 ? `rgb(${sum.map(v => Math.round(v / vivid)).join(', ')})` : null,
      });
    };
    img.src = src;
    return () => void (img.onload = null);
  }, [src]);
  return look;
}

function SidebarTabRow({ tab, index, icon, active, grouped = false }: { tab: Tab; index: number; icon: string | null | undefined; active: boolean; grouped?: boolean }) {
  const label = tab.title && tab.title !== 'about:blank' ? tab.title : t('Nouvel onglet', 'New tab');
  const slot = grouped ? {} : { 'data-slot-id': tab.id, 'data-slot-first': index, 'data-slot-last': index };

  return (
    <div
      role="tab"
      aria-selected={active}
      className={`sidebar-tab${active ? ' active' : ''}${tab.asleep ? ' asleep' : ''}`}
      title={label}
      {...slot}
      onContextMenu={e => {
        e.preventDefault();
        send({ type: 'tab.menu', id: tab.id, x: e.clientX + OFFSET_X, y: e.clientY + OFFSET_Y() });
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
        aria-label={t(`Fermer ${label}`, `Close ${label}`)}
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
