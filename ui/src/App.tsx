import { useEffect, useRef, useState, type MouseEvent } from 'react';
import { formatDomain, onMessage, onState, send, type State, type Tab } from './bridge';
import { t, useLang } from './i18n';
import { Icon, ShieldIcon, SidebarIcon } from './Icons';

// Pression sur le fond de la barre, en attente d'un mouvement pour déplacer la fenêtre.
let dragArmed: { x: number; y: number } | null = null;

export default function App() {
  const [state, setState] = useState<State | null>(null);
  const [sidebarOpen, setSidebarOpen] = useState(true);
  useLang();

  useEffect(() => {
    const off = onState(setState, e => {
      if (e.event === 'focus-address')
        document.querySelector<HTMLInputElement>('.address-pill.focused input, .address-pill input')?.focus();
      if (e.event === 'sidebar' && typeof e.open === 'boolean')
        setSidebarOpen(e.open);
    });
    send({ type: 'ui.ready' });

    const move = (e: globalThis.MouseEvent) => {
      if (!dragArmed) return;
      if (!(e.buttons & 1)) return void (dragArmed = null);
      if (Math.abs(e.screenX - dragArmed.x) + Math.abs(e.screenY - dragArmed.y) < 4) return;
      dragArmed = null;
      send({ type: 'window.drag' });
    };
    const disarm = () => (dragArmed = null);
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', disarm);
    return () => {
      off();
      window.removeEventListener('mousemove', move);
      window.removeEventListener('mouseup', disarm);
    };
  }, []);

  if (!state) return null;
  const byId = (id: string | null) => state.tabs.find(t => t.id === id);
  const activeTab = byId(state.activeId);
  const { split } = state;
  const panes = split.visible ? [byId(split.primaryId), byId(split.secondaryId)] : [activeTab];

  // Seul le fond de la barre déplace ou agrandit la fenêtre. Le déplacement ne démarre qu'une fois la souris
  // réellement en mouvement, bouton enfoncé : lancé sur un simple clic, Windows attendrait un relâchement
  // déjà passé et la fenêtre resterait accrochée au curseur.
  const dragWindow = (e: MouseEvent) => {
    if (e.button === 0 && e.target === e.currentTarget) dragArmed = { x: e.screenX, y: e.screenY };
  };
  const maximize = (e: MouseEvent) => {
    if (e.target === e.currentTarget) send({ type: 'window.maximize' });
  };

  return (
    <header className="topbar" onMouseDown={dragWindow} onDoubleClick={maximize}>
      <div className="topbar-left" onMouseDown={dragWindow}>
        <div className="lights">
          <button className="light close" aria-label={t('Fermer', 'Close')} onClick={() => send({ type: 'window.close' })} />
          <button className="light minimize" aria-label={t('Réduire', 'Minimize')} onClick={() => send({ type: 'window.minimize' })} />
          <button
            className="light maximize"
            aria-label={state.maximized ? t('Restaurer', 'Restore') : t('Agrandir', 'Maximize')}
            onClick={() => send({ type: 'window.maximize' })}
          />
        </div>

        <button
          className={`pill-surface sidebar-toggle${sidebarOpen ? ' active' : ''}`}
          aria-label={sidebarOpen ? t('Masquer les onglets', 'Hide tabs') : t('Afficher les onglets', 'Show tabs')}
          aria-pressed={sidebarOpen}
          title={sidebarOpen ? t('Masquer le panneau latéral', 'Hide sidebar') : t('Afficher le panneau latéral', 'Show sidebar')}
          onClick={() => send({ type: 'sidebar.toggle' })}
        >
          <SidebarIcon />
        </button>

        <div className="pill-surface nav-segment" role="group" aria-label="Navigation">
          <button
            className="segment-btn"
            aria-label={t('Précédent', 'Back')}
            disabled={!activeTab?.canGoBack}
            onClick={() => activeTab && send({ type: 'nav.back', id: activeTab.id })}
          >
            <Icon d="M9.5 4L5.5 8l4 4" />
          </button>
          <span className="segment-divider" aria-hidden />
          <button
            className="segment-btn"
            aria-label={t('Suivant', 'Forward')}
            disabled={!activeTab?.canGoForward}
            onClick={() => activeTab && send({ type: 'nav.forward', id: activeTab.id })}
          >
            <Icon d="M6.5 4l4 4-4 4" />
          </button>
        </div>
      </div>

      <div className="topbar-center" onMouseDown={dragWindow} onDoubleClick={maximize}>
        {panes.map((tab, i) => (
          <AddressPill
            key={tab?.id ?? i}
            tab={tab}
            focused={tab?.id === state.activeId}
            grow={!split.visible ? 1 : i === 0 ? split.ratio : 1 - split.ratio}
          />
        ))}
      </div>

      <div className="topbar-right" onMouseDown={dragWindow}>
        {state.downloads.count > 0 && !state.downloads.docked && (
          <button
            className={`pill-icon-btn${state.downloads.active > 0 ? ' on' : ''}`}
            aria-label={state.downloads.active > 0 ? t('Téléchargements (en cours)', 'Downloads (in progress)') : t('Téléchargements', 'Downloads')}
            title={t('Téléchargements', 'Downloads')}
            onClick={e => {
              const box = e.currentTarget.getBoundingClientRect();
              send({ type: 'downloads.open', x: box.right, y: box.bottom + 6 });
            }}
          >
            <Icon d="M8 2.5v8M4.5 7.5L8 11l3.5-3.5M3 13.5h10" />
          </button>
        )}
        <button
          className="pill-icon-btn"
          aria-label={t("Capture d'écran", 'Screenshot')}
          title={t("Capture d'écran", 'Screenshot')}
          onClick={e => {
            const box = e.currentTarget.getBoundingClientRect();
            send({ type: 'capture.menu', x: box.right, y: box.bottom + 6 });
          }}
        >
          <Icon d="M2.5 5.5h2l1-1.5h5l1 1.5h2v7h-11zM8 11a2 2 0 1 0 0-4 2 2 0 0 0 0 4" />
        </button>
        <button
          className={`pill-icon-btn${ split.visible ? ' on' : ''}`}
          aria-label={split.visible ? t('Fermer la vue scindée', 'Close split view') : t('Vue scindée', 'Split view')}
          aria-pressed={split.visible}
          title={split.visible ? t('Fermer la vue scindée', 'Close split view') : t('Afficher deux pages côte à côte', 'Show two pages side by side')}
          onClick={() => send({ type: split.visible ? 'split.close' : 'split.open' })}
        >
          <Icon d="M2 3h12v10H2zM8 3v10" />
        </button>
        <button
          className="pill-icon-btn"
          aria-label={t('Favoris et historique', 'Bookmarks and history')}
          title={t('Favoris et historique', 'Bookmarks and history')}
          onClick={e => {
            const box = e.currentTarget.getBoundingClientRect();
            send({ type: 'library.open', x: box.right, y: box.bottom + 6 });
          }}
        >
          <Icon d="M3 4h10M3 8h10M3 12h10" />
        </button>
        <button
          className="pill-icon-btn profile-btn"
          aria-label={t(`Profil : ${state.profile}`, `Profile: ${state.profile}`)}
          title={t(`Profil : ${state.profile}`, `Profile: ${state.profile}`)}
          onClick={e => {
            const box = e.currentTarget.getBoundingClientRect();
            send({ type: 'profile.menu', x: box.right, y: box.bottom + 6 });
          }}
        >
          <Icon d="M8 8a2.6 2.6 0 1 0 0-5.2A2.6 2.6 0 0 0 8 8M3 13.2c.6-2.2 2.6-3.4 5-3.4s4.4 1.2 5 3.4" />
        </button>
        <button
          className="pill-icon-btn"
          aria-label={t('Réglages', 'Settings')}
          title={t('Réglages', 'Settings')}
          onClick={e => {
            const box = e.currentTarget.getBoundingClientRect();
            send({ type: 'settings.open', x: box.right, y: box.bottom + 6 });
          }}
        >
          {/* Curseurs de réglage : deux glissières avec leur poignée */}
          <Icon d="M2.5 5h4M9.5 5h4M2.5 11h7M12.5 11h1M8 3.5a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3M11 9.5a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3" />
        </button>
      </div>
    </header>
  );
}

