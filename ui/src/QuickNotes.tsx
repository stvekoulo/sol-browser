import NoteMarkdown from './NoteMarkdown';
import { useState,useRef } from 'react';
import NoteToolbar from './NoteToolbar';
import NoteAttachments from './NoteAttachments';
import { useNotes } from './useNotes';
import type { NotesContent } from './notesModel';
export type { NotesContent } from './notesModel';
import { send } from './bridge';
import { t } from './i18n';

export default function QuickNotes({ content }: { content: NotesContent }) {
  const {data,change}=useNotes(content,false);
  const editor = useRef<HTMLTextAreaElement>(null);
  const draft=data?.draft??content.draft;
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  const [preview, setPreview] = useState(false);
  return <>
    <div className="notes-heading"><strong>{t('Notes rapides', 'Quick notes')}</strong><button className="pill-action" onClick={()=>send({type:'popup.close'})} aria-label={t('Fermer', 'Close')}>×</button></div>
    <p className="row-hint">{content.privateMode ? t('Privé : conservées en mémoire jusqu’à la fermeture.', 'Private: kept in memory until this window closes.') : t('Vos notes dans ce profil. Le brouillon reste disponible dans cette fenêtre.', 'Notes for this profile. Your draft stays available in this window.')}</p>
    <input className="library-search" aria-label={t('Titre de la note', 'Note title')} placeholder={t('Titre facultatif', 'Optional title')} maxLength={120} value={draft.title} onChange={e=>change('title',e.target.value)} />
    {!preview&&<NoteToolbar editor={editor} change={text=>change('text',text)} reference={()=>{send({type:'notes.manage'})}}/>}
    {preview ? <NoteMarkdown note={draft} all={data?.items??content.items} edit={id=>{send({type:'notes.edit',id});send({type:'notes.manage'})}}/> : <textarea ref={editor} className="notes-text" autoFocus aria-label={t('Contenu de la note', 'Note content')} placeholder={t('Une idée, un passage à conserver…', 'An idea, a passage to keep…')} maxLength={50000} value={draft.text} onChange={e=>change('text',e.target.value)} />}
    <NoteAttachments files={data?.attachments??content.attachments??[]}/>
    {draft.url && <span className="notes-source" title={draft.url}>{draft.url}</span>}
    <div className="notes-actions"><button className="pill-action" aria-pressed={preview} onClick={()=>setPreview(!preview)}>{preview?t('Modifier','Edit'):t('Aperçu','Preview')}</button><button className="pill-action" disabled={!draft.text.trim()&&!draft.files.length} onClick={()=>send({type:'notes.save'})}>{t('Enregistrer', 'Save')}</button>
      {draft.text.length > 0 && <button className="pill-action" onClick={()=> { if (confirmDiscard) send({type:'notes.discard'}); else setConfirmDiscard(true); }}>{confirmDiscard ? t('Confirmer l’abandon', 'Confirm discard') : t('Abandonner', 'Discard')}</button>}
      <button className="pill-action" onClick={()=>send({type:'notes.manage'})}>{t('Ouvrir les notes', 'Open notes')}</button></div>
    {(data?data.error:content.error) && <p role="alert" className="row-hint">{data?data.error:content.error}</p>}
  </>;
}
