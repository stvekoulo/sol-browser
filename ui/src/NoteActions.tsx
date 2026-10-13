import {OverflowActions} from '@/components/motion/overflow-actions';
import {Pin,Trash2,Clipboard,Maximize2,Minimize2} from 'lucide-react';
import {send} from './bridge';
import {t} from './i18n';
import type {Note} from './notesModel';
import AnimatedIcon from './AnimatedIcon';
export default function NoteActions({note,focus,setFocus,flush,act}:{note:Note;focus:boolean;setFocus:(v:boolean)=>void;flush:()=>void;act:(key:string)=>void}){
  const action=(id:string,label:string,icon:React.ReactNode,onClick:()=>void)=>({id,label:<></>,ariaLabel:label,icon,onClick});
  return <OverflowActions className="sol-note-actions" size="sm" collapseOnAction openLabel={t('Actions de la note','Note actions')} closeLabel={t('Réduire les actions','Collapse actions')} classNames={{track:'sol-note-action-track',action:'notes-tool'}}
    primaryActions={[action('focus',t('Mode concentration','Focus mode'),focus?<Minimize2/>:<Maximize2/>,()=>setFocus(!focus)),...(note.id?[action('pin',note.pinned?t('Détacher','Unpin'):t('Épingler','Pin'),<Pin/>,()=>act(note.pinned?'unpin':'pin'))]:[])]}
    overflowActions={note.id?[action('archive',note.archived?t('Désarchiver','Unarchive'):t('Archiver','Archive'),<AnimatedIcon name="archive"/>,()=>act(note.archived?'unarchive':'archive')),action('review',note.reviewed?t('À revoir','Review again'):t('Marquer comme revue','Mark reviewed'),<AnimatedIcon name="check"/>,()=>act(note.reviewed?'unreview':'review')),action('duplicate',t('Dupliquer','Duplicate'),<AnimatedIcon name="copy"/>,()=>act('duplicate')),action('export',t('Exporter cette note','Export this note'),<AnimatedIcon name="download"/>,()=>{flush();send({type:'notes.export',id:note.id,query:'markdown'})}),action('copy',t('Copier le Markdown','Copy Markdown'),<Clipboard/>,()=>navigator.clipboard.writeText(note.text).catch(()=>{})),action('trash',t('Corbeille','Trash'),<Trash2/>,()=>act('trash'))]:[]}/>
}
