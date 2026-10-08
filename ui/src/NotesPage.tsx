import { NoteAiButton } from './SolAiAction';
import SolMarkdown from './SolMarkdown';
import { useEffect, useState, useRef } from 'react';
import { formatDomain, onMessage, send } from './bridge';
import { t, useLang } from './i18n';
import { Icon } from './Icons';
import type { NotesContent } from './QuickNotes';
import './notes.css';

export default function NotesPage() {
  useLang();
  const editor = useRef<HTMLTextAreaElement>(null);
  const [data, setData] = useState<NotesContent | null>(null);
  const [query, setQuery] = useState('');
  const [writing, setWriting] = useState(false);
  const [preview, setPreview] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  useEffect(()=> {
    const off = onMessage(value=> { const next = value as NotesContent; if(next.popup === 'notes') setData(next); });
    send({type:'notes.ready'}); return off;
  },[]);
  useEffect(()=> { document.title = t('Notes — Sol', 'Notes — Sol'); },[]);
  if (!data) return null;
  const draft = data.draft;
  const saved = data.items.find(n=>n.id === draft.id);
  const dirty = !!draft.text && (!saved || saved.text !== draft.text || saved.title !== draft.title);
  const editing = writing || !!draft.id || !!draft.text;
  const change = (field: 'title' | 'text', value: string) => {
    const next = {...draft,[field]:value}; setData({...data,draft:next});
    send({type:'notes.draft',id:next.id,title:next.title,input:next.text});
  };
  const items = data.items.filter(n=>`${n.title}\n${n.text}\n${n.url}`.toLocaleLowerCase().includes(query.toLocaleLowerCase()));
  const start = () => { setPreview(false); setWriting(true); setConfirmDelete(false); setConfirmDiscard(false); send({type:'notes.edit',id:''}); };
  const dateGroup = (date:string) => {
    const d = new Date(date), now = new Date();
    if(d.toDateString() === now.toDateString()) return t('Aujourd’hui', 'Today');
    return now.getTime() - d.getTime() < 7*86400000 ? t('7 derniers jours', 'Previous 7 days') : t('Plus anciennes', 'Older');
  };
  const groups = [...new Set(items.map(n=>dateGroup(n.updated)))];
  return <main className="notes-workspace">
    <aside className="notes-browser" aria-label={t('Vos notes', 'Your notes')}>
      <header className="notes-browser-heading"><div><h1>{t('Notes', 'Notes')}</h1><span>{data.items.length} {t('notes', 'notes')}</span></div>
        <button className="notes-tool" title={t('Nouvelle note', 'New note')} aria-label={t('Nouvelle note', 'New note')} onClick={start}><Icon d="M10 3l3 3M4 10l6-7 3 3-6 7-4 1zM11 12v2H2V5h3" size={19}/></button>
      </header>
      <div className="notes-search"><Icon d="M7 11a4 4 0 1 1 0-8 4 4 0 0 1 0 8M10 10l3 3"/><input type="search" aria-label={t('Rechercher dans les notes', 'Search notes')} placeholder={t('Rechercher', 'Search')} value={query} onChange={e=>setQuery(e.target.value)}/></div>
      {data.privateMode && <p className="notes-private">{t('Privé · mémoire uniquement', 'Private · memory only')}</p>}
      <div className="notes-browser-list">{items.length === 0 && <p className="notes-empty-list">{query ? t('Aucun résultat.', 'No results.') : t('Vos idées et passages enregistrés apparaîtront ici.', 'Your saved ideas and passages will appear here.')}</p>}
        {groups.map(group=><section key={group}><h2>{group}</h2>{items.filter(n=>dateGroup(n.updated) === group).map(n=><button key={n.id} className={`notes-preview${draft.id === n.id ? ' selected' : ''}`} aria-pressed={draft.id === n.id} onClick={()=>{setWriting(false);setConfirmDelete(false);setConfirmDiscard(false);send({type:'notes.edit',id:n.id});}}>
          <strong>{n.title || n.text.split('\n')[0].slice(0,80)}</strong><span><time>{new Date(n.updated).toLocaleDateString()}</time> {n.text.replaceAll('\n',' ').slice(0,110)}</span>
          {n.url && <small>{formatDomain(n.url)}</small>}
        </button>)}</section>)}
      </div>
      <footer><button className="pill-action" disabled={!data.items.length} onClick={()=>send({type:'notes.export'})}>{t('Exporter les notes', 'Export notes')}</button></footer>
    </aside>
    <section className="notes-document" aria-label={t('Éditeur de note', 'Note editor')}>
      <header className="notes-document-toolbar"><span aria-live="polite">{dirty ? t('Modifications non enregistrées', 'Unsaved changes') : data.privateMode ? t('Notes privées', 'Private notes') : t('Dans ce profil', 'In this profile')}</span><div className="notes-toolbar-actions">{data.aiEnabled && editing && !preview && <NoteAiButton editor={editor}/>}
        {editing && <button className="pill-action" aria-pressed={preview} onClick={()=>setPreview(!preview)}>{preview?t('Modifier','Edit'):t('Aperçu','Preview')}</button>}<button className="pill-action" onClick={()=>send({type:'notes.open',x:700,y:80})}>{t('Note rapide', 'Quick note')}</button>
        {editing && <button className="pill-action" disabled={!draft.text.trim()} onClick={()=>{send({type:'notes.save'});setWriting(false);}}>{t('Enregistrer', 'Save')}</button>}
        {editing && <button className="notes-tool" aria-label={t('Abandonner le brouillon', 'Discard draft')} onClick={()=>{if(confirmDiscard){send({type:'notes.discard'});setWriting(false);setConfirmDiscard(false);}else setConfirmDiscard(true);}}>{confirmDiscard ? t('Abandonner ?', 'Discard?') : <Icon d="M4 4l8 8M12 4l-8 8"/>}</button>}
        {draft.id && <button className="notes-tool" aria-label={t('Supprimer la note', 'Delete note')} onClick={()=>{if(confirmDelete){send({type:'notes.delete',id:draft.id});setWriting(false);setConfirmDelete(false);}else setConfirmDelete(true);}}>{confirmDelete ? t('Supprimer ?', 'Delete?') : <Icon d="M3 4h10M6 4V2h4v2M4 4l.5 10h7L12 4M6.5 7v4M9.5 7v4"/>}</button>}
      </div></header>
      {data.error && <p className="notes-error" role="alert">{data.error}</p>}
      {editing ? <div className="notes-paper"><time className="notes-date">{new Date(draft.updated).toLocaleString()}</time>
        <input className="notes-title" aria-label={t('Titre', 'Title')} placeholder={t('Titre de la note', 'Note title')} maxLength={120} value={draft.title} onChange={e=>change('title',e.target.value)}/>
        {draft.url && <span className="notes-document-source">{draft.url}</span>}
        {preview ? <div className="notes-markdown"><SolMarkdown text={draft.text} onOpen={url=>send({type:'notes.link',url})}/></div> : <textarea ref={editor} className="notes-writing" aria-label={t('Contenu', 'Content')} placeholder={t('Commencez à écrire…', 'Start writing…')} maxLength={50000} value={draft.text} onChange={e=>change('text',e.target.value)}/>}
        {draft.id && draft.url && <button className="pill-action notes-source-link" onClick={()=>send({type:'notes.visit',id:draft.id})}>{t('Ouvrir la source', 'Open source')}</button>}
      </div> : <div className="notes-welcome"><Icon d="M4 2h8v12H4zM6 5h4M6 8h4M6 11h2" size={38}/><h2>{t('Vos notes, à portée de main', 'Your notes, close at hand')}</h2><p>{t('Sélectionnez une note ou gardez une nouvelle idée.', 'Choose a note or keep a new idea.')}</p><button className="pill-action" onClick={start}>{t('Nouvelle note', 'New note')}</button></div>}
    </section>
  </main>;
}
