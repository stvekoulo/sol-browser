import { useEffect, useRef, useState, type CSSProperties } from 'react';
import { formatDomain, send, type State } from './bridge';
import { t } from './i18n';
import { Icon } from './Icons';
import { focusTab, middleClose, sidebarMenu } from './sidebarActions';
import { useIconLook } from './useIconLook';
import { startSlotDrag } from './useSidebarDrag';
import { TabFavicon } from './TabFavicon';

export function SidebarPinned({
  state,
  icons,
}: {
  state: State;
  icons: Record<string, string | null | undefined>;
}) {
  const { shortcuts, tabs, activeId } = state;
  const visibleTabs = tabs.filter(tab => !shortcuts.some(s => s.tabId === tab.id));
  const focusableId = visibleTabs.some(tab => tab.id === activeId) ? activeId : visibleTabs[0]?.id;
  const pinned = visibleTabs.filter(tab => tab.pinned);
  const strip = useRef<HTMLDivElement>(null);
  const [room, setRoom] = useState(0);

  useEffect(() => {
    const el = strip.current;
    if (!el) return;
    const observer = new ResizeObserver(([entry]) => setRoom(entry.contentRect.width));
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const named = (count: number) => count > 0 && (room - 12 * (count - 1)) / count >= 104;
  const wideAt = (index: number, count: number) => named(count) || (index === count - 1 && count <= 3);
  const rowClass = (count: number) => `pinned-row${!named(count) && count > 3 ? ' even' : ''}`;

  return (
    <div ref={strip} className="pinned-strip">
      {/* Jusqu'à trois accès rapides se partagent toute la largeur, avec leur nom s'il tient ; à quatre et plus, des carrés par quatre. */}
      {shortcuts.length > 0 && (
        <div
          className="pinned-row shortcuts-row"
          role="group"
          aria-label={t('Accès rapides', 'Shortcuts')}
          style={{ '--count': Math.min(shortcuts.length, 4) } as CSSProperties}
        >
          {shortcuts.map((shortcut, index) => (
            <ShortcutPill
              key={shortcut.url}
              shortcut={shortcut}
              index={index}
              wide={shortcuts.length < 4 && named(shortcuts.length)}
              active={shortcut.tabId === activeId}
            />
          ))}
        </div>
      )}
      <div className="pinned-tabs-zone" data-pin-zone>
        {pinned.length > 0 && (
          <div className={rowClass(pinned.length)} role="tablist" aria-label={t('Onglets épinglés', 'Pinned tabs')}>
            {pinned.map((tab, i) => {
              const wide = wideAt(i, pinned.length);
              const label =
                tab.title && tab.title !== 'about:blank'
                  ? tab.title
                  : formatDomain(tab.url) || tab.url || t('Épinglé', 'Pinned');
              const tabIdx = tabs.indexOf(tab);
              return (
                <button
                  key={tab.id}
                  type="button"
                  role="tab"
                  tabIndex={tab.id === focusableId ? 0 : -1}
                  aria-selected={tab.id === activeId}
                  aria-busy={tab.loading || undefined}
                  aria-label={label}
                  className={`pinned-pill ${wide ? 'wide' : 'circle'}${tab.id === activeId ? ' active' : ''}${tab.asleep ? ' asleep' : ''}`}
                  title={`${tab.title || tab.url}\n${t('Glisser pour déplacer ou détacher', 'Drag to reorder or unpin')}`}
                  data-kind="tab"
                  data-id={tab.id}
                  data-first={tabIdx}
                  data-last={tabIdx}
                  data-pinned="true"
                  data-private={!!tab.privateMode}
                  onContextMenu={e => sidebarMenu('tab.menu', tab.id, e)}
                  onKeyDown={focusTab}
                  onClick={() => send({ type: 'tab.activate', id: tab.id })}
                  onMouseDown={e => middleClose(e, tab.id)}
                  onAuxClick={e => e.preventDefault()}
                  onPointerDown={e => startSlotDrag(e.currentTarget, e)}
                >
                  {tab.privateMode ? (
                    <Icon name="private" size={16} />
                  ) : (
                    <TabFavicon icon={icons[tab.id]} label={tab.title || tab.url} />
                  )}
                  {wide && <span>{tab.title?.split(' - ')[0].split(' · ')[0] || t('Épinglé', 'Pinned')}</span>}
                </button>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}

function ShortcutPill({
  shortcut,
  index,
  wide,
  active,
}: {
  shortcut: State['shortcuts'][number];
  index: number;
  wide: boolean;
  active: boolean;
}) {
  const { name, url, icon, tabId } = shortcut;
  const label = name || formatDomain(url) || url;
  const look = useIconLook(icon);
  return (
    <button
      type="button"
      aria-label={label}
      className={`pinned-pill ${wide ? 'wide' : 'circle'}${active ? ' active' : ''}${tabId ? ' loaded' : ''}`}
      style={look.color ? ({ '--site': look.color } as CSSProperties) : undefined}
      title={`${label}\n${t('Glisser pour ranger · Clic droit pour personnaliser', 'Drag to reorder · Right-click to customize')}`}
      data-kind="shortcut"
      data-id={index}
      data-first={index}
      data-last={index}
      onContextMenu={e => sidebarMenu('shortcut.menu', String(index), e)}
      onClick={() => send({ type: 'shortcut.open', index })}
      onMouseDown={e => middleClose(e, tabId)}
      onAuxClick={e => e.preventDefault()}
      onPointerDown={e => startSlotDrag(e.currentTarget, e)}
    >
      {icon ? (
        <img className={`favicon${look.tone}`} src={icon} alt="" draggable={false} />
      ) : (
        <span className="favicon letter" aria-hidden="true">
          {label[0]?.toUpperCase()}
        </span>
      )}
      {wide && <span>{label}</span>}
    </button>
  );
}
