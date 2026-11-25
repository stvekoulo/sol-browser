import { useIconLook } from './useIconLook';

export function TabFavicon({ icon, label }: { icon?: string | null; label: string }) {
  const { tone } = useIconLook(icon ?? null);
  return icon
    ? <img className={`favicon${tone}`} src={icon} alt="" draggable={false} />
    : <span className="favicon letter" aria-hidden="true">{label[0]?.toUpperCase()}</span>;
}
