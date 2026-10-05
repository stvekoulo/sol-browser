import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { interfaceScale, onMessage, send } from './bridge';
import { DownloadCard, percent, type Download } from './Downloads';
import { t, useLang } from './i18n';
import { Icon } from './Icons';

type Update = { version: string; percent: number; ready: boolean };

// Encart en bas à droite de la fenêtre : téléchargements en cours, et mise à jour de Sol. L'hôte envoie la liste ; l'encart annonce sa taille.
export default function Dock() {
  const [items, setItems] = useState<Download[]>([]);
  const [update, setUpdate] = useState<Update | null>(null);
  const [minimized, setMinimized] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  useLang();

  useEffect(() => {
    const off = onMessage(data => {
      const content = data as { items?: Download[]; update?: Update | null };
      if (content.items === undefined && content.update === undefined) return; // thème, langue…
      setItems(content.items ?? []);
      setUpdate(content.update ?? null);
    });
    send({ type: 'dock.ready' }); // créé au premier téléchargement : il demande ce qu'il doit afficher
    return off;
  }, []);

  useLayoutEffect(() => {
    if (!(items.length > 0 || update) || !root.current) return;
    const el = root.current;
    const report = () => send({ type: 'dock.size', width: el.offsetWidth * interfaceScale(), height: el.offsetHeight * interfaceScale() });
    report();
    window.addEventListener('ui-scale', report);
    return () => window.removeEventListener('ui-scale', report);
  }, [items, minimized, update]);

  if (items.length === 0 && !update) return null;

  // Mise à jour de Sol : téléchargement en cours, puis proposition de redémarrer.
  const updateCard = update && (
    <div className="dock-update">
      <span className="row-title">{update.ready ? t(`Sol ${update.version} est prêt`, `Sol ${update.version} is ready`) : t(`Téléchargement de Sol ${update.version}`, `Downloading Sol ${update.version}`)}</span>
      {update.ready ? (
        <>
          <span className="row-sub">{t('Redémarrez Sol pour installer la mise à jour. Vos onglets seront rouverts.', 'Restart Sol to install the update. Your tabs will reopen.')}</span>
          <div className="dock-update-actions">
            <button className="pill-action" onClick={() => send({ type: 'update.later' })}>{t('Plus tard', 'Later')}</button>
            <button className="pill-action primary" onClick={() => send({ type: 'update.restart' })}>{t('Redémarrer', 'Restart')}</button>
          </div>
        </>
      ) : (
        <>
          <span className="row-sub">{update.percent} %</span>
          <span className="dock-update-bar"><span style={{ width: `${update.percent}%` }} /></span>
        </>
      )}
    </div>
  );
  if (items.length === 0) return <div ref={root} className="dock">{updateCard}</div>;
  const active = items.filter(d => d.state === 'InProgress');

  if (minimized) {
    // Progression d'ensemble, seulement si toutes les tailles sont connues.
    const known = active.length > 0 && active.every(d => percent(d) !== null);
    const overall = known ? Math.round((active.reduce((s, d) => s + d.received, 0) / active.reduce((s, d) => s + (d.total ?? 0), 0)) * 100) : null;
    return (
      <div ref={root} className="dock dock-mini">
        <button className="dock-mini-btn" aria-label={t('Afficher les téléchargements', 'Show downloads')} title={t('Afficher les téléchargements', 'Show downloads')} onClick={() => setMinimized(false)}>
          <Icon d="M8 2.5v7.5M5 7l3 3 3-3M3.5 13h9" size={14} />
          <span>{active.length > 0 ? (overall !== null ? `${overall} %` : t(`${active.length} en cours`, `${active.length} in progress`)) : t('Terminé', 'Done')}</span>
        </button>
      </div>
    );
  }

  return (
    <div ref={root} className="dock">
      {updateCard}
      <div className="popup-header">
        <span className="popup-header-title">{active.length > 0 ? t(`Téléchargement${active.length > 1 ? 's' : ''} en cours`, `Download${active.length > 1 ? 's' : ''} in progress`) : t('Téléchargement terminé', 'Download complete')}</span>
        <button className="dock-minimize" aria-label={t('Réduire', 'Minimize')} title={t('Réduire', 'Minimize')} onClick={() => setMinimized(true)}>
          <Icon d="M4 10l4-4 4 4" size={13} />
        </button>
      </div>
      <div className="popup-list">
        {items.map(d => (
          <DownloadCard key={d.id} d={d} />
        ))}
      </div>
    </div>
  );
}
