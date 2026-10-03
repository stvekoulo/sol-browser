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

// Marque GitHub, dans la couleur du texte : lisible en clair comme en sombre.
export function GithubLogo() {
  return (
    <svg width="18" height="18" viewBox="0 0 16 16" fill="currentColor" aria-hidden>
      <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0 0 16 8c0-4.42-3.58-8-8-8z" />
    </svg>
  );
}

// DevBenin : pas de logo fourni, donc une pastille aux couleurs du drapeau béninois.
export function DevBeninLogo() {
  return (
    <svg width="18" height="18" viewBox="0 0 16 16" aria-hidden>
      <clipPath id="devbenin-shape">
        <rect width="16" height="16" rx="4.5" />
      </clipPath>
      <g clipPath="url(#devbenin-shape)">
        <rect width="6.4" height="16" fill="#008751" />
        <rect x="6.4" width="9.6" height="8" fill="#FCD116" />
        <rect x="6.4" y="8" width="9.6" height="8" fill="#E8112D" />
      </g>
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
