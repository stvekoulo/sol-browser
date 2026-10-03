// Icônes vectorielles du navigateur (16x16, alignement optique et épaisseur homogène).
export function Icon({ d, fill = false, size = 16 }: { d: string; fill?: boolean; size?: number }) {
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
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <rect x="2" y="2.5" width="12" height="11" rx="2.5" />
      <path d="M6.2 2.5v11" />
    </svg>
  );
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
  return (
    <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.35" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <rect x="5.5" y="5.5" width="7.5" height="7.5" rx="1.8" />
      <path d="M10.5 5.5V4a1.5 1.5 0 0 0-1.5-1.5H4A1.5 1.5 0 0 0 2.5 4v5A1.5 1.5 0 0 0 4 10.5h1.5" />
    </svg>
  );
}
