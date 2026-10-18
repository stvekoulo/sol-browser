// Outils de Sol AI sur la page ouverte, réévalués à chaque appel pour qu'une page ne puisse pas les remplacer.
// Les références (e1, e2…) restent valables jusqu'au prochain read_page. Aucun champ sensible n'est lu ni rempli.
(() => {
  let refs = window.__solAgentRefs ||= [];
  const clip = (s, n) => { s = (s || '').replace(/\s+/g, ' ').trim(); return s.length > n ? s.slice(0, n) + '…' : s; };
  const visible = el => { const r = el.getBoundingClientRect(), st = getComputedStyle(el); return r.width > 0 && r.height > 0 && st.visibility !== 'hidden' && st.display !== 'none'; };
  const sensitive = el => /password|credit-card|cc-|one-time-code/i.test(el.autocomplete || '') || el.type === 'password' ||
    /password|passwd|otp|cvv|cvc|card.?number/i.test(`${el.name || ''} ${el.id || ''}`);
  const ref = el => { let i = refs.indexOf(el); if (i < 0) { refs.push(el); i = refs.length - 1; } return 'e' + (i + 1); };
  const element = r => refs[Number(String(r).replace(/^e/, '')) - 1];
  const label = el => clip(el.getAttribute('aria-label') || el.innerText || el.value || el.placeholder || el.title || el.alt || el.name || '', 80);
  const mainText = () => {
    const root = document.querySelector('main, article, [role=main]') || document.body;
    const clone = root.cloneNode(true);
    clone.querySelectorAll('script,style,noscript,svg,input,textarea,[contenteditable],[hidden],[aria-hidden=true]').forEach(n => n.remove());
    return (clone.innerText || clone.textContent || '').replace(/\n{3,}/g, '\n\n').trim();
  };
  const snapshot = () => {
    refs = window.__solAgentRefs = [];
    const links = [], seen = new Set();
    for (const a of document.querySelectorAll('a[href]')) {
      if (links.length >= 200 || !visible(a)) continue;
      const href = a.href; if (!/^https?:/.test(href) || seen.has(href) || href.split('#')[0] === location.href.split('#')[0]) continue;
      seen.add(href); links.push({ ref: ref(a), text: label(a), href });
    }
    const controls = [];
    for (const el of document.querySelectorAll('button, input, select, textarea, [role=button], [role=tab], [contenteditable=true], summary')) {
      if (controls.length >= 120 || !visible(el) || el.disabled || sensitive(el)) continue;
      controls.push({ ref: ref(el), kind: el.tagName.toLowerCase() + (el.type ? ':' + el.type : ''), label: label(el) });
    }
    const text = mainText();
    return { url: location.href, title: document.title, language: document.documentElement.lang || '',
      headings: [...document.querySelectorAll('h1,h2,h3')].filter(visible).slice(0, 60).map(h => clip(h.innerText, 120)),
      text: text.slice(0, 20000), truncated: text.length > 20000, links, controls };
  };
  const find = query => {
    const q = query.toLowerCase(), hits = [], walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    for (let node; (node = walker.nextNode()) && hits.length < 15;) {
      const at = node.data.toLowerCase().indexOf(q); const el = node.parentElement;
      if (at < 0 || !el || el.closest('script,style,noscript') || !visible(el)) continue;
      const block = el.closest('p,li,td,pre,section,div') || el;
      hits.push({ ref: ref(block), text: clip(block.innerText, 400) });
    }
    return { query, matches: hits };
  };
  const show = r => {
    const el = element(r); if (!el?.isConnected) return { ok: false, error: 'unknown ref, call read_page again' };
    el.scrollIntoView({ block: 'center', behavior: 'smooth' });
    const box = el.getBoundingClientRect(), mark = document.createElement('div');
    mark.style.cssText = `position:fixed;left:${box.left - 4}px;top:${box.top - 4}px;width:${box.width + 8}px;height:${box.height + 8}px;border:2px solid #4c8dff;border-radius:8px;box-shadow:0 0 0 4px #4c8dff33;pointer-events:none;z-index:2147483647;transition:opacity .6s`;
    setTimeout(() => { const b = el.getBoundingClientRect(); mark.style.left = b.left - 4 + 'px'; mark.style.top = b.top - 4 + 'px'; }, 450);
    document.documentElement.appendChild(mark); setTimeout(() => mark.style.opacity = '0', 2200); setTimeout(() => mark.remove(), 2900);
    return { ok: true, text: clip(el.innerText || label(el), 300) };
  };
  const click = r => { const el = element(r); if (!el?.isConnected || !visible(el)) return { ok: false, error: 'unknown ref, call read_page again' }; show(r); el.click(); return { ok: true }; };
  const fill = (r, text) => {
    const el = element(r);
    if (!el?.isConnected || el.disabled || el.readOnly || sensitive(el)) return { ok: false, error: 'field unavailable' };
    el.focus();
    if ('value' in el) { const set = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), 'value')?.set; set ? set.call(el, text) : el.value = text; }
    else if (el.isContentEditable) el.textContent = text;
    el.dispatchEvent(new Event('input', { bubbles: true })); el.dispatchEvent(new Event('change', { bubbles: true }));
    return { ok: true };
  };
  // Lien du même site : lu avec la session de l'utilisateur, comme s'il l'ouvrait. Les autres sites sont lus par Sol, sans cookies.
  const readLink = async target => {
    const href = element(target)?.href || target;
    let url; try { url = new URL(href, location.href); } catch { return { ok: false, error: 'invalid address' }; }
    if (url.origin !== location.origin) return { ok: false, external: url.href };
    const response = await fetch(url.href, { credentials: 'include' });
    const doc = new DOMParser().parseFromString(await response.text(), 'text/html');
    doc.querySelectorAll('script,style,noscript,svg,nav,footer').forEach(n => n.remove());
    const text = ((doc.querySelector('main, article, [role=main]') || doc.body)?.textContent || '').replace(/\s+\n/g, '\n').replace(/[ \t]+/g, ' ').replace(/\n{3,}/g, '\n\n').trim();
    return { ok: response.ok, url: url.href, title: doc.title, text: text.slice(0, 15000), truncated: text.length > 15000 };
  };
  const links = () => refs.filter(el => el.href).map(el => el.href);
  return { snapshot, find, show, click, fill, readLink, links };
})()
