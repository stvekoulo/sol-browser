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

export function GoogleLogo() {
  return (
    <svg width="17" height="17" viewBox="0 0 16 16" aria-hidden>
      <path fill="#4285F4" d="M14.1 8.15c0-.45-.04-.88-.11-1.3H8v2.46h3.43a2.94 2.94 0 0 1-1.27 1.93v1.6h2.06c1.2-1.11 1.88-2.75 1.88-4.69z" />
      <path fill="#34A853" d="M8 14.4c1.72 0 3.16-.57 4.22-1.55l-2.06-1.6c-.57.38-1.3.61-2.16.61-1.66 0-3.07-1.12-3.57-2.63H2.3v1.65A6.38 6.38 0 0 0 8 14.4z" />
      <path fill="#FBBC05" d="M4.43 9.23a3.84 3.84 0 0 1 0-2.46V5.12H2.3a6.4 6.4 0 0 0 0 5.76l2.13-1.65z" />
      <path fill="#EA4335" d="M8 4.14c.93 0 1.77.32 2.43.95l1.82-1.82C11.15 2.24 9.72 1.6 8 1.6A6.38 6.38 0 0 0 2.3 5.12l2.13 1.65C4.93 5.26 6.34 4.14 8 4.14z" />
    </svg>
  );
}

export function XLogo() {
  return (
    <svg width="18" height="18" viewBox="0 0 16 16" aria-hidden>
      <circle cx="8" cy="8" r="7.2" fill="#141416" />
      <path fill="#ffffff" d="M9.05 7.23L12.18 3.6h-.74L8.72 6.75 6.56 3.6H4.05l3.28 4.78L4.05 12.2h.74l2.86-3.33 2.29 3.33h2.51L9.05 7.23zm-1.02 1.18l-.33-.47-2.65-3.79h1.14l2.14 3.06.33.47 2.78 3.97h-1.14L8.03 8.41z" />
    </svg>
  );
}

export function YoutubeLogo() {
  return (
    <svg width="18" height="13" viewBox="0 0 18 13" aria-hidden>
      <rect width="18" height="13" rx="3.5" fill="#FF0000" />
      <path fill="#ffffff" d="M7.2 3.8l4.5 2.7-4.5 2.7V3.8z" />
    </svg>
  );
}
