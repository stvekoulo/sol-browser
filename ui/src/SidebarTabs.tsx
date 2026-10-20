import type { CSSProperties, ReactNode } from 'react';
import { send, type State, type Tab } from './bridge';
import { Icon } from './Icons';
import { t } from './i18n';
import { focusTab, middleClose, sidebarMenu } from './sidebarActions';
import { startSlotDrag } from './useSidebarDrag';
import {SidebarSort,type SidebarBlock} from './SidebarSort';
import './private-tabs.css';
import { SidebarAiGroups } from './SidebarAiGroups';

export function SidebarTabs({ state, icons }: { state: State; icons: Record<string, string | null | undefined> }) {
  const { tabs, split, activeId } = state;
  const selected = new Set(state.selectedTabIds ?? []);
  const rows = (from: number, to: number) => {
    const items: SidebarBlock[] = [];
    for (let i = from; i <= to; i++) {
      const tab = tabs[i], next = i < to ? tabs[i + 1] : undefined;
      if (next && tab.id === split.primaryId && next.id === split.secondaryId) {
        items.push({id:tab.id,kind:"tab",first:i,last:i+1,label:tab.title+" · "+next.title,node:<div key={tab.id} className={`sidebar-split-group${split.visible ? ' active' : ''}`} data-kind="tab" data-id={tab.id} data-first={i} data-last={i + 1}>
          <TabRow tab={tab} index={i} icon={icons[tab.id]} active={tab.id === activeId} selected={selected.has(tab.id)} paired />
          <TabRow tab={next} index={i + 1} icon={icons[next.id]} active={next.id === activeId} selected={selected.has(next.id)} paired />
        </div>}); i++;
      } else items.push({id:tab.id,kind:"tab",first:i,last:i,label:tab.title,node:<TabRow key={tab.id} tab={tab} index={i} icon={icons[tab.id]} active={tab.id === activeId} selected={selected.has(tab.id)} />});
    }
    return items;
  };
  const items: SidebarBlock[] = [];
  for (let i = 0; i < tabs.length; i++) {
    if (tabs[i].pinned) continue;
    let end = i;
    const group = state.groups.find(g => g.id === tabs[i].groupId);
    if (!group) {
      if (tabs[i].id === split.primaryId && tabs[i + 1]?.id === split.secondaryId) end++;
      items.push(...rows(i, end));
    } else {
      while (tabs[end + 1]?.groupId === group.id) end++;
      const members = rows(i, end);
      const expanded = !group.collapsed;
      const active = tabs.slice(i, end + 1).some(tab => tab.id === activeId);
      items.push({id:group.id,kind:"group",first:i,last:end,label:group.name,node:<div key={group.id} className={`tab-group-block${expanded ? '' : ' collapsed'}${active ? ' has-active' : ''}`}
        data-kind="group" data-id={group.id} data-first={i} data-last={end} style={{ '--group': group.color } as CSSProperties}>
        <div className="tab-group-heading" onContextMenuCapture={e => sidebarMenu('group.menu', group.id, e)}>
          <button className="tab-group-header" aria-expanded={expanded}
            title={expanded
              ? t('Cliquer pour replier · Glisser pour déplacer le groupe', 'Click to collapse · Drag to move group')
              : t('Cliquer pour déplier · Glisser pour déplacer le groupe', 'Click to expand · Drag to move group')}
            onPointerDown={e => startSlotDrag(e.currentTarget.parentElement!.parentElement, e)}
            onKeyDown={e => {
              if (e.key === 'F2') { e.preventDefault(); send({ type: 'group.edit', id: group.id }); }
              if (e.key === 'ContextMenu' || e.shiftKey && e.key === 'F10') sidebarMenu('group.menu', group.id, e);
            }}
            onClick={() => send({ type: 'group.toggle', id: group.id })}>
            {group.icon ? <span className="tab-group-icon" aria-hidden>{group.icon}</span> : <span className="tab-group-dot" aria-hidden />}
            <span className="tab-group-name">{group.name}</span><span className="tab-group-count">{end - i + 1}</span>
            <span className="tab-group-chevron" aria-hidden><Icon d="M6 4l4 4-4 4" size={12} /></span>
          </button>
          <button className="sidebar-item-action group-add" title={t('Nouvel onglet dans ce groupe', 'New tab in this group')} aria-label={t('Nouvel onglet dans ce groupe', 'New tab in this group')}
            onClick={() => send({ type: 'group.open', id: group.id })}><Icon d="M8 3v10M3 8h10" size={13} /></button>
          <button className="sidebar-item-action" aria-label={t(`Options du groupe ${group.name}`, `Options for group ${group.name}`)} title={t('Options du groupe', 'Group options')}
            onClick={e => sidebarMenu('group.menu', group.id, e)}><Icon d="M3 8h.01M8 8h.01M13 8h.01" size={15} /></button>
        </div>
        {expanded && <div className="tab-group-tabs"><SidebarSort blocks={members}/></div>}
      </div>});
    }
    i = end;
  }
  return <div className="sidebar-tabs" role="tablist" aria-label={t('Onglets', 'Tabs')} aria-orientation="vertical" aria-multiselectable="true"
    onKeyDown={e => { if (e.key === 'Escape' && selected.size) { e.preventDefault(); send({ type: 'tabs.selection.clear' }); } }}>
    {selected.size > 1 && <div className="sidebar-selection" role="group" aria-label={t('Onglets sélectionnés', 'Selected tabs')}>
      <span role="status">{t(`${selected.size} sélectionnés`, `${selected.size} selected`)}</span>
      <button className="pill-action" onClick={() => send({ type: 'group.new', id: [...selected][0] })}>{t('Créer un groupe', 'Create group')}</button>
      <button className="pill-action" aria-label={t('Annuler la sélection', 'Clear selection')} onClick={() => send({ type: 'tabs.selection.clear' })}>×</button>
    </div>}
    <SidebarSort blocks={items}/>
    <SidebarAiGroups ai={state.aiTabs} />
    <button className="sidebar-new-tab" title={t('Nouvel onglet · Ctrl+T', 'New tab · Ctrl+T')} onClick={() => send({ type: 'tab.open' })}>
      <Icon d="M8 3.5v9M3.5 8h9" size={14} /><span>{t('Nouvel onglet', 'New tab')}</span>
    </button>
  </div>;
}

