import {Icon} from './Icons';
import {t} from './i18n';
import './saved-site-actions.css';

export default function DeleteSavedSite({collection, url, title, onDelete}: {
  collection: 'bookmarks' | 'history'; url: string; title: string; onDelete: () => void;
}) {
  const name = title || url;
  const label = collection === 'bookmarks'
    ? t(`Supprimer le favori « ${name} »`, `Delete bookmark “${name}”`)
    : t(`Supprimer de l’historique « ${name} »`, `Delete “${name}” from history`);
  return <button type="button" className="saved-item-delete" data-collection={collection} data-url={url}
    title={label} aria-label={label} onClick={onDelete}>
    <Icon d="M3 4.5h10M6 4.5V2.7h4v1.8M4.2 4.5l.6 9h6.4l.6-9M6.5 7v4M9.5 7v4" size={14}/>
  </button>;
}
