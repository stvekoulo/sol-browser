(() => {
  // Débloque l'interactivité sur Prime Video et Amazon face aux bloqueurs et modales fantômes.
  if (!/(^|\.)primevideo\.com$|(^|\.)amazon\.[a-z.]+$/.test(location.hostname)) return;

  const fixInteractivity = () => {
    // 1. Détecte le formulaire de cookies et clique sur accepter si présent.
    const forms = document.querySelectorAll('form[action*="_cookienotf_"], form[action*="cookie-preferences"]');
    for (const f of forms) {
      const btn = f.querySelector('input[type="submit"][name*="accept"], button[name*="accept"], input[name*="acceptCookies"], [data-action="accept-cookies"]');
      if (btn && btn.offsetParent !== null) {
        btn.click();
        break;
      }
    }

    // 2. Supprime les masques opaques ou calques fantômes bloquant les clics.
    const selectors = [
      '[data-testid="critical-notification"]',
      '.a-modal-scroller',
      '.a-modal-backdrop',
      '.dv-overlay',
      '.fg-overlay',
      '[class*="CriticalNotificationOverlay"]',
      '[class*="overlay-backdrop"]'
    ];
    for (const sel of selectors) {
      document.querySelectorAll(sel).forEach(el => {
        const visibleChild = Array.from(el.querySelectorAll('button, a, input, form')).some(c => c.offsetParent !== null);
        if (!visibleChild && (el.offsetWidth >= window.innerWidth * 0.8 || el.offsetHeight >= window.innerHeight * 0.8)) {
          el.style.setProperty('pointer-events', 'none', 'important');
          el.style.setProperty('display', 'none', 'important');
        }
      });
    }

    // 3. Rétablit le défilement et les clics du document.
    if (document.body) {
      const bStyle = getComputedStyle(document.body);
      if (bStyle.pointerEvents === 'none') document.body.style.setProperty('pointer-events', 'auto', 'important');
      if (bStyle.overflow === 'hidden' && !document.querySelector('.a-modal-scroller:not([style*="display: none"])')) {
        document.body.style.setProperty('overflow', 'auto', 'important');
      }
    }

    // 4. S'assure que les profils et le lecteur vidéo reçoivent bien les clics.
    document.querySelectorAll('.profile-card, [data-testid="profile-card"], [class*="profileCard"], .webPlayerSDKContainer, video').forEach(el => {
      if (getComputedStyle(el).pointerEvents === 'none') {
        el.style.setProperty('pointer-events', 'auto', 'important');
      }
    });
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', fixInteractivity);
  } else {
    fixInteractivity();
  }
  const timer = setInterval(fixInteractivity, 600);
  setTimeout(() => clearInterval(timer), 12000);
})();
