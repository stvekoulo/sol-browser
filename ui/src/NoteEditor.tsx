import { useRef,useState } from 'react';
import {Input} from '@/components/motion/input';
import {Button} from '@/components/motion/button/base';
import SolTabs from './SolTabs';
import NoteFold from './NoteFold';
import NoteActions from './NoteActions';
import { send } from './bridge';
import { t } from './i18n';
import { Icon } from './Icons';
import NoteMarkdown from './NoteMarkdown';
import NoteAttachments from './NoteAttachments';
import NoteAudio,{attachNoteBlob} from './NoteAudio';
import RichNoteEditor from './RichNoteEditor';
import NoteToolbar,{insertNote} from './NoteToolbar';
import { createdOf,dirtyNote,references,titleOf,type Note,type NotesContent } from './notesModel';

const localDate=(value:string)=>{const date=new Date(value);return Number.isNaN(date.getTime())?'':new Date(date.getTime()-date.getTimezoneOffset()*60000).toISOString().slice(0,16)};
export default function NoteEditor({data,change,meta,edit,close,focus,setFocus,flush}:{data:NotesContent;change:(field:'title'|'text',value:string)=>void;meta:(value:Partial<Pick<Note,'kind'|'tags'|'due'>>)=>void;edit:(id:string)=>void;close:()=>void;focus:boolean;setFocus:(value:boolean)=>void;flush:()=>void}){
  const editor=useRef<HTMLTextAreaElement>(null),[preview,setPreview]=useState(false),[picker,setPicker]=useState(false),[find,setFind]=useState(''),[comment,setComment]=useState(''),[selectedVersion,setSelectedVersion]=useState<number|null>(null);
  const note=data.draft,dirty=dirtyNote(data),backlinks=data.items.filter(n=>n.id!==note.id&&!n.deleted&&references(n,data.items).some(ref=>ref.id===note.id));
  const [mode,setMode]=useState<'source'|'wysiwyg'|'ir'>(()=>{const saved=localStorage.getItem('sol.notes.editor');return saved==='source'||saved==='ir'?saved:'wysiwyg'});
  const act=(query:string)=>{flush();send({type:'notes.action',input:JSON.stringify([note.id]),query})};
  return <section className="notes-document" aria-label={t('Éditeur de note','Note editor')} onDragOver={e=>{if(e.dataTransfer.types.includes('Files'))e.preventDefault()}} onDrop={e=>{if(!e.defaultPrevented&&e.dataTransfer.files.length){e.preventDefault();for(const file of e.dataTransfer.files)attachNoteBlob(file,file.name,note.id)}}} onPaste={e=>{if(!e.defaultPrevented&&e.clipboardData.files.length){e.preventDefault();for(const file of e.clipboardData.files)attachNoteBlob(file,file.name,note.id)}}}>
    <header className="notes-document-toolbar"><button className="notes-tool" title={t('Revenir aux notes','Back to notes')} aria-label={t('Revenir aux notes','Back to notes')} onClick={()=>{flush();close()}}><Icon d="M10 3L5 8l5 5"/></button>
      <span className="note-save-status" aria-live="polite">{dirty?t('Enregistrement…','Saving…'):data.privateMode?t('Note privée','Private note'):t('Enregistré','Saved')}</span>
      <div className="notes-toolbar-actions"><button className="pill-action" aria-pressed={preview} onClick={()=>setPreview(!preview)}>{preview?t('Modifier','Edit'):t('Aperçu','Preview')}</button>
        <button className="pill-action" disabled={!note.text.trim()&&!note.files.length} onClick={flush}>{t('Enregistrer','Save')}</button>
        <NoteActions {...{note,focus,setFocus,flush,act}}/>
      </div></header>
    <div className="notes-paper">
      {data.error&&<p role="alert" className="notes-error">{data.error}</p>}
      <div className="note-mode-controls"><SolTabs className="note-editor-modes" value={mode} change={next=>{setMode(next as typeof mode);localStorage.setItem('sol.notes.editor',next)}} label={t('Mode de rédaction','Editor mode')} items={[{value:'wysiwyg',label:t('Texte riche','Rich text')},{value:'ir',label:t('Rendu instantané','Instant rendering')},{value:'source',label:'Markdown'}]}/><SolTabs className="note-kind" value={note.kind} change={next=>meta({kind:next as Note['kind']})} label={t('Type de note','Note type')} items={[{value:'idea',label:t('Idée','Idea')},{value:'note',label:t('Note','Note')},{value:'task',label:t('Tâche','Task')}]}/></div>
      <Input className="note-title-control" classNames={{field:"note-title-field",input:"notes-title"}} aria-label={t('Titre','Title')} placeholder={t('Titre de la note','Note title')} maxLength={120} value={note.title} onChange={value=>change('title',value)}/>
      <div className="note-metadata"><label>Tags<input key={note.id+note.tags.join(',')} defaultValue={note.tags.join(', ')} aria-label="Tags" placeholder="#projet, #idées" onBlur={e=>meta({tags:[...new Set(e.target.value.split(/[,\s]+/).map(s=>s.replace(/^#/,'' )).filter(Boolean))].slice(0,40)})}/></label>{note.kind==='task'&&<><label>{t('Échéance','Due date')}<input type="datetime-local" aria-label={t('Échéance','Due date')} value={note.due?localDate(note.due):''} onChange={e=>meta({due:e.target.value?new Date(e.target.value).toISOString():null})}/></label>{note.id&&<button className="pill-action" aria-pressed={note.reviewed} onClick={()=>act(note.reviewed?'unreview':'review')}>{note.reviewed?t('Terminée','Done'):t('Marquer terminée','Mark done')}</button>}</>}</div>
      {!preview&&(mode==='source'?<NoteToolbar editor={editor} change={text=>change('text',text)} reference={()=>setPicker(!picker)}/>:<div className="note-format"><button className="pill-action" onClick={()=>{setMode('source');setPicker(!picker)}}>{t('Lier une note','Link a note')}</button><button className="pill-action" onClick={()=>send({type:'notes.attach'})}>{t('Joindre un fichier','Attach a file')}</button></div>)}
      {picker&&<div className="note-reference-picker"><input type="search" aria-label={t('Chercher une note à lier','Find a note to link')} placeholder={t('Rechercher une note…','Find a note…')} value={find} onChange={e=>setFind(e.target.value)}/>{data.items.filter(n=>!n.deleted&&n.id!==note.id&&titleOf(n).toLocaleLowerCase().includes(find.toLocaleLowerCase())).map(n=><button key={n.id} onClick={()=>{insertNote(editor.current,text=>change('text',text),'[[',']]',n.id,true);setPicker(false)}}>{titleOf(n)}</button>)}</div>}
      {preview?<NoteMarkdown note={note} all={data.items} change={text=>change('text',text)} edit={edit}/>:mode!=='source'?<RichNoteEditor value={note.text} mode={mode} noteId={note.id} change={text=>change('text',text)} save={flush}/>:<textarea ref={editor} className="notes-writing" aria-label={t('Contenu','Content')} placeholder={t('Écrivez votre idée… Markdown, #tags et [[liens entre notes]].','Capture an idea… Markdown, #tags and [[note links]].')} maxLength={50000} value={note.text} onChange={e=>change('text',e.target.value)} onKeyDown={e=>{if(e.ctrlKey&&(e.key.toLowerCase()==='b'||e.key.toLowerCase()==='i')){e.preventDefault();insertNote(editor.current,text=>change('text',text),e.key.toLowerCase()==='b'?'**':'_',e.key.toLowerCase()==='b'?'**':'_','texte')}if(e.ctrlKey&&e.key==='Enter'){e.preventDefault();flush()}}}/>}
      <NoteAttachments files={data.attachments??[]}/>
      <NoteAudio id={note.id}/>
      {note.url&&<button className="pill-action notes-source-link" title={note.url} onClick={()=>send({type:'notes.link',url:note.url})}>{t('Ouvrir la source','Open source')} ↗</button>}
      {backlinks.length>0&&<div className="note-backlinks"><h3>{t('Liens entrants','Backlinks')}</h3>{backlinks.map(n=><button key={n.id} className="pill-action" onClick={()=>edit(n.id)}>{titleOf(n)}</button>)}</div>}
      {note.id&&<div className="note-details">
        <NoteFold title={<>{t('Historique des versions','Version history')} · {note.history.length}</>}>{note.history.map((h,i)=>({h,i})).reverse().map(({h,i})=><div key={i} className="note-history-item"><button onClick={()=>setSelectedVersion(selectedVersion===i?null:i)}>{new Date(h.updated).toLocaleString()} · {h.title||h.text.slice(0,35)}</button><button className="pill-action" onClick={()=>{flush();send({type:'notes.version',id:note.id,index:i});setSelectedVersion(null)}}>{t('Restaurer','Restore')}</button>{selectedVersion===i&&<pre>{h.text}</pre>}</div>)}{!note.history.length&&<p>{t('Les prochaines modifications seront conservées ici.','Future changes will be kept here.')}</p>}</NoteFold>
        <NoteFold title={<>{t('Annotations','Annotations')} · {note.comments.length}</>}>{note.comments.map(c=><div className="note-comment" key={c.id}><time>{new Date(c.created).toLocaleString()}</time><p>{c.text}</p><button className="notes-tool" aria-label={t('Supprimer l’annotation','Delete annotation')} onClick={()=>send({type:'notes.comment',id:note.id,input:'',query:c.id})}>×</button></div>)}<textarea aria-label={t('Nouvelle annotation','New annotation')} value={comment} maxLength={5000} onChange={e=>setComment(e.target.value)}/><button className="pill-action" disabled={!comment.trim()} onClick={()=>{send({type:'notes.comment',id:note.id,input:comment});setComment('')}}>{t('Ajouter','Add')}</button></NoteFold>
        <NoteFold title={<>{t('Dates de la note','Note dates')}</>}><label>{t('Création','Created')}<input type="datetime-local" aria-label={t('Date de création','Creation date')} value={localDate(createdOf(note))} onChange={e=>{if(e.target.value){flush();send({type:'notes.dates',id:note.id,title:new Date(e.target.value).toISOString(),query:note.updated})}}}/></label><label>{t('Modification','Modified')}<input type="datetime-local" aria-label={t('Date de modification','Modification date')} value={localDate(note.updated)} onChange={e=>{if(e.target.value){flush();send({type:'notes.dates',id:note.id,title:createdOf(note),query:new Date(e.target.value).toISOString()})}}}/></label></NoteFold>
      </div>}
    </div>
  </section>;
}
