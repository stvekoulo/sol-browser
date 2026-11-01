(function install(window) {
  if (window.__solSpelling) return window.__solSpelling;
  const {document, HTMLInputElement, HTMLTextAreaElement, NodeFilter} = window;
  const getComputedStyle = el => window.getComputedStyle(el);
  const getSelection = () => window.getSelection();
  const addEventListener = (...args) => window.addEventListener(...args);
  const LIMIT = 20000, GRAMMAR = '#d97706';
  let delegate, dirty = true, idle = 0, errorsShown = [], inlineMenu = false, menu;
  const closeMenu = () => { menu?.remove(); menu = undefined; };
  // Vues de Sol : le moteur n'y ouvre pas de menu, les suggestions s'affichent ici.
  const showMenu = (x, y, choices, original) => {
    closeMenu();
    menu = document.createElement('div');
    menu.setAttribute('role', 'menu');
    menu.style.cssText = `position:fixed;left:${x}px;top:${y}px;pointer-events:auto;display:flex;flex-direction:column;min-width:120px;padding:4px;border-radius:10px;background:Canvas;color:CanvasText;color-scheme:light dark;box-shadow:0 4px 16px #0003;font:13px system-ui`;
    for (const choice of choices) {
      const item = document.createElement('button');
      item.type = 'button'; item.textContent = choice; item.setAttribute('role', 'menuitem');
      item.style.cssText = 'all:unset;cursor:pointer;padding:6px 10px;border-radius:6px';
      item.addEventListener('pointerenter', () => { item.style.background = '#8883'; });
      item.addEventListener('pointerleave', () => { item.style.background = ''; });
      item.addEventListener('mousedown', e => e.preventDefault());
      item.addEventListener('click', () => { closeMenu(); window.__solSpelling.replace(original, choice); });
      menu.append(item);
    }
    shadow.append(menu);
    if (!host.isConnected) document.documentElement.append(host);
    const box = menu.getBoundingClientRect();
    if (box.bottom > window.innerHeight) menu.style.top = `${Math.max(0, y - box.height)}px`;
    if (box.right > window.innerWidth) menu.style.left = `${Math.max(0, x - box.width)}px`;
  };
  const host = document.createElement('div');
  host.style.cssText = 'position:fixed;inset:0;pointer-events:none;z-index:2147483647';
  const shadow = host.attachShadow({mode:'closed'});
  const layer = document.createElement('div'); shadow.append(layer);
  let field, originalAttribute, selected, scanned = {}, painted;
  const plain = el => el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement;
  // Zone éditable : le texte de ses nœuds, avec un saut de ligne entre deux blocs, à un retour à la ligne et autour
  // d'un élément non modifiable (étiquette, mention). Sans lui, la fin d'un paragraphe et le début du suivant
  // formeraient un seul mot (« pas.Beaucoup »), signalé à tort.
  const scan = el => {
    // Rebuild: editor DOM can change while textContent remains identical.
    const list = []; let text = '', last = null, broken = false;
    const block = n => { let e = n.parentElement; while (e && e !== el && /^(inline|contents)$/.test(getComputedStyle(e).display)) e = e.parentElement; return e; };
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT,
      { acceptNode: n => n.nodeType === 1 && n.isContentEditable === false ? (broken = true, NodeFilter.FILTER_REJECT) : NodeFilter.FILTER_ACCEPT });
    for (let n; n = walker.nextNode();) {
      if (n.nodeType === 1) { if (n.tagName === 'BR') broken = true; continue; }
      if (!n.length) continue;
      const owner = block(n);
      if (list.length && (broken || owner !== last)) text += '\n';
      list.push({ node: n, start: text.length }); text += n.data; last = owner; broken = false;
    }
    return scanned = { el, raw: el.textContent, text, list };
  };
  const value = el => plain(el) ? el.value : scan(el).text;
  // Position du curseur dans ce texte, ou -1 (sélection étendue, curseur hors d'un nœud de texte).
  const caret = () => {
    if (plain(field)) return field.selectionStart === field.selectionEnd ? field.selectionStart : -1;
    const selection = getSelection();
    if (!selection || !selection.isCollapsed) return -1;
    const at = scan(field).list.find(s => s.node === selection.focusNode);
    return at ? at.start + selection.focusOffset : -1;
  };
  const hide = () => { painted = undefined; layer.replaceChildren(); host.remove(); };
  const clear = () => {
    delegate?.clear(); delegate = undefined;
    painted = undefined; layer.replaceChildren(); host.remove();
    if (field) { if (originalAttribute === null) field.removeAttribute('spellcheck'); else field.setAttribute('spellcheck', originalAttribute); }
    field = undefined;
  };
  const tooLong = el => (plain(el) ? el.value : el.textContent).length > LIMIT;
  // Identifiants, mots de passe, paiement : ni corrigés, ni jamais envoyés au service en ligne.
  const eligible = el => el && el !== document.body && !el.readOnly && !el.disabled && el.spellcheck !== false && !el.closest('[spellcheck="false"],.monaco-editor,.CodeMirror,.cm-editor') &&
    !(el.autocomplete && /password|username|email|tel|credit-card|cc-|one-time-code/i.test(el.autocomplete)) &&
    !(plain(el) && /pass|pwd|card|cvv|cvc|iban|otp|token|secret|login|user|mail|phone/i.test(`${el.name} ${el.id}`)) &&
    (el instanceof HTMLTextAreaElement || el instanceof HTMLInputElement && ['text','search'].includes(el.type) || el.isContentEditable);
  const range = (start, length) => {
    const r=document.createRange();let begun=false;
    for(const s of scan(field).list){const end=s.start+s.node.length;
      if(!begun && start<end){r.setStart(s.node,Math.max(0,start-s.start));begun=true;}
      if(begun && start+length<=end){r.setEnd(s.node,start+length-s.start);return r;}}
  };
  const read = () => {
    let el=document.activeElement; while(el?.shadowRoot?.activeElement)el=el.shadowRoot.activeElement;
    if (el?.tagName === 'IFRAME' || el?.tagName === 'FRAME') {
      try {
        const child = el.contentWindow;
        // Access is deliberately restricted to same-origin frames by the browser.
        if (child?.document) {
          const next = install(child);
          if (delegate !== next) { clear(); delegate = next; }
          return delegate.read();
        }
      } catch { /* Cross-origin and sandboxed frames retain native spellchecking. */ }
      clear(); return {frame:true}; // Cadre d'un autre site : le navigateur l'interroge directement.
    }
    if (delegate) clear();
    if (el === field && document.hasFocus() && !tooLong(field)) {
      // Rien de saisi et les traits en place : pas de relecture du texte à chaque passage.
      if (!dirty && painted !== undefined && ++idle % 3) return {idle:true};
      dirty = false;
      return {text:value(field)};
    }
    clear();
    if(!document.hasFocus() || !eligible(el) || tooLong(el))return null;
    field=el; originalAttribute=el.getAttribute('spellcheck'); el.spellcheck=false; dirty=false;
    return {text:value(el)};
  };
  const paint = (text, errors, accent, inline = false) => {
    if (delegate) return delegate.paint(text, errors, accent, inline);
    inlineMenu = inline;
    if(!field || !field.isConnected || value(field)!==text){hide();return;}
    // Le mot en cours de frappe (curseur juste après lui) n'est pas encore une faute.
    errorsShown = errors;
    const typing = caret();
    errors = errors.filter(([start,length]) => start+length !== typing);
    const box=field.getBoundingClientRect(), css=getComputedStyle(field);
    // Rien n'a changé depuis le dernier passage (texte, fautes, place du champ) : les traits restent tels quels.
    const state = [text, errors, accent, box.left, box.top, box.width, box.height, field.scrollLeft, field.scrollTop].join('|');
    if(state === painted && host.isConnected)return;
    painted = state;
    layer.replaceChildren();
    host.dataset.solSpelling = String(errors.length);
    if(!host.isConnected)document.documentElement.append(host);
    if(plain(field)){
      const clip=document.createElement('div');clip.style.cssText=`position:fixed;left:${box.left}px;top:${box.top}px;width:${box.width}px;height:${box.height}px;overflow:hidden;border-radius:${css.borderRadius};`;
      const mirror=document.createElement('div');
      for(const key of ['font','letterSpacing','lineHeight','padding','borderWidth','borderStyle','boxSizing','textAlign','textIndent','direction','wordSpacing','tabSize'])mirror.style[key]=css[key];
      mirror.style.borderColor='transparent'; mirror.style.color='transparent';
      mirror.style.width=box.width+'px';mirror.style.minHeight=box.height+'px';
      mirror.style.whiteSpace=field instanceof HTMLInputElement ? 'pre' : 'pre-wrap';mirror.style.overflowWrap='break-word';
      mirror.style.transform=`translate(${-field.scrollLeft}px,${-field.scrollTop}px)`;
      let offset=0;
      for(const [start,length,kind] of errors){mirror.append(document.createTextNode(text.slice(offset,start)));const mark=document.createElement('span');mark.textContent=text.slice(start,start+length);mark.style.textDecoration=`underline wavy ${kind ? GRAMMAR : accent}`;mark.style.textUnderlineOffset='3px';mirror.append(mark);offset=start+length;}
      mirror.append(document.createTextNode(text.slice(offset)+'\u200b'));clip.append(mirror);layer.append(clip);
    }else{
      for(const [start,length,kind] of errors){const r=range(start,length);if(!r)continue;const color=kind ? GRAMMAR : accent;
        for(const rect of r.getClientRects()){if(rect.bottom<box.top || rect.top>box.bottom)continue;const mark=document.createElement('div');
          mark.style.cssText=`position:fixed;left:${Math.max(rect.left,box.left)}px;top:${Math.min(rect.bottom,box.bottom)-2}px;width:${Math.max(0,Math.min(rect.right,box.right)-Math.max(rect.left,box.left))}px;height:3px;background:radial-gradient(circle at 2px 0,transparent 1px,${color} 1px,${color} 2px,transparent 2px) 0 0/4px 3px repeat-x`;layer.append(mark);}
      }
    }
  };
  document.addEventListener('input',e=>{if(field && e.composedPath().includes(field)){dirty=true;hide();}},true);
  // Les traits sont posés à l'écran : dès que le texte bouge (défilement, taille), ils s'effacent jusqu'au prochain passage.
  addEventListener('scroll',hide,{capture:true,passive:true});
  addEventListener('resize',hide,{passive:true});
  document.addEventListener('contextmenu',e=>{
    selected=undefined;
    if(!field || !(e.composedPath().includes(field) || field.contains(e.target)))return;
    const text=value(field);let caret=plain(field)?field.selectionStart:undefined;
    if(caret===undefined){const hit=document.caretRangeFromPoint(e.clientX,e.clientY);const at=hit && scan(field).list.find(s=>s.node===hit.startContainer);if(!at)return;caret=at.start+hit.startOffset;}
    // Une faute de grammaire peut couvrir plusieurs mots : elle passe avant le mot sous le curseur.
    const hit=errorsShown.find(([start,length])=>caret>=start && caret<=start+length);
    const words=[...text.matchAll(/[\p{L}\p{M}]+(?:['’][\p{L}\p{M}]+)*/gu)];
    const word=hit ? {index:hit[0],0:text.slice(hit[0],hit[0]+hit[1])} : words.find(m=>caret>=m.index && caret<=m.index+m[0].length);
    selected=word ? {field,text,start:word.index,length:word[0].length,word:word[0],at:performance.now()} : undefined;
    if(inlineMenu && hit?.[3]?.length && selected){e.preventDefault();showMenu(e.clientX,e.clientY,hit[3],selected.word);}
  },true);
  addEventListener('pointerdown',e=>{if(menu && !e.composedPath().includes(host))closeMenu();},true);
  document.addEventListener('keydown',e=>{if(e.key==='Escape')closeMenu();},true);
  const replace = (original,replacement) => {
    if (delegate) return delegate.replace(original, replacement);
    const s=selected;if(!s || !s.field.isConnected || value(s.field)!==s.text || s.word!==original)return false;
    s.field.focus();
    if(plain(s.field))s.field.setSelectionRange(s.start,s.start+s.length);
    else {if(field!==s.field)return false;const r=range(s.start,s.length);if(!r)return false;getSelection().removeAllRanges();getSelection().addRange(r);}
    return document.execCommand('insertText',false,replacement);
  };
  return window.__solSpelling={read,paint,clear,word:()=>delegate ? delegate.word() : selected && performance.now()-selected.at<10000 ? selected.word : null,replace};
})(window)
