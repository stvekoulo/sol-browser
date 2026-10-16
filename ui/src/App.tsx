import {WindowControls,useWindowDrag} from './WindowControls';
import { useEffect, useState } from 'react';
import { isStartPage, onState, send, type State } from './bridge';
import { t, useLang } from './i18n';
import AddressPill from './AddressPill';
import { Icon, SidebarIcon } from './Icons';

export default function App({sidebarOpen=true}:{sidebarOpen?:boolean}) {
  const [state, setState] = useState<State | null>(null);
  useLang();
  const windowDrag=useWindowDrag();

  useEffect(() => {
    const off = onState(setState, e => {
      if (e.event === 'focus-address')
        requestAnimationFrame(() => {
          const input = document.querySelector<HTMLInputElement>('.address-pill.focused input, .address-pill input');
          input?.focus();
          input?.select();
        });
    });

    return off;
  }, []);

  if (!state) return null;
  const byId = (id: string | null) => state.tabs.find(t => t.id === id);
  const activeTab = byId(state.activeId);
  const { split } = state;
  const panes = split.visible ? [byId(split.primaryId), byId(split.secondaryId)] : [activeTab];

  return (
    <header className={`topbar${sidebarOpen?' sidebar-open':''}`} {...windowDrag}>
      <div className="topbar-left" {...windowDrag}>
        {!sidebarOpen&&<WindowControls maximized={state.maximized}/>}

        <div className="pill-surface navigation-actions" role="group" aria-label={t('Navigation et panneau latéral', 'Navigation and sidebar')}>
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
      </div>

      <div className={`topbar-center${split.visible ? ' split-addresses' : ''}`} {...windowDrag}>
        {panes.map((tab, i) => (
          <AddressPill
            key={tab?.id ?? i}
            tab={tab}
            focused={tab?.id === state.activeId}
            shield={state.shield}
            grow={!split.visible ? 1 : i === 0 ? split.ratio : 1 - split.ratio}
          />
        ))}
      </div>

      <div className="topbar-right pill-surface" {...windowDrag}>
        {activeTab && !isStartPage(activeTab.url) && activeTab.url !== 'https://start.sol/index.html#notes' && /^https?:\/\//.test(activeTab.url) &&
          <button data-action="translation.open" className={`pill-icon-btn${activeTab.translated ? ' translation-active' : ''}`} aria-label={t('Traduire la page', 'Translate page')} aria-pressed={!!activeTab.translated} title={activeTab.translated ? t('Traduction · voir l’original', 'Translation · view original') : t('Traduire la page', 'Translate page')}
            onClick={e => { const box = e.currentTarget.getBoundingClientRect(); send({ type: 'translation.open', id: activeTab.id, x: box.right, y: box.bottom + 6 }); }}>
            <Icon d="M2 4h7M5.5 2v2M3 4c.5 3 2 5 5 6M8 4c-.5 3-2 5-5 6M9 13l2.5-7L14 13M10 11h3" />
          </button>}
        {activeTab?.storeDetail && !state.privateMode &&
          <button className="store-install" onClick={() => send({ type:'extension.current' })}>{activeTab.storeInstalled ? t('Installée', 'Installed') : t('Ajouter à Sol', 'Add to Sol')}</button>}
        {state.extensions?.map(extension => <button key={extension.id} className="pill-icon-btn pinned-extension" aria-label={extension.name} title={extension.name}
          onClick={e => { const box = e.currentTarget.getBoundingClientRect(); send({type:'extension.show', id:extension.id, x:box.right, y:box.bottom+6}); }}>
          {extension.icon ? <img src={extension.icon} alt="" /> : <span>{extension.name.slice(0,1)}</span>}
        </button>)}
        <button
          className="pill-icon-btn"
          aria-label={t("Capture d'écran", 'Screenshot')}
          title={t("Capture d'écran", 'Screenshot')}
            onClick={() => send({ type: 'capture.menu' })}
        >
          <Icon d="M2.5 5.5h2l1-1.5h5l1 1.5h2v7h-11zM8 11a2 2 0 1 0 0-4 2 2 0 0 0 0 4" />
        </button>
        {state.privateMode && (
          <span className="private-chip" title={t('Rien de ce que vous visitez ici n’est gardé : tout est effacé à la fermeture de la fenêtre.', 'Nothing you visit here is kept: everything is erased when the window closes.')}>
            {t('Privé', 'Private')}
          </span>
        )}
        <button
          className="pill-icon-btn"
          aria-label={t('Extensions', 'Extensions')}
          data-action="extensions.menu"
          title={t('Extensions', 'Extensions')}
          onClick={e => {
            const box = e.currentTarget.getBoundingClientRect();
            send({ type: 'extensions.menu', x: box.right, y: box.bottom + 6 });
          }}
        >
          {/* Pièce de puzzle */}
          <Icon d="M6.2 3.2a1.4 1.4 0 1 1 2.8 0v.6h2.6v2.7h.6a1.4 1.4 0 1 1 0 2.8h-.6v3H8.9v-.7a1.3 1.3 0 1 0-2.6 0v.7H3.4V9.2h.7a1.3 1.3 0 1 0 0-2.6h-.7V3.8h2.8z" />
        </button>
        <button
          className="pill-icon-btn profile-btn"
          data-action="profile.menu"
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
          data-action="settings.open"
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
