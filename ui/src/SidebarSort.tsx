import {useLayoutEffect,type ReactNode,type KeyboardEvent} from 'react';
import {SortableList,SortableListGroup,SortableListItem,useSortableList} from '@/components/motion/sortable-list';
import {send} from './bridge';
import {t} from './i18n';
import {sidebarSortOrder,type SidebarOrder} from './sidebarSortOrder';
export type SidebarBlock=SidebarOrder&{label:string;node:ReactNode};
let pendingFocus:{id:string;group:boolean}|null=null;
export function SidebarSort({blocks}:{blocks:SidebarBlock[]}){
  useLayoutEffect(()=>{if(!pendingFocus||!blocks.some(b=>b.id===pendingFocus!.id))return;const block=document.querySelector<HTMLElement>(`[data-kind="${pendingFocus.group?'group':'tab'}"][data-id="${CSS.escape(pendingFocus.id)}"]`);const target=pendingFocus.group?block?.querySelector<HTMLElement>('.tab-group-header'):block?.matches('[role="tab"]')?block:block?.querySelector<HTMLElement>('[role="tab"]');if(target){target.focus({preventScroll:true});pendingFocus=null;}},[blocks]);
  return <SortableList items={blocks} getItemLabel={b=>b.label} label={t('Rangement des onglets','Tab ordering')} showUndo={false} className="sol-sidebar-sort" instructionsText={t('Glissez pour ranger. Ctrl+Maj+flèches haut et bas déplacent l’onglet ou le groupe.','Drag to reorder. Ctrl+Shift+Up and Down move the tab or group.')} onItemsChange={next=>{const command=sidebarSortOrder(blocks,next);if(command)send(command)}}>
    <SortableListGroup role="presentation" className="sol-sidebar-sort-list">{blocks.map(block=><SortableListItem key={block.id} id={block.id} externalDrag role="presentation" className="sol-sidebar-sort-item" data-sort-block={block.id}><SortKeys id={block.id}>{block.node}</SortKeys></SortableListItem>)}</SortableListGroup>
  </SortableList>;
}
export function useSidebarSortKey(){
  const {moveItem}=useSortableList();
  return (e:KeyboardEvent<HTMLElement>,id:string)=>{
    if(!e.ctrlKey||!e.shiftKey||!['ArrowUp','ArrowDown','Home','End'].includes(e.key))return false;
    e.preventDefault();e.stopPropagation();pendingFocus={id,group:(e.target as HTMLElement).matches('.tab-group-header')};moveItem(id,e.key==='Home'?'first':e.key==='End'?'last':e.key==='ArrowUp'?-1:1);return true;
  };
}
function SortKeys({id,children}:{id:string;children:ReactNode}){
  const key=useSidebarSortKey();
  return <div className="sol-sort-content" onKeyDownCapture={e=>{const target=e.target as HTMLElement;if(target.closest<HTMLElement>('[data-sort-block]')?.dataset.sortBlock===id&&target.matches('[role="tab"],.tab-group-header'))key(e,id);}}>{children}</div>;
}
