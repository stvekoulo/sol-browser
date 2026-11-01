import { Camera, Paperclip, X } from 'lucide-react';
import { send } from './bridge';
import { t } from './i18n';

export type ChatAttachment = { id: string; name: string; image?: string | null };
const MAX_FILE_BYTES = 30 * 1024 * 1024;
export function readAttachment(file: File) {
  // Un collage d'image n'a parfois qu'un nom générique ; l'hôte choisit le traitement d'après son extension.
  const name = file.name || `capture.${file.type.split('/')[1] || 'png'}`;
  if (file.size > MAX_FILE_BYTES) { send({ type: 'chat.file', id: name, input: '' }); return; }
  const reader = new FileReader();
  reader.onload = () => send({ type: 'chat.file', id: name, input: String(reader.result) });
  reader.readAsDataURL(file);
}
export function AttachmentActions({ disabled }: { disabled: boolean }) {
  return <>
    <button type="button" className="pill-action" disabled={disabled} title={t('Ajouter des images, PDF, Word ou fichiers texte', 'Add images, PDF, Word or text files')} aria-label={t('Joindre des fichiers', 'Attach files')} onClick={() => send({ type: 'chat.attach' })}><Paperclip size={17} /></button>
    <button type="button" className="pill-action" disabled={disabled} title={t('Capturer la partie visible de la page', 'Capture the visible page')} aria-label={t('Joindre une capture de la page', 'Attach a page screenshot')} onClick={() => send({ type: 'chat.capture' })}><Camera size={17} /></button>
  </>;
}
export default function SolChatAttachments({ files, busy, attaching, error, onSend }: { onSend?: () => void; files: ChatAttachment[]; busy: boolean; attaching?: boolean; error?: string | null }) {
  return <div style={{ padding: '0 14px', flexShrink: 0 }}>
    {files.length > 0 && <ul aria-label={t('Pièces jointes', 'Attachments')} style={{ listStyle: 'none', padding: 0, margin: '4px 0', display: 'flex', gap: 8, flexWrap: 'wrap' }}>
      {files.map(file => <li key={file.id} style={{ display: 'flex', alignItems: 'center', gap: 5, maxWidth: '100%' }}>
        {file.image && <img src={file.image} alt={file.name} style={{ width: 48, height: 48, objectFit: 'contain', borderRadius: 6 }} />}
        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', fontSize: 12 }}>{file.name}</span>
        <button className="pill-action" disabled={busy || attaching} aria-label={t(`Retirer ${file.name}`, `Remove ${file.name}`)} onClick={() => send({ type: 'chat.detach', id: file.id })}><X size={14} /></button>
      </li>)}
    </ul>}
    {attaching && <p role="status">{t('Préparation des pièces jointes…', 'Preparing attachments…')}</p>}
    {error && <p role="alert" className="sol-chat-error">{error}</p>}
    {files.length > 0 && onSend && <button className="pill-action" disabled={busy || attaching} onClick={onSend}>{t("Analyser ces pièces jointes", "Analyze these attachments")}</button>}
    {files.length > 0 && <p className="sol-chat-permission">{t('Ces pièces seront envoyées à ChatGPT avec votre question. Vérifiez leur contenu avant l’envoi.', 'These attachments will be sent to ChatGPT with your question. Review their contents before sending.')}</p>}
  </div>;
}
