import { send } from './bridge';
import { t } from './i18n';
import { Icon } from './Icons';

export type Download = { id: string; name: string; received: number; total: number | null; state: 'InProgress' | 'Interrupted' | 'Completed' };

const size = (bytes: number) =>
  bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} ${t('Ko', 'KB')}` : `${(bytes / 1024 / 1024).toFixed(1)} ${t('Mo', 'MB')}`;

export const percent = (d: Download) => (d.total && d.total > 0 ? Math.round((d.received / d.total) * 100) : null);

function status(d: Download) {
  if (d.state === 'Completed') return `${t('Terminé', 'Done')} · ${size(d.received)}`;
  if (d.state === 'Interrupted') return t('Interrompu', 'Interrupted');
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
          <span className="row-title">{d.name}</span>
          <span className="row-sub">{status(d)}</span>
        </div>
        <div className="download-actions">
          {d.state === 'InProgress' && (
            <button className="pill-action" onClick={() => send({ type: 'download.cancel', id: d.id })}>
              {t('Annuler', 'Cancel')}
            </button>
          )}
          {d.state === 'Completed' && (
            <>
              <button className="pill-action primary" onClick={() => send({ type: 'download.open', id: d.id })}>
                {t('Ouvrir', 'Open')}
              </button>
              <button className="pill-action" onClick={() => send({ type: 'download.reveal', id: d.id })}>
                {t('Dossier', 'Folder')}
              </button>
            </>
          )}
        </div>
      </div>
      {d.state === 'InProgress' && (
        // Taille inconnue : la barre défile au lieu d'afficher un pourcentage inventé.
        <div className={`download-bar${pct === null ? ' unknown' : ''}`} role="progressbar" aria-valuenow={pct ?? undefined} aria-valuemin={0} aria-valuemax={100}>
          <div className="download-bar-fill" style={pct === null ? undefined : { width: `${pct}%` }} />
        </div>
      )}
    </div>
  );
}