function TabRow({ tab, index, icon, active, selected, paired = false }: { tab: Tab; index: number; icon: string | null | undefined; active: boolean; selected: boolean; paired?: boolean }) {
  const label = tab.title && tab.title !== 'about:blank' ? tab.title : t('Nouvel onglet', 'New tab');
  const activate = (toggle: boolean) => send(toggle ? { type: 'tab.select', id: tab.id } : { type: 'tab.activate', id: tab.id });
  return <div role="tab" tabIndex={0} aria-selected={selected || active} data-selected={selected || undefined} data-private={!!tab.privateMode} className={`sidebar-tab${active ? ' active' : ''}${selected ? ' selected' : ''}${tab.asleep ? ' asleep' : ''}`}
    title={`${label}\n${tab.url}${tab.asleep ? t('\nEn veille · Cliquer pour reprendre', '\nSleeping · Click to resume') : ''}${t('\nCtrl+clic pour sélectionner plusieurs onglets', '\nCtrl+click to select multiple tabs')}`}
    {...(paired ? {} : { 'data-kind': 'tab', 'data-id': tab.id, 'data-first': index, 'data-last': index })}
    onContextMenu={e => sidebarMenu('tab.menu', tab.id, e)} onClick={e => activate(e.ctrlKey || e.metaKey)}
    onKeyDown={e => { focusTab(e); if (e.target !== e.currentTarget) return; if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); activate(e.ctrlKey || e.metaKey); } if (e.key === 'Delete') send({ type: 'tab.close', id: tab.id }); }}
    onMouseDown={e => middleClose(e, tab.id)} onAuxClick={e => e.preventDefault()}
    onPointerDown={e => startSlotDrag(e.currentTarget.closest<HTMLElement>('[data-kind="tab"]'), e)}>
    {tab.loading ? <span className="spinner" aria-hidden /> : icon ? <img className="favicon" src={icon} alt="" draggable={false} /> : <span className="favicon letter" aria-hidden>{label[0]?.toUpperCase()}</span>}
    <span className="sidebar-tab-title">{label}</span>
    {tab.privateMode && <span className="private-tab-mark" title={t('Navigation privée', 'Private browsing')} aria-label={t('Navigation privée', 'Private browsing')}><Icon name="private" size={14}/></span>}
    {tab.sessionId && <span className="sidebar-session-badge" title={t(`Session : ${tab.sessionName}`, `Session: ${tab.sessionName}`)}>{tab.sessionName}</span>}
    <button className="sidebar-item-action tab-menu" aria-label={t(`Options de ${label}`, `Options for ${label}`)} title={t('Options de l’onglet', 'Tab options')}
      onPointerDown={e => e.stopPropagation()} onMouseDown={e => e.stopPropagation()} onClick={e => sidebarMenu('tab.menu', tab.id, e)}><Icon d="M3 8h.01M8 8h.01M13 8h.01" size={15} /></button>
    <button className="sidebar-tab-close" title={tab.groupId && !tab.privateMode ? t('Mettre en veille · Cliquer encore pour fermer', 'Put to sleep · Click again to close') : t('Fermer l’onglet', 'Close tab')} aria-label={t(`Fermer ${label}`, `Close ${label}`)}
      onPointerDown={e => e.stopPropagation()} onMouseDown={e => e.stopPropagation()} onClick={e => { e.stopPropagation(); send({ type: 'tab.close', id: tab.id }); }}><Icon d="M4.5 4.5l7 7M11.5 4.5l-7 7" size={12} /></button>
  </div>;
}
