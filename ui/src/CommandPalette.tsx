import {CommandPalette as BeuiPalette} from '@/components/motion/command-palette';
import {FileText,ArrowRight} from 'lucide-react';
import {send} from './bridge';
import {t} from './i18n';
import type {PaletteItem} from './PopupTypes';
export default function CommandPalette({items,listHeight}:{items:PaletteItem[];listHeight:number}){
  return <><div className="palette-heading"><span className="popup-header-title">{t('Commandes Sol','Sol commands')}</span><button className="pill-action" aria-label={t('Fermer','Close')} onClick={()=>send({type:'popup.close'})}>×</button></div>
    <BeuiPalette open embedded listHeight={listHeight} title={t('Commandes Sol','Sol commands')} placeholder={t('Rechercher une action ou un onglet…','Search for an action or tab…')} emptyMessage={t('Aucune action ou aucun onglet trouvé.','No matching action or tab.')} onOpenChange={open=>{if(!open)send({type:'popup.close'})}} items={items.map(item=>({id:item.id,label:item.title,group:item.group==='tabs'?t('Onglets ouverts','Open tabs'):t('Actions','Actions'),keywords:[item.hint,item.keywords],icon:item.group==='tabs'?FileText:ArrowRight,hint:item.shortcut||undefined,badge:item.group==='tabs'?undefined:null,onSelect:()=>send({type:'palette.pick',id:item.id})}))}/>
    <div className="palette-footer"><span>↑ ↓ {t('Parcourir','Navigate')}</span><span>↵ {t('Ouvrir','Open')}</span><span>{t('Échap Fermer','Esc Close')}</span></div></>;
}
