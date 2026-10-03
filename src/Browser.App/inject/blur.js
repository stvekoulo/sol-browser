// Flou progressif en haut et en bas de chaque page. Injecté par l'hôte à la création du document.
// API DOM uniquement (pas d'innerHTML) pour passer sur les sites qui imposent Trusted Types.
(() => {
  if (window.top !== window || window.__navBlur) return;
  window.__navBlur = true;
  const HEIGHT = 44, STEPS = [1, 2, 3, 5];
  const build = () => {
    const host = document.createElement('div');
    const root = host.attachShadow({ mode: 'closed' });
    for (const side of ['top', 'bottom']) {
      const band = document.createElement('div');
      band.style.cssText = `position:fixed;left:0;right:0;${side}:0;height:${HEIGHT}px;pointer-events:none;z-index:2147483647`;
      const dir = side === 'top' ? 'to top' : 'to bottom';
      STEPS.forEach((px, i) => {
        const layer = document.createElement('div');
        const from = (i * 100) / STEPS.length, to = ((i + 1) * 100) / STEPS.length;
        const mask = `linear-gradient(${dir},transparent ${from}%,#000 ${to}%)`;
        layer.style.cssText = `position:absolute;inset:0;backdrop-filter:blur(${px}px);-webkit-mask-image:${mask};mask-image:${mask}`;
        band.appendChild(layer);
      });
      root.appendChild(band);
    }
    document.documentElement.appendChild(host);
    window.__navBlurHost = host; // le navigateur masque les bandes le temps d'une capture d'écran
  };
  if (document.documentElement) build();
  else document.addEventListener('DOMContentLoaded', build, { once: true });
})();
