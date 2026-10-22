import { send } from './bridge';
import { t } from './i18n';
import { Icon } from './Icons';
import {Button} from '@/components/motion/button/base';
import './downloads.css';

export type Download = { id: string; name: string; received: number; total: number | null; state: 'InProgress' | 'Interrupted' | 'Completed';paused?:boolean;canceled?:boolean;canResume?:boolean;reason?:string;phase?:string };

const size = (bytes: number) =>
  bytes < 1024 * 1024 ? `${Math.max(0, Math.round(bytes / 1024))} ${t('Ko', 'KB')}` : `${(bytes / 1024 / 1024).toFixed(1)} ${t('Mo', 'MB')}`;

export const percent = (d: Download) => (d.total && d.total > 0 ? Math.max(0,Math.min(100,Math.round((d.received / d.total) * 100))) : null);
export const pendingDownload=(d:Download)=>!d.canceled&&(d.state==='InProgress'||d.paused||d.canResume);

function status(d: Download) {
  if (d.state === 'Completed') return `${t('Terminé', 'Done')} · ${size(d.received)}`;
  if(d.canceled)return t('Annulé','Canceled');
  if(d.paused)return `${t('En pause','Paused')} · ${size(d.received)}${d.total?` / ${size(d.total)}`:''}`;
  if(d.phase && !d.paused && !d.canceled) {
    const phases:Record<string,string>={'engine.downloader':t('Préparation du moteur vidéo…','Preparing video engine…'),'engine.ffmpeg':t('Préparation de l’assemblage vidéo…','Preparing video merging…'),merge:t('Assemblage des pistes…','Merging tracks…'),protected:t('Vidéo protégée : téléchargement indisponible','Protected video: download unavailable'),auth:t('Accès au site refusé ou connexion nécessaire','Site denied access or sign-in required'),failed:t('Échec du téléchargement vidéo — réessayez ou choisissez une autre source','Video download failed — retry or choose another source')};
    if(phases[d.phase])return phases[d.phase];
  }
  if (d.state === 'Interrupted') {
    const reasons:Record<string,string>={NetworkDisconnected:t('Connexion interrompue','Connection interrupted'),NetworkFailed:t('Erreur réseau','Network error'),NetworkTimeout:t('Délai réseau dépassé','Network timeout'),FileNoSpace:t('Espace disque insuffisant','Not enough disk space'),FileAccessDenied:t('Accès au fichier refusé','File access denied'),ServerForbidden:t('Accès refusé par le serveur','Server denied access'),ServerUnauthorized:t('Connexion au site nécessaire','Sign-in required'),UserShutdown:t('Onglet ou navigateur fermé','Tab or browser closed')};
    return reasons[d.reason??'']||t('Interrompu','Interrupted');
  }
  return d.total ? `${percent(d)} % · ${size(d.total)}` : size(d.received);
}

// Carte d'un téléchargement : partagée par le panneau de la barre et l'encart en bas à droite.
export function DownloadCard({ d }: { d: Download }) {
  const pct = percent(d);
  return (
    <div className="download-card">
      <div className="download-top">
        <span className="row-badge" aria-hidden>
          <Icon d="M8 2.5v7.5M5 7l3 3 3-3M3.5 13h9" size={13} />
        </span>
        <div className="download-text">
          <span className="row-title" title={d.name}>{d.name}</span>
          <span className="row-sub" aria-live="polite">{status(d)}</span>
        </div>
        <div className="download-actions">
          {pendingDownload(d) && <>
            <Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} className="pill-action" disabled={(d.paused||d.state==='Interrupted')&&!d.canResume} aria-label={d.paused||d.state==='Interrupted'?t(`Reprendre ${d.name}`,`Resume ${d.name}`):t(`Mettre en pause ${d.name}`,`Pause ${d.name}`)} onClick={()=>send({type:d.paused||d.state==='Interrupted'?'download.resume':'download.pause',id:d.id})}><Icon name={d.paused||d.state==='Interrupted'?'play':'pause'} size={14}/>{d.paused||d.state==='Interrupted'?t('Reprendre','Resume'):t('Pause','Pause')}</Button>
            <Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} className="pill-action" aria-label={t(`Annuler ${d.name}`,`Cancel ${d.name}`)} onClick={() => send({ type: 'download.cancel', id: d.id })}>
              <Icon name="close" size={14}/>{t('Annuler', 'Cancel')}
            </Button>
          </>}
          {d.state === 'Completed' && (
            <>
              <Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} className="pill-action primary" aria-label={t(`Ouvrir ${d.name}`,`Open ${d.name}`)} onClick={() => send({ type: 'download.open', id: d.id })}>
                <Icon name="file" size={14}/>{t('Ouvrir', 'Open')}
              </Button>
              <Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} className="pill-action" aria-label={t(`Afficher ${d.name} dans le dossier`,`Show ${d.name} in folder`)} onClick={() => send({ type: 'download.reveal', id: d.id })}>
                <Icon name="folder" size={14}/>{t('Dossier', 'Folder')}
              </Button>
            </>
          )}
        </div>
      </div>
      {pendingDownload(d) && (
        // Taille inconnue : la barre défile au lieu d'afficher un pourcentage inventé.
        <div className={`download-bar${pct === null ? ' unknown' : ''}${d.paused||d.state==='Interrupted'?' paused':''}`} role="progressbar" aria-label={d.name} aria-valuenow={pct ?? undefined} aria-valuemin={0} aria-valuemax={100}>
          <div className="download-bar-fill" style={pct === null ? undefined : { width: `${pct}%` }} />
        </div>
      )}
    </div>
  );
}
