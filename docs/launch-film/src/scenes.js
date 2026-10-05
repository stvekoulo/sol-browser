// Sol — film de lancement, 35 s. Chaque image est une fonction du temps : apply(t).
// Structure reprise plan par plan d'un film modèle : une question posée en typographie fine sur fond beige, puis le
// produit (« Introducing »), trois plans coupés sur le temps, le logo, l'adresse et l'appel à l'action.
film({ W: 1920, H: 1080, FPS: 60, BPM: 120, BEATS: 70 });

const CX = 960, CY = 540;
const SIZE = 50;              // texte fin
const BOLD = 66;              // « Introducing », adresse, appel à l'action
const URL_TEXT = "gitlab.com/stevencrdkoulo/sol-browser";
const BEIGE = [225, 221, 213], ROSE = [222, 211, 208];
const K = {};
let SP = 0;                   // largeur d'une espace à la taille SIZE

// ---------- Fabrication

function add(stage, key, html, cls, css = "") {
  const d = document.createElement("div");
  d.className = cls;
  d.style.cssText = css;
  d.innerHTML = html;
  stage.appendChild(d);
  K[key] = d;
  return d;
}

// Un mot posé à la main : sa largeur est mesurée une fois, sa position vient de place().
function word(stage, key, text, size = SIZE, weight = 400) {
  const d = add(stage, key, text, "t", `font-size:${size}px;font-weight:${weight}`);
  d.w = measure(text, size, weight, "letter-spacing:-0.01em");
  d.h = size * 1.2;
  return d;
}

// Place un élément par son centre.
function place(el, cx, cy, { o = 1, s = 1, r = 0, blur = 0, clipL = 0 } = {}) {
  if (o <= 0.001) { show(el, false); return; }
  show(el, true);
  el.style.opacity = o.toFixed(3);
  el.style.transform = `translate(${(cx - el.w / 2).toFixed(2)}px,${(cy - el.h / 2).toFixed(2)}px) rotate(${r.toFixed(3)}deg) scale(${s.toFixed(4)})`;
  el.style.filter = blur > 0.05 ? `blur(${blur.toFixed(2)}px)` : "none";
  el.style.clipPath = clipL > 0.001 ? `inset(0 0 0 ${(clipL * 100).toFixed(2)}%)` : "none";
}

// Positions des mots d'une ligne centrée : renvoie le centre de chaque mot.
function row(els, cx = CX, gap = SP) {
  const total = els.reduce((s, e) => s + e.w, 0) + gap * (els.length - 1);
  let x = cx - total / 2;
  return els.map((e) => { const c = x + e.w / 2; x += e.w + gap; return c; });
}

function line(stage, key, size = SIZE, weight = 400) {
  return add(stage, key, "", "line", `top:${Math.round(CY - size * 0.6)}px;font-size:${size}px;font-weight:${weight}`);
}

function typed(el, text, n, o = 1) {
  const s = text.slice(0, Math.max(0, Math.min(text.length, Math.floor(n))));
  if (el.last !== s) { el.textContent = s; el.last = s; }
  show(el, o > 0.001 && s.length > 0);
  el.style.opacity = o.toFixed(3);
}

const WHEN = ["when", "was", "the", "last", "time"];
const ORBIT = [[250, 0.4], [150, 2.3], [330, 4.1], [200, 5.4], [380, 1.3]]; // rayon, phase de chaque mot dispersé
const CARDS = [["c1", 13, -34, 8, 14.0], ["c2", -8, 12, -10, 14.5], ["c3", 10, 30, 12, 15.0]]; // image, angle, dx, dy, entrée

