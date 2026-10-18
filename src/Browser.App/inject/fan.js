// Éventail d'actions au coin des éléments d'une page : texte sélectionné, liens en forme de carte, images, blocs de code.
// Géométrie et ressorts du bloc « Action node » de Bencho (Copyright (c) 2026 Lorenzo Cabra, MIT ; voir
// ui/licenses/bencho-MIT.txt). Icônes : tracés de Lucide (ISC). Isolé dans un Shadow DOM fermé : la page ne peut ni
// le styler ni le lire. Chaque message porte une clé que Sol a écrite dans ce script : une page ne peut pas l'imiter.
(() => {
  if (window.top !== window || window.__solFan) return;
  window.__solFan = true;
  const KEY = '__SOL_FAN_KEY__';
  const webview = window.chrome?.webview;
  if (!webview) return;
  const post = webview.postMessage.bind(webview);
  const fr = /^fr\b/i.test(navigator.language || '');
  const T = (a, b) => (fr ? a : b);
  const still = matchMedia('(prefers-reduced-motion: reduce)').matches;

  /* ── la géométrie du bloc ─────────────────────────────────
     Les boutons suivent le contour de l'élément décalé d'un écart constant : le long du bord haut, autour du coin
     sur un arc concentrique, puis le long du bord droit. Rangés sous l'élément, ils sortent du coin. */
  const DOT = 32, R = DOT / 2, SPACING = DOT + 10, GAP = 10;
  const ride = (s, corner, off) => {
    const arc = Math.max(0.001, corner + off), len = (arc * Math.PI) / 2;
    if (s <= 0) return { x: -corner + s, y: -off };
    if (s >= len) return { x: off, y: corner + (s - len) };
    const th = Math.PI / 2 - s / arc;
    return { x: -corner + arc * Math.cos(th), y: corner - arc * Math.sin(th) };
  };
  const hideAt = c => (c >= R + 2 ? c * Math.SQRT2 : Math.max(R * Math.SQRT2 + 2, c * Math.SQRT2 + c + 2));

  /* ── icônes (Lucide, 24 × 24) ── */
  const ICONS = {
    tab: '<rect x="2" y="4" width="20" height="16" rx="2"/><path d="M10 4v4M2 8h20M6 4v4"/>',
    split: '<rect x="3" y="3" width="18" height="18" rx="2"/><path d="M12 3v18"/>',
    link: '<path d="M9 17H7A5 5 0 0 1 7 7h2M15 7h2a5 5 0 1 1 0 10h-2"/><line x1="8" y1="12" x2="16" y2="12"/>',
    copy: '<rect x="8" y="8" width="14" height="14" rx="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/>',
    image: '<rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="9" cy="9" r="2"/><path d="m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21"/>',
    explain: '<path d="M15 14c.2-1 .7-1.7 1.5-2.5 1-.9 1.5-2.2 1.5-3.5A6 6 0 0 0 6 8c0 1 .2 2.2 1.5 3.5.7.7 1.3 1.5 1.5 2.5M9 18h6M10 22h4"/>',
    /* la mascotte de Sol AI, en trait : une balle carrée et ses deux yeux */
    sol: '<rect x="3" y="3" width="18" height="18" rx="6"/><rect x="8.5" y="9" width="2" height="5" rx="1" fill="currentColor"/><rect x="13.5" y="9" width="2" height="5" rx="1" fill="currentColor"/>',
    more: '<circle cx="5" cy="12" r="1.2"/><circle cx="12" cy="12" r="1.2"/><circle cx="19" cy="12" r="1.2"/>',
    note: '<path d="M13.4 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-7.4M2 6h4M2 10h4M2 14h4M2 18h4"/><path d="M21.378 5.626a1 1 0 1 0-3.004-3.004l-5.01 5.012a2 2 0 0 0-.506.854l-.837 2.87a.5.5 0 0 0 .62.62l2.87-.837a2 2 0 0 0 .854-.506z"/>',
  };

  /* ── ce qui reçoit un éventail ─────────────────────────────
     Un lien seulement s'il a l'allure d'une carte : un lien dans une phrase garde la barre de sélection et le clic
     droit, sinon chaque paragraphe se couvrirait de boutons. */
  const web = u => /^https?:/i.test(u || '');
  const kindOf = el => {
    for (let n = el, depth = 0; n && n !== document.body && depth < 6; n = n.parentElement, depth++) {
      if (n.tagName === 'PRE' && n.offsetHeight >= 48) return { el: n, kind: 'code' };
      if (n.tagName === 'IMG' && n.offsetWidth >= 120 && n.offsetHeight >= 90 && web(n.currentSrc || n.src)) return { el: n, kind: 'image' };
      if (n.tagName === 'A' && web(n.href) && n.offsetWidth >= 140 && n.offsetHeight >= 56) return { el: n, kind: 'link' };
    }
    return null;
  };
  const actionsFor = ({ el, kind, text }) => kind === 'selection'
    /* la sélection : copier, Sol AI, et « Plus » qui ouvre le menu du clic droit pour tout le reste */
    ? [['copy', T('Copier', 'Copy'), text], ['sol', T('Sol AI', 'Sol AI'), text], ['more', T('Plus d’actions…', 'More actions…'), text]]
    : kind === 'link'
    ? [['tab', T('Ouvrir dans un nouvel onglet', 'Open in a new tab'), el.href], ['split', T('Ouvrir en vue scindée', 'Open in split view'), el.href], ['link', T('Copier le lien', 'Copy link'), el.href]]
    : kind === 'image'
      ? [['tab', T("Ouvrir l'image", 'Open image'), el.currentSrc || el.src], ['link', T("Copier le lien de l'image", 'Copy image link'), el.currentSrc || el.src], ['note', T('Ajouter à une note', 'Add to a note'), el.currentSrc || el.src]]
      : [['copy', T('Copier le code', 'Copy code'), el.innerText], ['explain', T('Expliquer avec Sol AI', 'Explain with Sol AI'), el.innerText], ['note', T('Ajouter à une note', 'Add to a note'), el.innerText]];

  /* ── la vue : un hôte fixe, une racine fermée ── */
  const host = document.createElement('sol-fan');
  const root = host.attachShadow({ mode: 'closed' });
  root.innerHTML = `<style>
    :host { all: initial; position: fixed; inset: 0; pointer-events: none; z-index: 2147483646; }
    .pad { position: fixed; pointer-events: none; }
    .pad.on { pointer-events: auto; }
    button { all: unset; position: fixed; width: ${DOT}px; height: ${DOT}px; margin: -${R}px 0 0 -${R}px; border-radius: 999px;
      display: grid; place-items: center; box-sizing: border-box; cursor: pointer; pointer-events: auto;
      background: rgba(28, 28, 32, .92); color: #fff; box-shadow: 0 0 0 1px rgba(255,255,255,.14), 0 4px 14px rgba(0,0,0,.28);
      backdrop-filter: blur(10px); transition: scale 160ms cubic-bezier(.24,1.34,.38,1); }
    button:hover { scale: 1.12; } button:focus-visible { outline: 2px solid #4c8dff; outline-offset: 2px; }
    button[hidden] { display: none; }
    svg { width: 15px; height: 15px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; }
    @media (prefers-color-scheme: light) { button { background: rgba(255,255,255,.95); color: #18181b; box-shadow: 0 0 0 1px rgba(0,0,0,.12), 0 4px 14px rgba(0,0,0,.16); } }
  </style><i class="pad"></i>`;
  const pad = root.querySelector('.pad');
  const buttons = [0, 1, 2].map(() => { const b = document.createElement('button'); b.hidden = true; root.append(b); return b; });
  const attach = () => { if (!host.isConnected) (document.documentElement || document).append(host); };

  /* ── une ressort par bouton, l'échelonnement est un délai sur la cible ── */
  const springs = buttons.map(() => ({ at: 0, v: 0, target: 0 }));
  let current = null, open = false, raf = 0, prev = 0, showTimer = 0, hideTimer = 0;
  const rectOf = c => (c.range ? c.range.getBoundingClientRect() : c.el.getBoundingClientRect());
  const place = () => {
    if (!current) return;
    const r = rectOf(current);
    const corner = current.el ? Math.min(16, parseFloat(getComputedStyle(current.el).borderTopRightRadius) || 0) : 0;
    const off = GAP + R, n = current.actions.length, mid = ((corner + off) * Math.PI) / 4, deep = hideAt(corner) / Math.SQRT2;
    let l = 0, t = 0, rr = 0, d = 0;
    current.actions.forEach((_, i) => {
      const spot = ride(mid + (i - (n - 1) / 2) * SPACING, corner, off), u = springs[i].at;
      const x = r.right + (-deep + (spot.x + deep) * u), y = r.top + (deep + (spot.y - deep) * u);
      const b = buttons[i];
      b.style.left = `${x}px`; b.style.top = `${y}px`; b.style.scale = `${0.82 + 0.18 * Math.min(u, 1)}`;
      b.style.visibility = u < 0.005 && springs[i].target === 0 ? 'hidden' : 'visible';
      l = Math.min(l, spot.x - R - 6); rr = Math.max(rr, spot.x + R + 6); t = Math.min(t, spot.y - R - 6); d = Math.max(d, spot.y + R + 6);
    });
    /* le pad comble l'air entre l'élément et l'éventail, pour que le pointeur puisse aller jusqu'aux boutons */
    Object.assign(pad.style, { left: `${r.right + l}px`, top: `${r.top + t}px`, width: `${rr - l}px`, height: `${d - t}px` });
  };
  const tick = now => {
    const dt = prev ? Math.min(2.5, (now - prev) / 16.67) : 1; prev = now;
    let moving = false;
    springs.forEach(s => {
      if (still) { s.at = s.target; s.v = 0; return; }
      s.v += (s.target - s.at) * 0.11 * dt; s.v *= Math.pow(0.66, dt); s.at += s.v * dt;
      if (Math.abs(s.target - s.at) < 0.002 && Math.abs(s.v) < 0.002) { s.at = s.target; s.v = 0; } else moving = true;
    });
    place();
    /* la boucle s'arrête dès que tout est posé ; le défilement repositionne ensuite */
    raf = moving ? requestAnimationFrame(tick) : 0;
    if (!moving && !open) { buttons.forEach(b => (b.hidden = true)); current = null; }
  };
  const run = () => { if (!raf) { prev = 0; raf = requestAnimationFrame(tick); } };
  const show = target => {
    attach();
    current = { ...target, actions: actionsFor(target) };
    current.actions.forEach(([id, label], i) => {
      const b = buttons[i]; b.hidden = false; b.title = label; b.setAttribute('aria-label', label);
      b.innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true">${ICONS[id]}</svg>`; b.dataset.i = String(i);
    });
    buttons.slice(current.actions.length).forEach(b => (b.hidden = true));
    open = true; pad.classList.add('on');
    springs.forEach((s, i) => setTimeout(() => { if (open) { s.target = i < current.actions.length ? 1 : 0; run(); } }, still ? 0 : i * 45));
  };
  const hide = () => {
    open = false; pad.classList.remove('on');
    const n = current?.actions.length ?? 0;
    springs.forEach((s, i) => setTimeout(() => { if (!open) { s.target = 0; run(); } }, still ? 0 : (n - 1 - i) * 45));
  };

  /* ── le pointeur : un temps d'arrêt avant d'ouvrir, un peu de grâce avant de refermer ── */
  const inside = e => { const p = e.composedPath(); return p.includes(host) || (!!current?.el && p.includes(current.el)); };
  addEventListener('pointerover', e => {
    if (!e.isTrusted || e.pointerType === 'touch') return;
    if (inside(e)) { clearTimeout(hideTimer); return; }
    /* l'éventail d'une sélection reste tant que la sélection existe */
    if (open && current?.kind === 'selection') return;
    const target = kindOf(e.target);
    clearTimeout(showTimer);
    if (!target) { if (open) { clearTimeout(hideTimer); hideTimer = setTimeout(hide, 250); } return; }
    if (current?.el === target.el && open) return;
    showTimer = setTimeout(() => { if (open && current?.el !== target.el) { hide(); setTimeout(() => show(target), 160); } else show(target); }, 450);
  }, true);
  addEventListener('pointerout', e => { if (e.isTrusted && open && current?.kind !== 'selection' && !e.relatedTarget) { clearTimeout(hideTimer); hideTimer = setTimeout(hide, 250); } }, true);

  /* ── la sélection : l'éventail s'ouvre tout de suite au coin du texte choisi ──
     Pas dans un champ de saisie : là, le menu contextuel propose déjà Corriger, Reformuler et Traduire. */
  const selected = () => {
    const s = getSelection();
    const text = s && !s.isCollapsed ? s.toString().trim() : '';
    const active = document.activeElement;
    if (!text || text.length > 50000 || active?.matches?.('input,textarea') || active?.isContentEditable) return null;
    const range = s.getRangeAt(0).cloneRange(), r = range.getBoundingClientRect();
    return r.width || r.height ? { kind: 'selection', range, text } : null;
  };
  const onSelect = () => {
    const target = selected();
    if (!target) return;
    clearTimeout(showTimer); clearTimeout(hideTimer);
    if (open) { hide(); setTimeout(() => show(target), 120); } else show(target);
  };
  addEventListener('mouseup', e => { if (e.isTrusted && e.button === 0 && !e.composedPath().includes(host)) setTimeout(onSelect, 10); }, true);
  addEventListener('keyup', e => { if (e.isTrusted && e.shiftKey) setTimeout(onSelect, 10); }, true);
  document.addEventListener('selectionchange', () => { if (open && current?.kind === 'selection' && getSelection()?.isCollapsed) hide(); });
  addEventListener('scroll', () => { if (current) place(); }, { passive: true, capture: true });
  addEventListener('resize', () => { if (current) place(); }, { passive: true });
  addEventListener('keydown', e => { if (e.key === 'Escape' && open) hide(); }, true);

  /* un appui sur l'éventail ne doit pas effacer la sélection qu'il sert */
  root.addEventListener('mousedown', e => e.preventDefault());

  /* ── un clic part vers Sol, avec la clé ── */
  root.addEventListener('click', e => {
    const b = e.target.closest?.('button');
    if (!b || !current || !e.isTrusted) return;
    const [action, , value] = current.actions[Number(b.dataset.i)];
    const r = rectOf(current);
    post(JSON.stringify({ sol: 'fan', key: KEY, kind: current.kind, action, value: String(value).slice(0, 50000),
      x: r.right, y: r.top, ratio: devicePixelRatio, title: document.title, language: document.documentElement.lang || '' }));
    hide();
  });
})();
