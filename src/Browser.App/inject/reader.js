// Executed on demand, with Mozilla Readability scoped in the same function.
(() => {
  const old = document.getElementById('sol-reader-view');
  if (old?.solArticle) { old.solRestore(); old.remove(); return false; }
  if (!document.body || document.querySelectorAll('*').length > 60000) return 'unsupported';
  const article = new Readability(document.cloneNode(true), { maxElemsToParse: 60000, serializer: el => el }).parse();
  if (!article || article.textContent.trim().length < 500 || article.textContent.length > 500000) return 'unsupported';
  // Shared plain-text representation: no website HTML, scripts or handlers are rendered.
  const blocks = [...article.content.querySelectorAll('h1,h2,h3,h4,p,li,blockquote,pre')]
    .filter(el => !el.parentElement?.closest('li,blockquote,pre'))
    .map(el => ({ kind: el.tagName.toLowerCase(), text: el.textContent.trim() }))
    .filter(b => b.text).slice(0, 3000);
  if (!blocks.length) blocks.push({ kind: 'p', text: article.textContent });
  const host = document.createElement('div'); host.id = 'sol-reader-view';
  const previousFocus = document.activeElement, previousInert = document.body.inert;
  host.solRestore = () => { document.body.inert = previousInert; if (previousFocus?.isConnected) previousFocus.focus({ preventScroll: true }); };
  host.style.cssText = 'position:fixed!important;inset:0!important;z-index:2147483647!important;display:block!important;';
  const root = host.attachShadow({ mode: 'open' });
  const style = document.createElement('style');
  style.textContent = `:host{--bg:#eef0e2;--fg:#171719;--accent:#64784e;color-scheme:light}*{box-sizing:border-box}.view{position:absolute;inset:0;overflow:auto;background:var(--bg);color:var(--fg);font:20px/1.75 system-ui,sans-serif;overscroll-behavior:contain}nav{position:fixed;bottom:18px;left:50%;transform:translateX(-50%);z-index:1;display:flex;justify-content:center;gap:4px;padding:5px;background:color-mix(in srgb,var(--bg) 90%,transparent);backdrop-filter:blur(18px);border:1px solid color-mix(in srgb,var(--fg) 12%,transparent);border-radius:999px;box-shadow:0 5px 24px #0002}button{font:14px system-ui;color:inherit;background:color-mix(in srgb,var(--fg) 6%,transparent);border:1px solid color-mix(in srgb,var(--fg) 12%,transparent);border-radius:999px;padding:8px 14px;cursor:pointer}button:focus-visible,a:focus-visible{outline:2px solid var(--accent);outline-offset:3px}button:disabled{opacity:.4;cursor:default}main{max-width:760px;margin:auto;padding:38px 24px 80px;overflow-wrap:anywhere}h1{font-size:1.8em;line-height:1.2;margin:0 0 20px}h2,h3,h4{line-height:1.4}p{margin:1em 0}.meta{font-size:14px;opacity:.7;margin-bottom:34px}a{color:inherit}blockquote{border-inline-start:3px solid var(--accent);margin-inline:0;padding-inline-start:20px}pre{white-space:pre-wrap;font-size:.85em} @media(max-width:500px){main{padding:24px 20px 60px}h1{font-size:1.5em}}`;
  root.append(style);
  const view = document.createElement('div'); view.className = 'view';
  const nav = document.createElement('nav'); nav.setAttribute('aria-label', readerOptions.en ? 'Reader settings' : 'Réglages de lecture');
  let size = 20;
  const button = (label, action) => { const b = document.createElement('button'); b.textContent = label; b.onclick = action; nav.append(b); return b; };
  const smaller = button('A−', () => resize(-2)), larger = button('A+', () => resize(2));
  smaller.setAttribute('aria-label', readerOptions.en ? 'Smaller text' : 'Réduire le texte');
  larger.setAttribute('aria-label', readerOptions.en ? 'Larger text' : 'Agrandir le texte');
  function resize(delta) { size = Math.max(16, Math.min(32, size + delta)); view.style.fontSize = size + 'px'; smaller.disabled = size === 16; larger.disabled = size === 32; }
  const main = document.createElement('main'); main.lang = article.lang || document.documentElement.lang;
  if (article.dir === 'rtl') main.dir = 'rtl';
  const heading = document.createElement('h1'); heading.textContent = article.title || document.title; main.append(heading);
  const meta = document.createElement('div'); meta.className = 'meta';
  meta.textContent = [article.byline, new URL(location.href).hostname].filter(Boolean).join(' · '); main.append(meta);
  for (const block of blocks) { const el = document.createElement(block.kind === 'li' ? 'p' : block.kind); el.textContent = (block.kind === 'li' ? '• ' : '') + block.text; main.append(el); }
  view.append(nav, main); root.append(view);
  host.solArticle = { title: heading.textContent, author: article.byline, url: location.href, language: main.lang, blocks };
  // WebView2 delegates local voices to the OS; remote voices are never offered.
  const synth = window.speechSynthesis;
  const voice = document.createElement('select'); voice.setAttribute('aria-label', readerOptions.en ? 'Reading voice' : 'Voix de lecture'); nav.append(voice);
  const status = document.createElement('span'); status.setAttribute('role', 'status');
  let voices = [], playing = false, paused = false, generation = 0, current;
  const speak = button(readerOptions.en ? 'Listen' : 'Écouter', () => {
    if (playing) { paused = !paused; paused ? synth.pause() : synth.resume(); update(); return; }
    const selected = voices.find(v => v.voiceURI === voice.value); if (!selected) return;
    stop(); playing = true; update(); const token = generation;
    const pieces = [heading.textContent, ...blocks.map(b => b.text)].flatMap(text => text.match(/[\s\S]{1,899}(?:\s|$)|[\s\S]{1,900}/g) || []);
    let index = 0;
    const next = () => {
      if (token !== generation) return;
      if (index >= pieces.length) { stop(); return; }
      current = new SpeechSynthesisUtterance(pieces[index++]); current.voice = selected; current.lang = selected.lang;
      current.onend = next;
      current.onerror = e => { if (token !== generation) return; stop(); status.textContent = readerOptions.en ? 'Playback unavailable. Try another voice.' : 'Lecture indisponible. Essayez une autre voix.'; };
      synth.speak(current);
    };
    synth.resume(); next();
  });
  const finish = button('■', () => stop()); finish.setAttribute('aria-label', readerOptions.en ? 'Stop reading' : 'Arrêter la lecture');
  nav.append(status);
  function update() { speak.textContent = playing ? paused ? (readerOptions.en ? 'Resume' : 'Reprendre') : 'Pause' : (readerOptions.en ? 'Listen' : 'Écouter'); speak.disabled = !voice.value; finish.disabled = !playing; voice.disabled = playing; host.dataset.speech = playing ? paused ? 'paused' : 'playing' : 'stopped'; }
  function stop() { generation++; playing = paused = false; if (synth) { synth.cancel(); synth.resume(); } current = null; status.textContent = ''; update(); }
  function listVoices() {
    if (playing) return;
    const previous = voice.value, language = (main.lang || (readerOptions.en ? 'en' : 'fr')).split('-')[0].toLowerCase();
    voices = synth?.getVoices().filter(v => v.localService) || [];
    voice.replaceChildren(); const empty = document.createElement('option'); empty.value = ''; empty.textContent = readerOptions.en ? 'Choose a voice' : 'Choisir une voix'; voice.append(empty);
    for (const v of voices) { const option = document.createElement('option'); option.value = v.voiceURI; option.textContent = `${v.name} · ${v.lang}`; voice.append(option); }
    voice.value = voices.find(v => v.voiceURI === previous)?.voiceURI || voices.find(v => v.lang.toLowerCase().split('-')[0] === language)?.voiceURI || '';
    status.textContent = !voices.length ? (readerOptions.en ? 'No local voice installed' : 'Aucune voix locale installée') : !voice.value ? (readerOptions.en ? 'Choose an installed voice' : 'Choisissez une voix installée') : '';
    update();
  }
  voice.onchange = () => { status.textContent = ''; update(); };
  host.solListen = async () => {
    const token = generation;
    if (!voices.length && synth) {
      await new Promise(resolve => {
        const ready = () => { clearTimeout(timer); synth.removeEventListener('voiceschanged', ready); resolve(); };
        const timer = setTimeout(ready, 1500); synth.addEventListener('voiceschanged', ready);
      });
    }
    if (!host.isConnected || token !== generation) return;
    if (speak.disabled) voice.focus(); else speak.click();
  };
  host.solStop = stop;
  synth?.addEventListener('voiceschanged', listVoices);
  window.addEventListener('pagehide', stop);
  const restore = host.solRestore;
  host.solRestore = () => { stop(); synth?.removeEventListener('voiceschanged', listVoices); window.removeEventListener('pagehide', stop); restore(); };
  style.textContent += `select{font:13px system-ui;color:var(--fg);background:var(--bg);border:1px solid color-mix(in srgb,var(--fg) 12%,transparent);border-radius:999px;padding:8px;max-width:160px;min-width:0}select:focus-visible{outline:2px solid var(--accent)}nav{max-width:calc(100% - 24px);flex-wrap:wrap;width:max-content}nav [role=status]:not(:empty){flex-basis:100%;text-align:center;font:12px/1.4 system-ui;padding:3px 8px} @media(max-width:500px){nav{gap:3px}nav button{padding:8px 10px}select{max-width:130px}main{padding-bottom:140px}}`;
  listVoices();
  document.documentElement.append(host);
  document.body.inert = true;
  view.tabIndex = -1; view.focus({ preventScroll: true });
  return true;
})()
