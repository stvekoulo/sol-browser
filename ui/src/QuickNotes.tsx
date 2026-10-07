import { useState } from 'react';
import { send } from './bridge';
import { t } from './i18n';

type Note = { id: string; title: string; text: string; url: string; updated: string };
export type NotesContent = { popup: 'notes'; items: Note[]; draft: Note; error: string | null; privateMode: boolean };

export default function QuickNotes({ content }: { content: NotesContent }) {
  const [draft, setDraft] = useState(content.draft);
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  const change = (field: 'title' | 'text', value: string) => {
    const next = { ...draft, [field]: value }; setDraft(next);
    send({type:'notes.draft', id:next.id, title:next.title, input:next.text});
  };
  return <>
    <div className="notes-heading"><strong>{t('Notes rapides', 'Quick notes')}</strong><button className="pill-action" onClick={()=>send({type:'popup.close'})} aria-label={t('Fermer', 'Close')}>×</button></div>
    <p className="row-hint">{content.privateMode ? t('Privé : conservées en mémoire jusqu’à la fermeture.', 'Private: kept in memory until this window closes.') : t('Vos notes dans ce profil. Le brouillon reste disponible dans cette fenêtre.', 'Notes for this profile. Your draft stays available in this window.')}</p>
    <input className="library-search" aria-label={t('Titre de la note', 'Note title')} placeholder={t('Titre facultatif', 'Optional title')} maxLength={120} value={draft.title} onChange={e=>change('title',e.target.value)} />
    <textarea className="notes-text" autoFocus aria-label={t('Contenu de la note', 'Note content')} placeholder={t('Une idée, un passage à conserver…', 'An idea, a passage to keep…')} maxLength={50000} value={draft.text} onChange={e=>change('text',e.target.value)} />
    {draft.url && <span className="notes-source" title={draft.url}>{draft.url}</span>}
    <div className="notes-actions"><button className="pill-action" disabled={!draft.text.trim()} onClick={()=>send({type:'notes.save'})}>{t('Enregistrer', 'Save')}</button>
      {draft.text.length > 0 && <button className="pill-action" onClick={()=> { if (confirmDiscard) send({type:'notes.discard'}); else setConfirmDiscard(true); }}>{confirmDiscard ? t('Confirmer l’abandon', 'Confirm discard') : t('Abandonner', 'Discard')}</button>}
      <button className="pill-action" onClick={()=>send({type:'notes.manage'})}>{t('Ouvrir les notes', 'Open notes')}</button></div>
    {content.error && <p role="alert" className="row-hint">{content.error}</p>}
  </>;
}
