import { useState } from 'react';
import { send } from './bridge';
import { t } from './i18n';
export default function NotesControls({days,ids,flush}:{days:number;ids:string[];flush:()=>void}){
  const [current,setCurrent]=useState(false);
  return <footer><button className="pill-action" onClick={()=>{flush();send({type:'notes.import'})}}>{t('Importer','Import')}</button><details className="notes-menu"><summary>{t('Exporter','Export')}</summary><div>
    <label className="note-export-filter"><input type="checkbox" checked={current} onChange={e=>setCurrent(e.target.checked)}/>{t('La vue filtrée','Filtered view')}</label>
    {([['markdown','Markdown'],['json','JSON'],['csv','CSV'],['zip',t('Notes et fichiers','Notes and files')]]).map(([format,label])=><button key={format} onClick={e=>{flush();e.currentTarget.closest('details')?.removeAttribute('open');send({type:'notes.export',query:format,input:current?JSON.stringify(ids):undefined})}}>{label}</button>)}
  </div></details><details className="note-policy"><summary>{t('Archivage automatique','Automatic archiving')}</summary><form onSubmit={e=>{e.preventDefault();send({type:'notes.archive-policy',index:Number(new FormData(e.currentTarget).get('days'))})}}><label>{t('Idées non épinglées après','Unpinned ideas after')}<input key={days} name="days" type="number" min="0" max="3650" defaultValue={days} required/> {t('jours (0 : désactivé)','days (0: disabled)')}</label><button className="pill-action">{t('Appliquer','Apply')}</button></form></details></footer>;
}
