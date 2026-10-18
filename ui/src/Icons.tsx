// Icônes vectorielles du navigateur (16x16, alignement optique et épaisseur homogène).
import AnimatedIcon,{type IconName} from './AnimatedIcon';
import {iconNameForPath} from './iconNames';
export function Icon({ d='', name,fill = false, size = 16 }: { d?: string;name?:IconName;fill?: boolean; size?: number }) {
  const animated=name??(!fill?iconNameForPath(d):undefined);
  if(animated)return <AnimatedIcon name={animated} size={size}/>;
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill={fill ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="1.45" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <path d={d} />
    </svg>
  );
}

// Bouclier bicolore affiché à gauche du domaine dans la pilule centrale.
export function ShieldIcon() {
  return (
    <svg className="shield-icon" width="15" height="15" viewBox="0 0 16 16" fill="none" aria-hidden>
      <path d="M8 1.8L3 3.9v4.2c0 3.2 2.1 5.6 5 6.4 2.9-.8 5-3.2 5-6.4V3.9L8 1.8z" stroke="currentColor" strokeWidth="1.35" strokeLinejoin="round" />
      <path d="M8 1.8L3 3.9v4.2c0 3.2 2.1 5.6 5 6.4V1.8z" fill="currentColor" />
    </svg>
  );
}

// Icône du panneau latéral [| ] dans le bouton pilule en haut à gauche.
export function SidebarIcon() {
  return <AnimatedIcon name="sidebar"/>;
}

// Cadenas plein affiché à gauche de l'URL complète dans la carte latérale.
export function LockIcon() {
  return (
    <svg className="lock-icon" width="14" height="14" viewBox="0 0 16 16" fill="none" aria-hidden>
      <path d="M5.2 7V5.2a2.8 2.8 0 1 1 5.6 0V7" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      <rect x="3.5" y="7" width="9" height="6.8" rx="2" fill="currentColor" />
    </svg>
  );
}

// Double feuillet pour copier l'adresse dans la carte latérale.
export function CopyIcon() {
  return <AnimatedIcon name="copy" size={15}/>;
}