// Pilule d'adresse centrale : affiche le domaine centré au repos, et l'URL complète à l'édition.
function AddressPill({ tab, focused, grow }: { tab: Tab | undefined; focused: boolean; grow: number }) {
  const rawUrl = !tab || tab.url === 'about:blank' ? '' : tab.url;
  const domain = formatDomain(rawUrl);
  const [text, setText] = useState(rawUrl);
  const [editing, setEditing] = useState(false);
  const [count, setCount] = useState(0);
  const [selected, setSelected] = useState(-1);
  const pillRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!editing) setText(rawUrl);
  }, [rawUrl, editing]);

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
    const next = ((selected + 1 + step + count + 1) % (count + 1)) - 1;
    setSelected(next);
    send({ type: 'suggest.select', index: next });
  };

  return (
    <div ref={pillRef} className={`address-pill${focused ? ' focused' : ''}${editing ? ' editing' : ''}`} style={{ flexGrow: grow }}>
      <span className="address-lead" aria-hidden>
        {tab?.loading ? <span className="spinner" /> : <ShieldIcon />}
      </span>

      <form
        className="address-form"
        onSubmit={e => {
          e.preventDefault();
          if (id && selected >= 0) send({ type: 'suggest.pick', index: selected });
          else if (id && text.trim()) send({ type: 'nav.go', id, input: text });
          (document.activeElement as HTMLElement | null)?.blur();
        }}
      >
        <input
          aria-label={t('Adresse ou recherche', 'Address or search')}
          placeholder={t('Rechercher ou saisir une adresse', 'Search or enter an address')}
          spellCheck={false}
          value={editing ? text : domain}
          onChange={e => {
            setText(e.target.value);
            const box = (pillRef.current ?? e.target).getBoundingClientRect();
            if (id) send({ type: 'suggest', id, query: e.target.value, x: box.left, y: box.bottom + 6, width: box.width });
          }}
          onFocus={e => {
            setEditing(true);
            setText(rawUrl);
            requestAnimationFrame(() => e.target.select());
            if (id && !focused) send({ type: 'tab.activate', id });
          }}
          onKeyDown={e => {
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
              e.preventDefault();
              move(e.key === 'ArrowDown' ? 1 : -1);
            } else if (e.key === 'Escape') {
              setText(rawUrl);
              e.currentTarget.blur();
            }
          }}
          onBlur={() => {
            setEditing(false);
            setCount(0);
            setSelected(-1);
            if (id) send({ type: 'suggest', id, query: '', x: 0, y: 0, width: 0 });
          }}
        />
      </form>

      <div className="address-trail">
        {tab && tab.zoom !== 100 && (
          <button className="zoom-chip" aria-label={t(`Zoom ${tab.zoom} %, revenir à 100 %`, `Zoom ${tab.zoom}%, reset to 100%`)} title={t('Revenir à 100 %', 'Reset to 100%')} onClick={() => send({ type: 'zoom.reset', id: tab.id })}>
            {tab.zoom} %
          </button>
        )}
        {rawUrl && (
          <button
            className={`inline-btn${tab?.bookmarked ? ' on' : ''}`}
            aria-label={tab?.bookmarked ? t('Retirer des favoris', 'Remove bookmark') : t('Ajouter aux favoris', 'Add bookmark')}
            title={tab?.bookmarked ? t('Retirer des favoris', 'Remove bookmark') : t('Ajouter aux favoris', 'Add bookmark')}
            onClick={() => tab && send(tab.bookmarked ? { type: 'bookmark.remove', url: tab.url } : { type: 'bookmark.add', url: tab.url, title: tab.title })}
          >
            <Icon d="M8 2.4l1.7 3.5 3.8.5-2.8 2.7.7 3.8L8 11.1l-3.4 1.8.7-3.8-2.8-2.7 3.8-.5z" fill={!!tab?.bookmarked} size={14} />
          </button>
        )}
        <button
          className="inline-btn"
          aria-label={tab?.loading ? t('Arrêter', 'Stop') : t('Recharger', 'Reload')}
          title={tab?.loading ? t('Arrêter', 'Stop') : t('Recharger', 'Reload')}
          disabled={!id}
          onClick={() => id && send({ type: tab?.loading ? 'nav.stop' : 'nav.reload', id })}
        >
          <Icon d={tab?.loading ? 'M4.5 4.5l7 7M11.5 4.5l-7 7' : 'M12.8 8a4.8 4.8 0 1 1-1.4-3.4M12.8 3.2v3h-3'} size={14} />
        </button>
      </div>
    </div>
  );
}
