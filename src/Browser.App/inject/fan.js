// Éventail d'actions au coin des éléments d'une page : texte sélectionné, liens en forme de carte, images, blocs de code.
// Géométrie du bloc « Action node » de Bencho (Copyright (c) 2026 Lorenzo Cabra, MIT ; voir
// ui/licenses/bencho-MIT.txt). Icônes : tracés de Lucide (ISC). Isolé dans un Shadow DOM fermé : la page ne peut ni
// le styler ni le lire. Chaque message porte une clé que Sol a écrite dans ce script : une page ne peut pas l'imiter.
(() => {
  if (window.top !== window || window.__solFan || location.hostname === 'start.sol') return;
  const KEY = '__SOL_FAN_KEY__';
  const webview = window.chrome?.webview;
  if (!webview) return;
  window.__solFan = true;
  const post = webview.postMessage.bind(webview);
  let config = /*__SOL_FAN_SETTINGS__*/null || { enabled: true, trigger: 0 };
  let pending = null, openTimer = 0;
  const cancelOpen = () => { clearTimeout(openTimer); openTimer = 0; pending = null; };
  webview.addEventListener('message', e => {
    if (e.data?.event !== 'fan-settings') return;
    const next = e.data.fan;
    if (!next || next.enabled === config.enabled && next.trigger === config.trigger) return;
    config = next; cancelOpen(); clearDismissal(); hide();
  });
  const request = target => {
    if (!config.enabled || config.trigger === 2) return;
    if (config.trigger !== 1) { show(target); return; }
    if (pending?.el === target.el && pending?.text === target.text) return;
    cancelOpen(); pending = target;
    openTimer = setTimeout(() => { cancelOpen(); if (!document.hidden && (target.range || target.el.isConnected)) show(target); }, 5000);
  };
  const fr = /^fr\b/i.test(navigator.language || '');
  const T = (a, b) => (fr ? a : b);

  /* ── la géométrie du bloc ─────────────────────────────────
     Les boutons suivent le contour de l'élément décalé d'un écart constant : le long du bord haut, autour du coin
     sur un arc concentrique, puis le long du bord droit. Le ressort d'origine est exécuté par le navigateur. */
  const DOT = 32, R = DOT / 2, SPACING = DOT + 10, GAP = 10;
  const spring = [0];
  let springValue = 0, springVelocity = 0;
  for (let step = 0; step < 180; step++) {
    springVelocity = (springVelocity + (1 - springValue) * .11) * .66; springValue += springVelocity;
    if (Math.abs(1 - springValue) < .002 && Math.abs(springVelocity) < .002) break;
    spring.push(springValue);
  }
  spring.push(1);
  const ride = (s, corner, off) => {
    const arc = Math.max(0.001, corner + off), len = (arc * Math.PI) / 2;
    if (s <= 0) return { x: -corner + s, y: -off };
    if (s >= len) return { x: off, y: corner + (s - len) };
    const th = Math.PI / 2 - s / arc;
    return { x: -corner + arc * Math.cos(th), y: corner - arc * Math.sin(th) };
  };

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
    if (!(el instanceof Element) || el.closest('input,textarea,[contenteditable]:not([contenteditable="false"])')) return null;
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
  // Fermer la racine et enregistrer les gestionnaires avant les scripts du site.
  const create = document.createElement.bind(document), createSvg = document.createElementNS.bind(document);
  const host = create('sol-fan'), root = host.attachShadow({ mode: 'closed' }), append = root.append.bind(root);
  let pad, buttons = [];
  root.addEventListener('mousedown', e => e.preventDefault());
  root.addEventListener('click', onAction);
  // Feuille construite : fonctionne aussi quand le site refuse les styles inline par sa CSP.
  const sheet = new CSSStyleSheet();
  sheet.replaceSync(`
    :host { all: initial; position: fixed; inset: 0; pointer-events: none; z-index: 2147483646; }
    .pad { position: fixed; pointer-events: none; }
    .pad.on { pointer-events: auto; }
    button { all: unset; position: fixed; width: ${DOT}px; height: ${DOT}px; margin: -${R}px 0 0 -${R}px; border-radius: 999px;
      display: grid; place-items: center; box-sizing: border-box; cursor: pointer; pointer-events: auto;
      background: rgba(28, 28, 32, .92); color: #fff; box-shadow: 0 0 0 1px rgba(255,255,255,.14), 0 4px 14px rgba(0,0,0,.28);
      transition: scale 120ms ease-out; }
    button:hover { scale: 1.12; } button:focus-visible { outline: 2px solid #4c8dff; outline-offset: 2px; }
    button[hidden] { display: none; }
    svg { width: 15px; height: 15px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; }
    @media (prefers-color-scheme: light) { button { background: rgba(255,255,255,.95); color: #18181b; box-shadow: 0 0 0 1px rgba(0,0,0,.12), 0 4px 14px rgba(0,0,0,.16); } }
    @media (prefers-reduced-motion: reduce) { button { transition: none; } }
  `);
  root.adoptedStyleSheets = [sheet];
  const attach = () => {
    if (!pad) {
      pad = create('i'); pad.className = 'pad'; append(pad);
      buttons = [0, 1, 2].map(() => { const b = create('button'); b.type = 'button'; b.hidden = true; append(b); return b; });
    }
    if (!host.isConnected) document.documentElement.append(host);
  };
  // Icônes constantes créées par l'API SVG : aucun HTML injecté, compatible avec Trusted Types.
  const icon = id => {
    const ns = 'http://www.w3.org/2000/svg', svg = createSvg(ns, 'svg');
    svg.setAttribute('viewBox', '0 0 24 24'); svg.setAttribute('aria-hidden', 'true');
    for (const [, tag, attributes] of ICONS[id].matchAll(/<(\w+)\s([^>]*?)\/>/g)) {
      const child = createSvg(ns, tag);
      for (const [, name, value] of attributes.matchAll(/([\w-]+)="([^"]*)"/g)) child.setAttribute(name, value);
      svg.append(child);
    }
    return svg;
  };

  /* Une seule vue réutilisée par page. La géométrie ne se recalcule que quand elle change. */
  let current = null, frame = 0, selectionFrame = 0, hideTimer = 0;
  let motions = [], motionEpoch = 0;
  const animateFan = (on, target) => {
    const epoch = ++motionEpoch, still = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const deep = (target.corner >= R + 2 ? target.corner * Math.SQRT2 : Math.max(R * Math.SQRT2 + 2, target.corner * Math.SQRT2 + target.corner + 2)) / Math.SQRT2;
    const starts = buttons.map(button => new DOMMatrix(getComputedStyle(button).transform));
    motions.forEach(animation => animation.cancel());
    motions = buttons.slice(0, target.actions.length).map((button, index) => {
      button.style.pointerEvents = on ? 'auto' : 'none'; button.tabIndex = on ? 0 : -1;
      if (still) { button.style.transform = 'none'; button.hidden = !on; return null; }
      const tucked = { x: -deep - target.spots[index].x, y: deep - target.spots[index].y, scale: .82 };
      const from = on ? tucked : { x: starts[index].e, y: starts[index].f, scale: starts[index].a };
      const to = on ? { x: 0, y: 0, scale: 1 } : tucked;
      return button.animate(spring.map(u => ({ transform: `translate(${from.x + (to.x - from.x) * u}px,${from.y + (to.y - from.y) * u}px) scale(${from.scale + (to.scale - from.scale) * Math.min(u, 1)})` })), {
        duration: (spring.length - 1) * 16.67, delay: (on ? index : target.actions.length - 1 - index) * 45, easing: 'linear', fill: 'both',
      });
    }).filter(Boolean);
    if (!on) Promise.all(motions.map(animation => animation.finished)).then(() => { if (epoch === motionEpoch) buttons.forEach(button => { button.hidden = true; }); }).catch(() => {});
  };
  let dismissed = false;
  const clearDismissal = () => { dismissed = false; removeEventListener('pointermove', resumePointer, true); };
  const resumePointer = e => {
    if (!e.isTrusted || e.pointerType === 'touch' || (!e.movementX && !e.movementY)) return;
    clearDismissal(); const target = kindOf(e.target); if (target) request(target);
  };
  const dismiss = () => {
    dismissed = true; hide();
    addEventListener('pointermove', resumePointer, { capture: true, passive: true });
  };
  const rectOf = c => (c.range ? c.range.getBoundingClientRect() : c.el.getBoundingClientRect());
  const place = () => {
    if (!current) return;
    if (!(current.el || current.range.commonAncestorContainer).isConnected) { hide(); return; }
    const r = rectOf(current);
    if (!r.width || !r.height || r.bottom <= 0 || r.top >= innerHeight || r.right <= 0 || r.left >= innerWidth) { hide(); return; }
    const { spots, bounds } = current;
    const x = Math.max(8 - bounds.l, Math.min(r.right, innerWidth - 8 - bounds.r));
    const y = Math.max(8 - bounds.t, Math.min(r.top, innerHeight - 8 - bounds.b));
    spots.forEach((spot, i) => {
      buttons[i].style.left = `${x + spot.x}px`; buttons[i].style.top = `${y + spot.y}px`;
    });
    Object.assign(pad.style, { left: `${x + bounds.l}px`, top: `${y + bounds.t}px`, width: `${bounds.r - bounds.l}px`, height: `${bounds.b - bounds.t}px` });
  };
  const schedulePlace = () => { if (current && !frame) frame = requestAnimationFrame(() => { frame = 0; place(); }); };
  const show = target => {
    if (!config.enabled) return;
    cancelOpen(); clearDismissal();
    clearTimeout(hideTimer); cancelAnimationFrame(frame); hideTimer = frame = 0;
    attach();
    const actions = actionsFor(target), corner = target.el ? Math.min(16, parseFloat(getComputedStyle(target.el).borderTopRightRadius) || 0) : 0;
    const off = GAP + R, mid = (corner + off) * Math.PI / 4;
    const spots = actions.map((_, i) => ride(mid + (i - (actions.length - 1) / 2) * SPACING, corner, off));
    const bounds = spots.reduce((b, s) => ({ l: Math.min(b.l, s.x - R - 6), r: Math.max(b.r, s.x + R + 6), t: Math.min(b.t, s.y - R - 6), b: Math.max(b.b, s.y + R + 6) }), { l: 0, r: 0, t: 0, b: 0 });
    current = { ...target, actions, spots, bounds, corner };
    current.actions.forEach(([id, label], i) => {
      const b = buttons[i]; b.hidden = false; b.title = label; b.setAttribute('aria-label', label);
      if (b.dataset.action !== id) { b.replaceChildren(icon(id)); b.dataset.action = id; }
      b.dataset.i = String(i);
    });
    buttons.slice(current.actions.length).forEach(b => (b.hidden = true));
    pad.classList.add('on'); place(); if (current) animateFan(true, current);
  };
  const hide = () => {
    cancelOpen(); clearTimeout(hideTimer); cancelAnimationFrame(frame); cancelAnimationFrame(selectionFrame);
    frame = selectionFrame = hideTimer = 0;
    pad?.classList.remove('on');
    if (current) animateFan(false, current);
    current = null;
  };
  const leave = () => { if (current && !hideTimer) hideTimer = setTimeout(() => { hideTimer = 0; hide(); }, 180); };

  /* Ouverture immédiate ; une courte grâce permet de traverser l'espace vers les commandes. */
  const inside = e => { const p = e.composedPath(); return p.includes(host) || (!!current?.el && p.includes(current.el)); };
  addEventListener('pointerover', e => {
    if (!e.isTrusted || e.pointerType === 'touch' || dismissed || !config.enabled || config.trigger === 2) return;
    if (inside(e)) { clearTimeout(hideTimer); hideTimer = 0; return; }
    /* l'éventail d'une sélection reste tant que la sélection existe */
    if (current?.kind === 'selection') return;
    const target = kindOf(e.target);
    if (!target) { cancelOpen(); leave(); return; }
    request(target);
  }, true);
  addEventListener('pointerout', e => { if (!e.isTrusted) return; if (pending?.el && (!(e.relatedTarget instanceof Node) || !pending.el.contains(e.relatedTarget))) cancelOpen(); if (current?.kind !== 'selection' && !e.relatedTarget && config.trigger !== 2) leave(); }, true);
  addEventListener('focusin', e => {
    if (!config.enabled || inside(e) || current?.kind === 'selection' || !e.target.matches?.(':focus-visible')) return;
    const target = kindOf(e.target); if (target) show(target); else leave();
  }, true);

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
    cancelAnimationFrame(selectionFrame); selectionFrame = 0;
    if (target) request(target); else if (current?.kind === 'selection') hide();
  };
  addEventListener('contextmenu', e => {
    if (!e.isTrusted || !config.enabled || config.trigger !== 2 || e.composedPath().includes(host)) return;
    if (e.target.closest?.('input,textarea,[contenteditable]:not([contenteditable="false"])')) return;
    const target = selected() || kindOf(e.target); if (!target) return;
    e.preventDefault(); e.stopImmediatePropagation(); show(target);
  }, true);
  addEventListener('pointerdown', e => { if (!e.composedPath().includes(host) && config.trigger === 2) hide(); }, true);
  addEventListener('mouseup', e => { if (e.isTrusted && e.button === 0 && !e.composedPath().includes(host)) onSelect(); }, true);
  addEventListener('keyup', e => { if (e.isTrusted && e.shiftKey && e.key !== 'Escape') onSelect(); }, true);
  document.addEventListener('selectionchange', () => {
    if (pending?.kind === 'selection' && (getSelection()?.isCollapsed || getSelection()?.toString().trim() !== pending.text)) cancelOpen();
    if (current?.kind !== 'selection') return;
    if (getSelection()?.isCollapsed) hide(); else if (!selectionFrame) selectionFrame = requestAnimationFrame(onSelect);
  });
  addEventListener('scroll', schedulePlace, { passive: true, capture: true });
  addEventListener('resize', schedulePlace, { passive: true });
  addEventListener('keydown', e => { if (e.key === 'Escape') { cancelOpen(); if (current) dismiss(); } }, true);
  addEventListener('pagehide', hide);
  document.addEventListener('visibilitychange', () => { if (document.hidden) hide(); });

  /* ── un clic part vers Sol, avec la clé ── */
  function onAction(e) {
    const b = e.target.closest?.('button');
    if (!b || !current || !e.isTrusted || !config.enabled) return;
    const [action, , value] = current.actions[Number(b.dataset.i)];
    const r = rectOf(current);
    post(JSON.stringify({ sol: 'fan', key: KEY, kind: current.kind, action, value: String(value).slice(0, 50000),
      x: r.right, y: r.top, ratio: devicePixelRatio, title: document.title, language: document.documentElement.lang || '' }));
    dismiss();
  }
})();
