import {type RefObject} from 'react';
import {ContextMenu,ContextMenuContent,ContextMenuItem,ContextMenuSeparator} from '@/components/motion/context-menu';
import {send} from './bridge';
import {Icon} from './Icons';
import {menuIconPath,isClosing} from './PopupIcons';
import {t} from './i18n';
export default function SolMenu({items,rootRef}:{items:string[];rootRef:RefObject<HTMLDivElement|null>}){
  return <div ref={rootRef} className="popup beui-menu-popup">
    <ContextMenu open embedded onOpenChange={open=>{if(!open)send({type:'popup.close'});}}>
      <ContextMenuContent className="sol-menu-surface" ariaLabel={t('Actions','Actions')}>
        {items.map((label,index)=>label===''?<ContextMenuSeparator key={index}/>:<ContextMenuItem key={index} textValue={label} className="sol-menu-item" tone={isClosing(label)?'destructive':'default'} closeOnSelect={false} onSelect={()=>send({type:'menu.pick',index})}>
          <span className="row-badge" aria-hidden><Icon d={menuIconPath(label)} size={14}/></span><span className="row-title">{label}</span>
        </ContextMenuItem>)}
      </ContextMenuContent>
    </ContextMenu>
  </div>;
}
