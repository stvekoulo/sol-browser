import { useEffect, useRef, useState, type CSSProperties } from 'react';
import { formatDomain, send, type State } from './bridge';
import { t } from './i18n';
import { useIconLook } from './useIconLook';
import { focusTab, middleClose, sidebarMenu } from './sidebarActions';
import { startSlotDrag } from './useSidebarDrag';
import { Icon } from './Icons';

export function SidebarPinned({ state, icons }: { state: State; icons: Record<string, string | null | undefined> }) {
  const { shortcuts, tabs, activeId } = state;
  const pinned = tabs.filter(tab => tab.pinned && !shortcuts.some(s => s.tabId === tab.id));
  const strip = useRef<HTMLDivElement>(null);
  const [room, setRoom] = useState(0);
  useEffect(() => {
    const el = strip.current;
    if (!el) return;
    const observer = new ResizeObserver(([entry]) => setRoom(entry.contentRect.width)); observer.observe(el);
    return () => observer.disconnect();
  }, []);
  const named = (count: number) => count > 0 && (room - 12 * (count - 1)) / count >= 104;
  const wideAt = (index: number, count: number) => named(count) || (index === count - 1 && count <= 3);
  const rowClass = (count: number) => `pinned-row${!named(count) && count > 3 ? ' even' : ''}`;
  return <div ref={strip} className="pinned-strip">
    {shortcuts.length > 0 && <div className="pinned-row even" aria-label={t('Accès rapides', 'Shortcuts')}>
      {shortcuts.map((shortcut, index) => <ShortcutPill key={shortcut.url} shortcut={shortcut} index={index} wide={false} active={shortcut.tabId === activeId} />)}
    </div>}
    <div className="pinned-tabs-zone" data-pin-zone>
      {pinned.length > 0 && <div className={rowClass(pinned.length)}>
        {pinned.map((tab, i) => <button key={tab.id} role="tab" aria-selected={tab.id === activeId}
          className={`pinned-pill ${wideAt(i, pinned.length) ? 'wide' : 'circle'}${tab.id === activeId ? ' active' : ''}${tab.asleep ? ' asleep' : ''}`}
          title={`${tab.title || tab.url}\n${t('Glisser pour déplacer ou détacher', 'Drag to reorder or unpin')}`}
          data-kind="tab" data-id={tab.id} data-first={tabs.indexOf(tab)} data-last={tabs.indexOf(tab)} data-pinned="true" data-private={!!tab.privateMode}
          onContextMenu={e => sidebarMenu('tab.menu', tab.id, e)} onKeyDown={focusTab}
          onClick={() => send({ type: 'tab.activate', id: tab.id })}
          onMouseDown={e => middleClose(e, tab.id)} onAuxClick={e => e.preventDefault()}
          onPointerDown={e => startSlotDrag(e.currentTarget, e)}>
          {tab.privateMode ? <Icon name="private" size={16}/> : icons[tab.id] ? <img className="favicon" src={icons[tab.id]!} alt="" draggable={false} /> : <span className="favicon letter">{(tab.title || tab.url)[0]?.toUpperCase()}</span>}
          {wideAt(i, pinned.length) && <span>{tab.title?.split(' - ')[0].split(' · ')[0] || t('Épinglé', 'Pinned')}</span>}
        </button>)}
      </div>}
    </div>
  </div>;
}

function ShortcutPill({ shortcut, index, wide, active }: { shortcut: State['shortcuts'][number]; index: number; wide: boolean; active: boolean }) {
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
    onPointerDown={e => startSlotDrag(e.currentTarget, e)}>
    {icon ? <img className={`favicon${look.tone}`} src={icon} alt="" draggable={false} /> : <span className="favicon letter">{label[0]?.toUpperCase()}</span>}
    {wide && <span>{label}</span>}
  </button>;
}
