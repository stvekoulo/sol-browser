import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { onMessage, send } from './bridge';
import { DownloadCard, percent, type Download } from './Downloads';
import { Icon } from './Icons';

// Encart en bas à droite de la fenêtre : téléchargements en cours. L'hôte envoie la liste ; l'encart annonce sa taille.
export default function Dock() {
  const [items, setItems] = useState<Download[]>([]);
  const [minimized, setMinimized] = useState(false);
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => onMessage(data => setItems((data as { items?: Download[] }).items ?? [])), []);

  useLayoutEffect(() => {
    if (items.length > 0 && root.current) send({ type: 'dock.size', width: root.current.offsetWidth, height: root.current.offsetHeight });
  }, [items, minimized]);

  if (items.length === 0) return null;
  const active = items.filter(d => d.state === 'InProgress');

  if (minimized) {
    // Progression d'ensemble, seulement si toutes les tailles sont connues.
    const known = active.length > 0 && active.every(d => percent(d) !== null);
    const overall = known ? Math.round((active.reduce((s, d) => s + d.received, 0) / active.reduce((s, d) => s + (d.total ?? 0), 0)) * 100) : null;
    return (
      <div ref={root} className="dock dock-mini">
        <button className="dock-mini-btn" aria-label="Afficher les téléchargements" title="Afficher les téléchargements" onClick={() => setMinimized(false)}>
          <Icon d="M8 2.5v7.5M5 7l3 3 3-3M3.5 13h9" size={14} />
          <span>{active.length > 0 ? (overall !== null ? `${overall} %` : `${active.length} en cours`) : 'Terminé'}</span>
        </button>
      </div>
    );
  }

  return (
    <div ref={root} className="dock">
      <div className="popup-header">
        <span className="popup-header-title">{active.length > 0 ? `Téléchargement${active.length > 1 ? 's' : ''} en cours` : 'Téléchargement terminé'}</span>
        <button className="dock-minimize" aria-label="Réduire" title="Réduire" onClick={() => setMinimized(true)}>
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