function build(stage) {
  SP = measure("a b", SIZE, 400, "letter-spacing:-0.01em") - measure("ab", SIZE, 400, "letter-spacing:-0.01em");
  add(stage, "glow", "", "", "");
  K.glow.id = "glow";

  // A — « Sorry to interrupt your video »
  ["Sorry", "to", "interrupt"].forEach((w, i) => word(stage, "a" + i, w));
  word(stage, "a3", "your video");
  // B — « Quick question. »
  word(stage, "b0", "Quick"); word(stage, "b1", "question.");
  // C, D — « when was the last time », tapé puis dispersé
  line(stage, "c");
  WHEN.forEach((w, i) => word(stage, "d" + i, w));
  // E, F — « Someone glanced at your screen » → « Someone » → « Stopped. and asked »
  line(stage, "e");
  word(stage, "f0", "Stopped."); word(stage, "f1", "and asked");
  // G, H, I — la capture, « which browser », « is that? », la pile de cartes
  add(stage, "g", `<img src="${ASSETS.img.light}">`, "card", "width:1080px;height:607px;border-radius:30px");
  K.g.w = 1080; K.g.h = 607;
  add(stage, "gt", "", "line", `top:118px;font-size:${SIZE}px;font-weight:400`);
  word(stage, "h0", "is"); word(stage, "h1", "that?");
  CARDS.forEach(([img]) => {
    const c = add(stage, img, `<img src="${ASSETS.img[img]}">`, "card", "width:444px;height:600px;border-radius:44px");
    c.w = 444; c.h = 600;
  });
  // J, K — « if you had to think … » → « it's been too long »
  word(stage, "j", "if you had to think");
  for (let i = 0; i < 3; i++) { const d = add(stage, "dot" + i, "", "dot"); d.w = 9; d.h = 9; }
  word(stage, "k", "it’s been too long");

  // L, M — le produit : quatre fenêtres de Sol dans un espace en perspective
  const world = add(stage, "world", "", "", "");
  world.id = "world";
  for (const key of ["light", "player", "compare", "dark"]) {
    const w = document.createElement("div");
    w.className = "win";
    w.innerHTML = `<img src="${ASSETS.img[key]}">`;
    world.appendChild(w);
    K["w_" + key] = w;
  }
  word(stage, "intro", "Introducing", BOLD, 700);
  add(stage, "logo", `<img src="${ASSETS.img.logo}" style="width:100%;height:100%">`, "t", "width:190px;height:190px");
  K.logo.w = 190; K.logo.h = 190;
  word(stage, "mark", "Sol", 210, 700);
  // N, O — l'adresse, l'appel à l'action
  word(stage, "url", URL_TEXT, 50, 700);
  word(stage, "cta", "Download now.", BOLD, 700);
}

// Une fenêtre dans l'espace : décalage du centre, échelle, angles.
function win(el, o, x, y, s, ry, rx) {
  if (o <= 0.001) { show(el, false); return; }
  show(el, true);
  el.style.opacity = o.toFixed(3);
  el.style.transform = `translate3d(${x.toFixed(2)}px,${y.toFixed(2)}px,0) rotateX(${rx.toFixed(3)}deg) rotateY(${ry.toFixed(3)}deg) scale(${s.toFixed(4)})`;
}

const between = (t, a, b) => t >= a && t < b;
const fade = (t, t0, d = 0.14) => prog(t, t0, d, E.out);
const hide = (...els) => els.forEach((e) => show(e, false));

