import { send } from './bridge';
import { t } from './i18n';
import type { NoteAttachment } from './notesModel';
import {Card} from '@/components/motion/card';
export default function NoteAttachments({files}:{files:NoteAttachment[]}){
  return <div className="note-attachments">{files.map(file=><figure key={file.id}>
    {file.mime.startsWith('image/')&&file.data?<a href={file.data} onClick={e=>{e.preventDefault();const dialog=e.currentTarget.parentElement?.querySelector('dialog');dialog?.showModal()}}><img src={file.data} alt={file.name}/></a>:file.mime.startsWith('audio/')&&file.data?<audio controls src={file.data}/>:<span className="note-file-icon">{file.mime==='application/pdf'?'PDF':'↧'}</span>}
    <figcaption><strong>{file.name}</strong><small>{(file.size/1024/1024).toFixed(2)} Mo</small><div><button className="pill-action" onClick={()=>send({type:'notes.file',query:file.id})}>{t('Enregistrer','Save file')}</button><button className="notes-tool" title={t('Retirer la pièce jointe','Remove attachment')} aria-label={t('Retirer la pièce jointe','Remove attachment')} onClick={()=>send({type:'notes.detach',query:file.id})}>×</button></div></figcaption>
    {file.mime.startsWith('image/')&&file.data&&<dialog className="note-image-view" aria-label={file.name} onClick={e=>{if(e.target===e.currentTarget)e.currentTarget.close()}}><Card size="compact" className="sol-surface-card note-image-card"><button className="notes-tool" autoFocus onClick={e=>e.currentTarget.closest('dialog')?.close()} aria-label={t('Fermer','Close')}>×</button><img src={file.data} alt={file.name}/></Card></dialog>}
  </figure>)}</div>;
}
