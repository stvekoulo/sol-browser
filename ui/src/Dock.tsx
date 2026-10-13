import {useEffect,useLayoutEffect,useRef,useState} from 'react';
import {NotificationStack} from '@/components/motion/notification-stack';
import {interfaceScale,onMessage,send} from './bridge';
import {DownloadCard,pendingDownload,type Download} from './Downloads';
import DockUpdate,{type Update} from './DockUpdate';
import {t,useLang} from './i18n';

export default function Dock(){
  const [items,setItems]=useState<Download[]>([]),[update,setUpdate]=useState<Update|null>(null),[expanded,setExpanded]=useState(false);
  const root=useRef<HTMLDivElement>(null);useLang();
  useEffect(()=>{
    const off=onMessage(data=>{const content=data as {items?:Download[];update?:Update|null};if(content.items===undefined&&content.update===undefined)return;setItems(content.items??[]);setUpdate(content.update??null);});
    const blur=()=>setExpanded(false);window.addEventListener('blur',blur);send({type:'dock.ready'});
    return()=>{off();window.removeEventListener('blur',blur);};
  },[]);
  useLayoutEffect(()=>{
    const el=root.current;if(!el||!items.length&&!update)return;
    const report=()=>send({type:'dock.size',width:el.offsetWidth*interfaceScale(),height:el.offsetHeight*interfaceScale()});
    report();const observer=new ResizeObserver(report);observer.observe(el);window.addEventListener('ui-scale',report);
    return()=>{observer.disconnect();window.removeEventListener('ui-scale',report);};
  },[items.length,!!update]);
  if(!items.length&&!update)return null;
  const active=items.filter(d=>d.state==='InProgress'&&!d.paused&&!d.canceled),pending=items.filter(pendingDownload);
  const label=active.length?t(`${active.length} téléchargement${active.length>1?'s':''} en cours`,`${active.length} download${active.length>1?'s':''} in progress`):pending.length?(pending.some(d=>d.paused)?t('En pause','Paused'):t('Interrompu','Interrupted')):items.every(d=>d.canceled)?t('Annulé','Canceled'):items.some(d=>d.state==='Interrupted'&&!d.canceled)?t('Interrompu','Interrupted'):items.some(d=>d.canceled)?t('Téléchargements','Downloads'):t('Terminé','Done');
  return <div ref={root} className="dock download-stack-dock">
    {update&&<DockUpdate update={update}/>}
    {expanded&&<div className="dock-stack-heading"><span className="popup-header-title">{label}</span></div>}
    {!!items.length&&<NotificationStack expanded={expanded} onExpandedChange={setExpanded} maxVisible={3} ariaLabel={t('Téléchargements','Downloads')} className="sol-download-stack" collapsedLabel={<span className="popup-header-title">{label}</span>} expandedLabel={t('Réduire','Collapse')} classNames={{stack:'sol-download-stack-list',card:'sol-download-stack-card',footer:'sol-download-stack-footer',count:'sol-download-count'}} items={items.map(d=>({id:d.id,title:d.name,content:<DownloadCard d={d}/>}))}/>}
  </div>;
}