function apply(t) {
  // Fond : beige pour la question, rosé pour le produit, puis retour au beige sur la dernière image (la boucle se ferme).
  const warm = prog(t, 20.85, 0.7, E.smooth) - prog(t, 34.55, 0.45, E.smooth);
  const bg = BEIGE.map((v, i) => Math.round(lerp(v, ROSE[i], warm)));
  document.getElementById("stage").style.background = `rgb(${bg.join(",")})`;
  K.glow.style.opacity = warm.toFixed(3);

  // ---------- A (0 – 3,0) : « Sorry to interrupt » puis « your video »
  {
    const els = [K.a0, K.a1, K.a2, K.a3];
    const gap = 74;
    const l1 = K.a0.w + K.a1.w + K.a2.w + 2 * SP;
    const x0 = CX - (l1 + gap + K.a3.w) / 2;
    const centres = [x0 + K.a0.w / 2, x0 + K.a0.w + SP + K.a1.w / 2, x0 + K.a0.w + K.a1.w + 2 * SP + K.a2.w / 2, x0 + l1 + gap + K.a3.w / 2];
    const starts = [0.2, 0.38, 0.55, 0.98];
    const wipe = x0 + prog(t, 1.25, 0.4, E.inOut) * (l1 + 30); // le début de la phrase s'efface de gauche à droite
    els.forEach((e, i) => {
      if (!between(t, starts[i], 3.0)) { show(e, false); return; }
      const p = spring(t, starts[i], 0.34, 0.8);
      const left = centres[i] - e.w / 2;
      const clip = i < 3 ? clamp((wipe - left) / e.w) : 0;
      const drop = i === 2 ? 26 : i === 3 ? 0 : -12;         // « interrupt » arrive par-dessous, comme dans le modèle
      place(e, centres[i], CY + (1 - p) * drop, { o: clip >= 1 ? 0 : fade(t, starts[i]), blur: (1 - fade(t, starts[i], 0.2)) * 3, clipL: clip });
    });
  }

  // ---------- B (3,0 – 4,25) : « Quick question. »
  if (between(t, 3.0, 4.25)) {
    const [c0, c1] = row([K.b0, K.b1]);
    const p = spring(t, 3.18, 0.4, 0.72);
    place(K.b0, c0, CY, { o: fade(t, 3.0, 0.08) });
    place(K.b1, c1 + (1 - p) * 18, CY + (1 - p) * 30, { o: fade(t, 3.18, 0.1) });
  } else hide(K.b0, K.b1);

  // ---------- C (4,25 – 6,25) : « when was the last time », tapé
  const whenText = WHEN.join(" ");
  if (between(t, 4.25, 6.25)) typed(K.c, whenText, 4 + clamp((t - 4.75) / 0.85) * (whenText.length - 4));
  else show(K.c, false);

  // ---------- D (6,25 – 8,75) : les mots se dispersent, tournent, puis se rejoignent
  {
    const els = WHEN.map((_, i) => K["d" + i]);
    const home = row(els);
    const out = prog(t, 6.25, 0.5, E.out), back = prog(t, 8.1, 0.6, E.in);
    els.forEach((e, i) => {
      if (!between(t, 6.25, 8.72)) { show(e, false); return; }
      const [radius, phase] = ORBIT[i];
      const angle = phase + (t - 6.25) * 0.85;
      const r = radius * (1 - back);
      const x = lerp(home[i], CX + Math.cos(angle) * r * 1.5, out), y = lerp(CY, CY + Math.sin(angle) * r, out);
      place(e, x, y, { s: lerp(1, 0.6, out) * (1 - 0.45 * back), o: 1 - back, blur: (1 - out) * out * 10 + back * 4 });
    });
  }

  // ---------- E (8,75 – 10,5) : « Someone glanced at your screen », effacé jusqu'à « Someone »
  const someone = "Someone glanced at your screen";
  if (between(t, 8.75, 10.5)) typed(K.e, someone, someone.length - clamp((t - 9.85) / 0.45) * (someone.length - 7), fade(t, 8.75, 0.1));
  else show(K.e, false);

  // ---------- F (10,5 – 12,0) : « Stopped. » « and asked »
  if (between(t, 10.5, 12.0)) {
    const [c0, c1] = row([K.f0, K.f1]);
    const p = spring(t, 10.95, 0.4, 0.74);
    place(K.f0, c0, CY + (1 - spring(t, 10.5, 0.3, 0.85)) * 10, { o: fade(t, 10.5, 0.08) });
    place(K.f1, c1, CY + (1 - p) * 42, { o: fade(t, 10.95, 0.12), blur: (1 - fade(t, 10.95, 0.22)) * 4 });
  } else hide(K.f0, K.f1);

  // ---------- G (12,0 – 13,5) : la capture monte ; « which browser »
  if (between(t, 12.0, 13.5)) {
    const up = spring(t, 12.0, 0.6, 0.86), gone = prog(t, 13.22, 0.26, E.in);
    place(K.g, CX, lerp(1480, 640, clamp(up, 0, 1.02)), { s: lerp(0.86, 1.06, prog(t, 12.0, 1.4, E.out)) + gone * 0.05, o: 1 - gone });
    const which = "which browser";
    typed(K.gt, which, t < 12.5 ? 0 : t < 12.75 ? 5 : which.length, 1 - gone);
  } else hide(K.g, K.gt);

  // ---------- H, I (13,25 – 16,25) : « is that? », puis les mots s'écartent autour d'une pile de cartes
  if (between(t, 13.25, 16.25)) {
    const [c0, c1] = row([K.h0, K.h1]);
    const apart = spring(t, 14.0, 0.5, 0.82);
    const fall = spring(t, 13.25, 0.42, 0.8);
    place(K.h0, lerp(c0, CX - 500, apart), lerp(148, CY, fall), { o: fade(t, 13.25, 0.1) });
    place(K.h1, lerp(c1, CX + 520, apart), CY, { o: fade(t, 13.48, 0.1), blur: (1 - fade(t, 13.48, 0.2)) * 3 });
    CARDS.forEach(([key, angle, dx, dy, t0]) => {
      const p = spring(t, t0, 0.42, 0.7);
      place(K[key], CX + dx, CY + dy, { o: fade(t, t0, 0.08), s: lerp(0.62, 1, p), r: angle * (0.6 + 0.4 * p) + (t - t0) * 0.8 });
    });
  } else hide(K.h0, K.h1, K.c1, K.c2, K.c3);

  // ---------- J (16,25 – 19,75) : « if you had to think » et trois points qui attendent
  // ---------- K (19,5 – 21,0) : la ligne file à gauche, « it's been too long » arrive par la droite
  {
    const think = "if you had to think";
    const slide = prog(t, 19.5, 0.32, E.inOut);
    if (between(t, 16.25, 19.82)) {
      const n = clamp((t - 16.3) / 0.9) * think.length;
      const shown = think.slice(0, Math.floor(n));
      if (K.j.last !== shown) { K.j.textContent = shown; K.j.last = shown; }
      const jx = CX - 40 - slide * 1150;
      place(K.j, jx, CY, { o: shown ? 1 - slide * 0.6 : 0, blur: slide * (1 - slide) * 26 });
      for (let i = 0; i < 3; i++) {
        const born = 17.45 + i * 0.16;
        const hop = Math.max(0, Math.sin(((t - 17.45) / 0.9 - i * 0.16) * 2 * Math.PI)) * 9; // les points sautent à tour de rôle
        place(K["dot" + i], jx + K.j.w / 2 + 26 + i * 19, CY + 16 - (t >= born ? hop : 0), { o: t >= born ? 1 - slide : 0 });
      }
    } else hide(K.j, K.dot0, K.dot1, K.dot2);
    if (between(t, 19.5, 21.0)) {
      const leave = prog(t, 20.62, 0.36, E.in);
      place(K.k, CX + (1 - slide) * 1150 - leave * 420, CY + leave * 24, { o: 1 - leave, blur: slide * (1 - slide) * 26 + leave * 5 });
    } else show(K.k, false);
  }

  // ---------- L (21,0 – 25,5) : le produit. La caméra part d'un détail de la fenêtre et recule ; « Introducing ».
  {
    const p = prog(t, 21.0, 4.5, E.smooth);
    win(K.w_light, between(t, 21.0, 25.5) ? fade(t, 21.0, 0.5) : 0, lerp(1250, 10, p), lerp(1010, 205, p), lerp(2.4, 0.84, p), lerp(-24, 9, p), lerp(10, 4, p));
    const o = prog(t, 22.4, 0.5, E.out) - prog(t, 24.55, 0.5, E.in);
    place(K.intro, CX, 250, { o: between(t, 22.4, 25.1) ? o : 0 });
  }
  // ---------- M (25,5 – 27,75) : deux plans coupés sur le temps
  {
    const a = (t - 25.5) / 0.75, b = (t - 26.25) / 1.5;
    win(K.w_player, between(t, 25.5, 26.25) ? 1 : 0, -20 + a * 14, 10, 1.02 + a * 0.02, 17 - a * 3, 5);
    win(K.w_compare, between(t, 26.25, 27.75) ? 1 : 0, 30 - b * 22, 10, 0.98 + b * 0.03, -16 + b * 5, 6);
  }
  // ---------- Fin du produit (27,75 – 31,0) : la fenêtre à gauche, le logo et le nom à droite
  if (between(t, 27.75, 31.0)) {
    const d = (t - 27.75) / 3.25;
    win(K.w_dark, 1, -440 + d * 10, 30, 0.68 + d * 0.012, 20 - d * 3, 5);
    const pop = spring(t, 28.42, 0.36, 0.72), o = fade(t, 28.42, 0.1);
    const total = K.logo.w + 34 + K.mark.w, x0 = 1470 - total / 2;
    place(K.logo, x0 + K.logo.w / 2, CY - 4, { o, s: lerp(0.8, 1, pop) });
    place(K.mark, x0 + K.logo.w + 34 + K.mark.w / 2, CY - 14, { o, s: lerp(0.9, 1, pop) });
  } else { win(K.w_dark, 0, 0, 0, 1, 0, 0); hide(K.logo, K.mark); }

  // ---------- N (31,0 – 32,75) : l'adresse. O (32,75 – 35) : l'appel à l'action.
  place(K.url, CX, CY, { o: between(t, 31.0, 32.75) ? prog(t, 31.0, 0.45, E.out) - prog(t, 32.3, 0.4, E.in) : 0 });
  place(K.cta, CX, CY, { o: between(t, 32.75, 35) ? 1 - prog(t, 34.45, 0.4, E.in) : 0 });
}

