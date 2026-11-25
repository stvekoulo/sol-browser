import { send, type Tab } from './bridge';
import { t } from './i18n';
import { Icon } from './Icons';
import { focusTab, middleClose, sidebarMenu } from './sidebarActions';
import { startSlotDrag } from './useSidebarDrag';
import { TabFavicon } from './TabFavicon';

export function SidebarTabRow({
  tab,
  index,
  icon,
  active,
  focusable,
  selected,
  paired = false,
}: {
  tab: Tab;
  index: number;
  icon: string | null | undefined;
  active: boolean;
  focusable: boolean;
  selected: boolean;
  paired?: boolean;
}) {
  const label = tab.title && tab.title !== 'about:blank' ? tab.title : t('Nouvel onglet', 'New tab');
  const activate = (toggle: boolean) =>
    send(toggle ? { type: 'tab.select', id: tab.id } : { type: 'tab.activate', id: tab.id });
  const tooltip = `${label}\n${tab.url}${tab.asleep ? t('\nEn veille · Cliquer pour reprendre', '\nSleeping · Click to resume') : ''}${t('\nCtrl+clic pour sélectionner plusieurs onglets', '\nCtrl+click to select multiple tabs')}`;

  return (
    <div
      className={`sidebar-tab${active ? ' active' : ''}${selected ? ' selected' : ''}${tab.asleep ? ' asleep' : ''}`}
      data-id={tab.id}
      data-selected={selected || undefined}
      data-private={!!tab.privateMode}
      onContextMenu={e => sidebarMenu('tab.menu', tab.id, e)}
      onMouseDown={e => middleClose(e, tab.id)}
      onAuxClick={e => e.preventDefault()}
    >
      <button
        type="button"
        role="tab"
        tabIndex={focusable ? 0 : -1}
        aria-selected={selected || active}
        aria-busy={tab.loading || undefined}
        className="sidebar-tab-trigger"
        title={tooltip}
        {...(paired
          ? { 'data-id': tab.id }
          : { 'data-kind': 'tab', 'data-id': tab.id, 'data-first': index, 'data-last': index })}
        onClick={e => activate(e.ctrlKey || e.metaKey)}
        onKeyDown={e => {
          focusTab(e);
          if (e.target !== e.currentTarget) return;
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            activate(e.ctrlKey || e.metaKey);
          }
          if (e.key === 'Delete') send({ type: 'tab.close', id: tab.id });
          if (e.key === 'ContextMenu' || (e.shiftKey && e.key === 'F10')) {
            sidebarMenu('tab.menu', tab.id, e);
          }
        }}
        onPointerDown={e => startSlotDrag(e.currentTarget.closest<HTMLElement>('[data-kind="tab"]'), e)}
      >
        {tab.loading && !icon ? (
          <span className="spinner" aria-hidden />
        ) : (
          <TabFavicon icon={icon} label={label} />
        )}
        <span className="sidebar-tab-title">{label}</span>
        {tab.privateMode && (
          <span
            className="private-tab-mark"
            title={t('Navigation privée', 'Private browsing')}
            aria-label={t('Navigation privée', 'Private browsing')}
          >
            <Icon name="private" size={14} />
          </span>
        )}
        {tab.sessionId && (
          <span className="sidebar-session-badge" title={t(`Session : ${tab.sessionName}`, `Session: ${tab.sessionName}`)}>
            {tab.sessionName}
          </span>
        )}
      </button>
      <button
        type="button"
        className="sidebar-item-action tab-menu"
        aria-label={t(`Options de ${label}`, `Options for ${label}`)}
        title={t('Options de l’onglet', 'Tab options')}
        onPointerDown={e => e.stopPropagation()}
        onMouseDown={e => e.stopPropagation()}
        onClick={e => sidebarMenu('tab.menu', tab.id, e)}
      >
        <Icon d="M3 8h.01M8 8h.01M13 8h.01" size={15} />
      </button>
      <button
        type="button"
        className="sidebar-tab-close"
        title={
          tab.groupId && !tab.privateMode
            ? t('Mettre en veille · Cliquer encore pour fermer', 'Put to sleep · Click again to close')
            : t('Fermer l’onglet', 'Close tab')
        }
        aria-label={t(`Fermer ${label}`, `Close ${label}`)}
        onPointerDown={e => e.stopPropagation()}
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
