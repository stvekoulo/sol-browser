import {Button} from '@/components/motion/button/base';
import {Checkbox} from '@/components/motion/checkbox';
import { useState } from 'react';
import { send } from './bridge';
import { t } from './i18n';
export default function NotesControls({days,ids,flush}:{days:number;ids:string[];flush:()=>void}){
  const [current,setCurrent]=useState(false);
  return <footer><Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} className="pill-action" onClick={()=>{flush();send({type:'notes.import'})}}>{t('Importer','Import')}</Button><details className="notes-menu"><summary>{t('Exporter','Export')}</summary><div>
    <label className="note-export-filter"><Checkbox className="sol-checkbox" aria-label={t('La vue filtrée','Filtered view')} checked={current} onCheckedChange={setCurrent}/>{t('La vue filtrée','Filtered view')}</label>
    {([['markdown','Markdown'],['json','JSON'],['csv','CSV'],['zip',t('Notes et fichiers','Notes and files')]]).map(([format,label])=><Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} key={format} onClick={e=>{flush();e.currentTarget.closest('details')?.removeAttribute('open');send({type:'notes.export',query:format,input:current?JSON.stringify(ids):undefined})}}>{label}</Button>)}
  </div></details><details className="note-policy"><summary>{t('Archivage automatique','Automatic archiving')}</summary><form onSubmit={e=>{e.preventDefault();send({type:'notes.archive-policy',index:Number(new FormData(e.currentTarget).get('days'))})}}><label>{t('Idées non épinglées après','Unpinned ideas after')}<input key={days} name="days" type="number" min="0" max="3650" defaultValue={days} required/> {t('jours (0 : désactivé)','days (0: disabled)')}</label><Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} type="submit" className="pill-action">{t('Appliquer','Apply')}</Button></form></details></footer>;
}
