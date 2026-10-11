import { useEffect, useRef, useState, type CSSProperties } from 'react';
import { formatDomain, send, type State, type Tab } from './bridge';
import { t } from './i18n';
import { useIconLook } from './useIconLook';
import { focusTab, middleClose, sidebarMenu } from './sidebarActions';
import { startSlotDrag } from './useSidebarDrag';
import { tabMatches } from './SidebarTabs';

export function SidebarPinned({ state, icons, query }: { state: State; icons: Record<string, string | null | undefined>; query: string }) {
  const { shortcuts, tabs, activeId } = state;
  const pinned = tabs.filter(tab => tab.pinned && !shortcuts.some(s => s.tabId === tab.id) && tabMatches(tab, query));
  const shown = shortcuts.map((shortcut, index) => ({ shortcut, index })).filter(({ shortcut }) => !query || tabMatches({title: shortcut.name, url: shortcut.url}, query));
  const strip = useRef<HTMLDivElement>(null);
  const [room, setRoom] = useState(0);
  useEffect(() => {
    const el = strip.current;
    if (!el) return;
    const observer = new ResizeObserver(() => setRoom(el.clientWidth)); observer.observe(el);
    return () => observer.disconnect();
  }, []);
  const named = (count: number) => count > 0 && (room - 8 * (count - 1)) / count >= 104;
  const wideAt = (index: number, count: number) => named(count) || (index === count - 1 && count <= 3);
  const rowClass = (count: number) => `pinned-row${!named(count) && count > 3 ? ' even' : ''}`;
  return <div ref={strip} className="pinned-strip">
    {shown.length > 0 && <div className={rowClass(shown.length)} aria-label={t('Accès rapides', 'Shortcuts')}>
      {shown.map(({shortcut, index}, shownIndex) => <ShortcutPill key={shortcut.url} shortcut={shortcut} index={index} wide={wideAt(shownIndex, shown.length)} active={shortcut.tabId === activeId} filtered={!!query} />)}
    </div>}
    <div className="pinned-tabs-zone" data-pin-zone>
      {pinned.length > 0 && <div className={rowClass(pinned.length)}>
        {pinned.map((tab, i) => <button key={tab.id} role="tab" aria-selected={tab.id === activeId}
          className={`pinned-pill ${wideAt(i, pinned.length) ? 'wide' : 'circle'}${tab.id === activeId ? ' active' : ''}${tab.asleep ? ' asleep' : ''}`}
          title={`${tab.title || tab.url}\n${t('Glisser pour déplacer ou détacher', 'Drag to reorder or unpin')}`}
          data-kind="tab" data-id={tab.id} data-first={tabs.indexOf(tab)} data-last={tabs.indexOf(tab)} data-pinned="true"
          onContextMenu={e => sidebarMenu('tab.menu', tab.id, e)} onKeyDown={focusTab}
          onClick={() => send({ type: 'tab.activate', id: tab.id })}
          onMouseDown={e => middleClose(e, tab.id)} onAuxClick={e => e.preventDefault()}
          onPointerDown={e => { if (!query) startSlotDrag(e.currentTarget, e); }}>
          {icons[tab.id] ? <img className="favicon" src={icons[tab.id]!} alt="" draggable={false} /> : <span className="favicon letter">{(tab.title || tab.url)[0]?.toUpperCase()}</span>}
          {wideAt(i, pinned.length) && <span>{tab.title?.split(' - ')[0].split(' · ')[0] || t('Épinglé', 'Pinned')}</span>}
        </button>)}
      </div>}
    </div>
  </div>;
}

function ShortcutPill({ shortcut, index, wide, active, filtered }: { shortcut: State['shortcuts'][number]; index: number; wide: boolean; active: boolean; filtered: boolean }) {
  const { name, url, icon, tabId } = shortcut;
  const label = name || formatDomain(url);
  const look = useIconLook(icon);
  return <button className={`pinned-pill ${wide ? 'wide' : 'circle'}${active ? ' active' : ''}${tabId ? ' loaded' : ''}`}
    style={look.color ? ({ '--site': look.color } as CSSProperties) : undefined}
    title={`${label}\n${t('Glisser pour ranger · Clic droit pour personnaliser', 'Drag to reorder · Right-click to customize')}`}
    data-kind="shortcut" data-id={index} data-first={index} data-last={index}
    onContextMenu={e => sidebarMenu('shortcut.menu', String(index), e)}
    onClick={() => send({ type: 'shortcut.open', index })}
    onMouseDown={e => middleClose(e, tabId)} onAuxClick={e => e.preventDefault()}
    onPointerDown={e => { if (!filtered) startSlotDrag(e.currentTarget, e); }}>
    {icon ? <img className={`favicon${look.tone}`} src={icon} alt="" draggable={false} /> : <span className="favicon letter">{label[0]?.toUpperCase()}</span>}
    {wide && <span>{label}</span>}
  </button>;
}
