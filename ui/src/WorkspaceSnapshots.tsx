import {useState} from 'react';
import {Button} from '@/components/motion/button/base';
import {send} from './bridge';
import {t} from './i18n';
export type WorkspaceContent={popup:'workspaces';error:string;canSave:boolean;items:{id:string;name:string;count:number;updated:string}[]};
export default function WorkspaceSnapshots({content}:{content:WorkspaceContent}){
  const [name,setName]=useState(''),[deleting,setDeleting]=useState('');
  return <section className="popup name-form"><h2 className="name-form-title">{t('Espaces de travail','Workspaces')}</h2>
    <p className="row-hint">{t('Retrouvez vos onglets, groupes, vues scindées et maquettes. La restauration ajoute les onglets à la fenêtre actuelle.','Restore tabs, groups, splits and mockups. Restoring adds tabs to the current window.')}</p>
    {content.canSave&&<form onSubmit={e=>{e.preventDefault();if(name.trim()){send({type:'workspace.save',title:name.trim()});setName('')}}}>
      <input aria-label={t('Nom de l’espace','Workspace name')} maxLength={80} value={name} onChange={e=>setName(e.target.value)} placeholder={t('Mon projet','My project')}/>
      <Button type="submit" className="pill-action" disabled={!name.trim()}>{t('Enregistrer la disposition','Save layout')}</Button></form>}
    {content.error&&<p role="alert">{content.error}</p>}
    <div className="popup-list">{content.items.map(w=><div key={w.id}>
      <Button className="row" onClick={()=>send({type:'workspace.restore',id:w.id})}><span className="row-title">{w.name}</span><span className="row-hint">{w.count} {t('onglets','tabs')}</span></Button>
      <Button className="pill-action" aria-label={t(`Supprimer ${w.name}`,`Delete ${w.name}`)} onClick={()=>{if(deleting===w.id){send({type:'workspace.delete',id:w.id});setDeleting('')}else setDeleting(w.id)}}>{deleting===w.id?t('Confirmer la suppression','Confirm deletion'):t('Supprimer','Delete')}</Button>
    </div>)}</div>
    {!content.items.length&&<p className="row-hint">{t('Aucun espace enregistré.','No saved workspaces.')}</p>}
  </section>;
}
