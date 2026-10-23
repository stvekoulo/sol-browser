// Outils de Sol AI sur la page ouverte, réévalués à chaque appel pour qu'une page ne puisse pas les remplacer.
// Les références restent valables jusqu'au prochain read_page. Aucun champ sensible n'est lu ni rempli.
(() => {
  const state = window.__solAgentRefs?.url === location.href ? window.__solAgentRefs
    : (window.__solAgentRefs = { url: location.href, epoch: crypto.randomUUID().slice(0, 8), refs: [] });
  let refs = state.refs;
  const clip = (s, n) => { s = (s || '').replace(/\s+/g, ' ').trim(); return s.length > n ? s.slice(0, n) + '…' : s; };
  const visible = el => { const r = el.getBoundingClientRect(), st = getComputedStyle(el); return r.width > 0 && r.height > 0 && st.visibility !== 'hidden' && st.display !== 'none'; };
  const sensitive = el => /password|credit-card|cc-|one-time-code/i.test(el.autocomplete || '') || el.type === 'password' ||
    /password|passwd|otp|cvv|cvc|card.?number/i.test(`${el.name || ''} ${el.id || ''}`);
  const ref = el => { let i = refs.indexOf(el); if (i < 0) { refs.push(el); i = refs.length - 1; } return `e${state.epoch}_${i + 1}`; };
  const element = r => typeof r === 'string' && r.startsWith(`e${state.epoch}_`) ? refs[Number(r.split('_')[1]) - 1] : null;
  const labelText = node => { const copy = node.cloneNode(true); copy.querySelectorAll('input,select,textarea,button,[contenteditable]').forEach(n => n.remove()); return copy.textContent; };
  const label = el => clip(el.getAttribute('aria-label') || (el.getAttribute('aria-labelledby') || '').split(/\s+/).map(id => el.getRootNode().getElementById?.(id)?.textContent || '').join(' ').trim()
    || [...(el.labels || [])].map(labelText).join(' ')
    || el.innerText || el.placeholder || el.title || el.alt || el.name || el.id || '', 120);
  const roots = () => {
    const found = [document];
    for (let i = 0; i < found.length && i < 80; i++)
      for (const el of found[i].querySelectorAll('*')) {
        if (el.shadowRoot) found.push(el.shadowRoot);
        if (el.tagName === 'IFRAME') { try { if (el.contentDocument?.body) found.push(el.contentDocument); } catch {} }
      }
    return found.slice(0, 80);
  };
  const mainText = () => {
    const root = document.querySelector('main, article, [role=main]') || document.body;
    const clone = root.cloneNode(true);
    clone.querySelectorAll('script,style,noscript,svg,input,textarea,[contenteditable],[hidden],[aria-hidden=true]').forEach(n => n.remove());
    return (clone.innerText || clone.textContent || '').replace(/\n{3,}/g, '\n\n').trim();
  };
  const snapshot = () => {
    refs = state.refs = []; state.epoch = crypto.randomUUID().slice(0, 8);
    const documents = roots();
    const queryAll = selector => documents.flatMap(root => [...root.querySelectorAll(selector)]);
    const links = [], seen = new Set();
    for (const a of queryAll('a[href]')) {
      if (links.length >= 200 || !visible(a)) continue;
      const href = a.href; if (!/^https?:/.test(href) || seen.has(href) || href.split('#')[0] === location.href.split('#')[0]) continue;
      seen.add(href); links.push({ ref: ref(a), text: label(a), href });
    }
    const controls = [];
    for (const el of queryAll('button, input, select, textarea, [role=button], [role=tab], [role=checkbox], [role=radio], [role=combobox], [role=option], [role=menuitem], [contenteditable=true], summary')) {
      if (controls.length >= 120 || !visible(el) || el.disabled || sensitive(el)) continue;
      controls.push({ ref: ref(el), kind: el.tagName.toLowerCase() + (el.type ? ':' + el.type : ''), label: label(el),
        required: !!el.required, checked: ['checkbox', 'radio'].includes(el.type) ? el.checked : undefined,
        options: el.tagName === 'SELECT' ? [...el.options].slice(0, 80).map(o => ({ value: o.value, label: o.label, selected: o.selected, disabled: o.disabled })) : undefined });
    }
    const text = mainText();
    return { url: location.href, title: document.title, language: document.documentElement.lang || '',
      headings: [...document.querySelectorAll('h1,h2,h3')].filter(visible).slice(0, 60).map(h => clip(h.innerText, 120)),
      text: text.slice(0, 20000), truncated: text.length > 20000, links, controls,
      limitations: ['Password, payment, OTP and file inputs are not available.', 'Cross-origin frames and closed shadow roots cannot be read.'],
      frames: [...document.querySelectorAll('iframe')].map(f => ({ title: f.title, src: f.src })).slice(0, 20) };
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
  const click = r => { const el = element(r); if (!el?.isConnected || !visible(el) || el.disabled || sensitive(el) || el.type === 'file') return { ok: false, error: 'element unavailable, call read_page again' }; show(r); el.click(); return { ok: true }; };
  const writable = el => el?.isConnected && visible(el) && !el.disabled && !el.readOnly && !sensitive(el);
  const error = message => ({ ok: false, error: message });
  const validate = (r, kind, value) => {
    const el = element(r);
    if (!writable(el)) return 'field unavailable, call read_page again';
    if (kind === 'text') {
      if (!(el.tagName === 'TEXTAREA' || el.isContentEditable || el.tagName === 'INPUT' && !['file','checkbox','radio','submit','button','reset','image','hidden'].includes(el.type))) return 'not a text field';
      if (typeof value !== 'string' || value.length > 50000 || el.maxLength >= 0 && value.length > el.maxLength) return 'text exceeds field limit';
    } else if (kind === 'select') {
      if (el.tagName !== 'SELECT' || ![...el.options].some(o => !o.disabled && !o.parentElement?.disabled && (o.value === value || o.label === value))) return 'option unavailable';
    } else if (kind === 'checked') {
      if (el.tagName !== 'INPUT' || !['checkbox','radio'].includes(el.type) || !['true','false'].includes(value) || el.type === 'radio' && value === 'false') return 'not a supported checkbox/radio action';
    } else return 'unknown field kind';
    return null;
  };
  const write = (r, kind, value) => {
    const problem = validate(r, kind, value); if (problem) return error(problem);
    const el = element(r);
    el.focus();
    if (kind === 'checked') {
      if (el.checked !== (value === 'true')) el.click();
      if (el.checked !== (value === 'true')) return error('the site rejected the checkbox/radio change');
    }
    else {
      const text = kind === 'select' ? [...el.options].find(o => !o.disabled && !o.parentElement?.disabled && (o.value === value || o.label === value)).value : value;
      if ('value' in el) { const set = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), 'value')?.set; set ? set.call(el, text) : el.value = text; }
      else if (el.isContentEditable) el.textContent = text;
      const EventType = el.ownerDocument.defaultView.Event;
      el.dispatchEvent(new EventType('input', { bubbles: true, composed: true })); el.dispatchEvent(new EventType('change', { bubbles: true }));
      if ('value' in el && el.value !== text) return error('the site rejected the value; inspect the field format');
    }
    return { ok: true };
  };
  const fill = (r, text) => write(r, 'text', text);
  const select = (r, value) => write(r, 'select', value);
  const check = (r, checked) => typeof checked === 'boolean' ? write(r, 'checked', String(checked)) : error('expected a boolean');
  const fillForm = fields => {
    if (!Array.isArray(fields) || fields.length < 1 || fields.length > 30 || new Set(fields.map(f => f?.ref)).size !== fields.length) return error('invalid or repeated fields');
    // Prévalider le lot : une référence périmée ne doit pas laisser un formulaire à moitié rempli.
    for (const field of fields) { const issue = validate(field?.ref, field?.kind, field?.value); if (issue) return { ...error(issue), ref: field?.ref }; }
    const completed = [];
    for (const field of fields) {
      const result = write(field.ref, field.kind, field.value);
      if (!result.ok) return { ...result, ref: field.ref, completed, partial: completed.length > 0 };
      completed.push(field.ref);
    }
    return { ok: true, completed, submitted: false };
  };
  const focus = r => { const el = element(r); if (!writable(el) || el.type === 'file') return error('element unavailable'); el.scrollIntoView({block:'center'}); el.focus(); return { ok: true }; };
  const hoverPoint = r => {
    const el = element(r); if (!el?.isConnected || !visible(el)) return error('element unavailable');
    el.scrollIntoView({block:'center',behavior:'instant'}); let box = el.getBoundingClientRect(); let x = box.x + box.width / 2, y = box.y + box.height / 2;
    for (let view = el.ownerDocument.defaultView; view && view !== window; view = view.parent) {
      const frame = view.frameElement; if (!frame) return error('frame unavailable');
      box = frame.getBoundingClientRect(); x += box.x + frame.clientLeft; y += box.y + frame.clientTop;
    }
    return { ok: true, x, y };
  };
  const scroll = direction => {
    const operations = { up:[0,-innerHeight*.8], down:[0,innerHeight*.8], left:[-innerWidth*.8,0], right:[innerWidth*.8,0] };
    if (direction === 'top' || direction === 'bottom') window.scrollTo({ top:direction === 'top' ? 0 : document.documentElement.scrollHeight, behavior:'instant' });
    else if (operations[direction]) window.scrollBy({left:operations[direction][0],top:operations[direction][1],behavior:'instant'});
    else return error('invalid direction');
    return { ok:true, x:scrollX, y:scrollY };
  };
  // Lien du même site : lu avec la session de l'utilisateur, comme s'il l'ouvrait. Les autres sites sont lus par Sol, sans cookies.
  const readLink = async target => {
    const href = element(target)?.href || target;
    let url; try { url = new URL(href, location.href); } catch { return { ok: false, error: 'invalid address' }; }
    if (!/^https?:$/.test(url.protocol) || url.username || url.password) return error('invalid web address');
    if (url.origin !== location.origin) return { ok: false, external: url.href };
    const response = await fetch(url.href, { credentials: 'include' });
    const doc = new DOMParser().parseFromString(await response.text(), 'text/html');
    doc.querySelectorAll('script,style,noscript,svg,nav,footer').forEach(n => n.remove());
    const text = ((doc.querySelector('main, article, [role=main]') || doc.body)?.textContent || '').replace(/\s+\n/g, '\n').replace(/[ \t]+/g, ' ').replace(/\n{3,}/g, '\n\n').trim();
    return { ok: response.ok, url: url.href, title: doc.title, text: text.slice(0, 15000), truncated: text.length > 15000 };
  };
  const links = () => refs.filter(el => el.href).map(el => el.href);
  return { snapshot, find, show, click, fill, select, check, fillForm, focus, hoverPoint, scroll, readLink, links };
})()
