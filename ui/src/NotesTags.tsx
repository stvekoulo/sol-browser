import {Card} from '@/components/motion/card';
import {Button} from '@/components/motion/button/base';
import { useEffect,useState } from 'react';
import {Input} from '@/components/motion/input';
import SolModal from './SolModal';
import { t } from './i18n';
import { send } from './bridge';
import type { Note } from './notesModel';
type TagLook={icon:string;collapsed:boolean;order:number};
export default function NotesTags({items,selected,choose,rename,flush}:{items:Note[];selected:string;choose:(tag:string)=>void;rename:(tag:string)=>void;flush:()=>void}){
  const [looks,setLooks]=useState<Record<string,TagLook>>(()=>{try{const value=JSON.parse(localStorage.getItem('sol.notes.tags')||'{}');return Object.fromEntries(Object.entries(value).filter(([,v])=>v&&typeof v==='object'&&'icon'in v&&typeof v.icon==='string'&&v.icon.length<=8)) as Record<string,TagLook>}catch{return {}}}),[iconTag,setIconTag]=useState<string|null>(null),[icon,setIcon]=useState('');
  useEffect(()=>localStorage.setItem('sol.notes.tags',JSON.stringify(looks)),[looks]);
  const tags=[...new Set(items.filter(n=>!n.deleted).flatMap(n=>n.tags.flatMap(name=>name.split('/').map((_,i)=>name.split('/').slice(0,i+1).join('/')))))].sort((a,b)=>(looks[a]?.order??1000)-(looks[b]?.order??1000)||a.localeCompare(b));
  const update=(name:string,patch:Partial<TagLook>)=>setLooks(old=>({...old,[name]:{...(old[name]??{icon:'',collapsed:false,order:1000}),...patch}}));
  const reorder=(tag:string,delta:number)=>{const index=tags.indexOf(tag),next=tags[index+delta];if(!next)return;setLooks(old=>Object.fromEntries(tags.map((name,i)=>[name,{...(old[name]??{icon:'',collapsed:false}),order:name===tag?index+delta:name===next?index:i}])))};
  return <div className="notes-tags" spellCheck={false}><h2>Tags</h2>{tags.filter(name=>!name.split('/').slice(0,-1).some((_,i)=>looks[name.split('/').slice(0,i+1).join('/')]?.collapsed)).map(name=>{
    const children=tags.some(tag=>tag.startsWith(name+'/'));
    return <div className="note-tag-row" key={name} draggable onDragStart={e=>e.dataTransfer.setData('application/sol-tag',name)} onDragOver={e=>{if(e.dataTransfer.types.includes('application/sol-tag'))e.preventDefault()}} onDrop={e=>{const from=e.dataTransfer.getData('application/sol-tag');if(from&&from!==name&&!name.startsWith(from+'/')){e.preventDefault();flush();send({type:'notes.tag',id:from,title:name+'/'+from.split('/').at(-1)})}}}>
      {children&&<Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} className="notes-tool" aria-label={looks[name]?.collapsed?t('Déplier le tag','Expand tag'):t('Replier le tag','Collapse tag')} aria-expanded={!looks[name]?.collapsed} onClick={()=>update(name,{collapsed:!looks[name]?.collapsed})}>{looks[name]?.collapsed?'›':'⌄'}</Button>}
      <Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} className={selected===name?'active':''} style={{paddingLeft:8+name.split('/').length*6}} onClick={()=>choose(name)}><span>{looks[name]?.icon||'#'}</span>{name.split('/').at(-1)}<small>{items.filter(n=>!n.deleted&&n.tags.some(t=>t===name||t.startsWith(name+'/'))).length}</small></Button>
      <details className="notes-menu"><summary aria-label={t('Actions du tag','Tag actions')}>⋯</summary><Card size="compact" className="sol-surface-card sol-inline-menu"><Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} onClick={()=>rename(name)}>{t('Renommer / supprimer','Rename / remove')}</Button><Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} onClick={()=>{setIconTag(name);setIcon(looks[name]?.icon||'')}}>{t('Changer l’icône','Change icon')}</Button><Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} onClick={()=>reorder(name,-1)}>{t('Monter','Move up')}</Button><Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} onClick={()=>reorder(name,1)}>{t('Descendre','Move down')}</Button></Card></details>
    </div>;
  })}{!tags.length&&<p>{t('Ajoutez #un-tag à une note.','Add #a-tag to a note.')}</p>}
  <SolModal id={iconTag?"tag-icon":null} title={t("Icône du tag","Tag icon")} close={()=>setIconTag(null)}><form className="note-modal" onSubmit={e=>{e.preventDefault();iconTag&&update(iconTag,{icon});setIconTag(null)}}><h2>{t('Icône du tag','Tag icon')}</h2><Input aria-label={t('Emoji ou symbole','Emoji or symbol')} placeholder="📚" maxLength={8} value={icon} onChange={setIcon}/><div><Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} type="button" className="pill-action" onClick={()=>setIconTag(null)}>{t('Annuler','Cancel')}</Button><Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} type="submit" className="pill-action">{t('Enregistrer','Save')}</Button></div></form></SolModal>
  </div>;
}