// Sons des actions : un tic par lettre tapée, un petit son par mot qui arrive, un souffle par mouvement d'ensemble.
function cues() {
  const list = [];
  const ticks = (t0, dur, n, gain = 0.5) => { for (let i = 0; i < n; i++) list.push(["tick", t0 + (dur * i) / n, { gain }]); };
  [0.2, 0.38, 0.55].forEach((t, i) => list.push(["pop", t, { note: i, gain: 0.5 }]));
  list.push(["pop", 0.98, { note: 4, gain: 0.55 }], ["swish", 1.25, { gain: 0.45, pan: -0.4 }]);
  list.push(["pop", 3.0, { note: 2, gain: 0.55 }], ["pop", 3.18, { note: 4, gain: 0.55 }]);
  ticks(4.75, 0.85, 18);
  list.push(["whoosh", 6.25, { gain: 0.6 }], ["whoosh", 8.1, { gain: 0.5, pan: 0.3 }], ["pop", 8.75, { note: 3, gain: 0.6 }]);
  ticks(9.85, 0.45, 12, 0.4);
  list.push(["pop", 10.5, { note: 1, gain: 0.6 }], ["pop", 10.95, { note: 3, gain: 0.55 }]);
  list.push(["whoosh", 12.0, { gain: 0.55 }], ["tick", 12.5, {}], ["tick", 12.75, {}], ["pop", 13.25, { note: 2, gain: 0.5 }], ["pop", 13.48, { note: 4, gain: 0.5 }]);
  list.push(["swish", 14.0, { gain: 0.4 }], ["pop", 14.0, { note: 0, gain: 0.7 }], ["pop", 14.5, { note: 2, gain: 0.7 }], ["pop", 15.0, { note: 4, gain: 0.75 }]);
  ticks(16.3, 0.9, 19);
  for (let i = 0; i < 6; i++) list.push(["blip", 17.45 + i * 0.3, { note: i % 3, gain: 0.3 }]);
  list.push(["whoosh", 19.5, { gain: 0.7, pan: -0.5 }], ["whoosh", 20.9, { gain: 0.55 }]);
  list.push(["thud", 25.5, { gain: 0.6 }], ["thud", 26.25, { gain: 0.6 }], ["thud", 27.75, { gain: 0.65 }], ["chime", 28.42, { gain: 0.8 }]);
  list.push(["pop", 31.0, { note: 2, gain: 0.45 }], ["pop", 32.75, { note: 4, gain: 0.6 }]);
  return list;
}
