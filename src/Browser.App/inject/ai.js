(() => {
  if (window.__solAI) return;
  let snapshot;
  const value = el => 'value' in el ? el.value : el.textContent || '';
  const fieldOf = el => {
    const field = el?.closest?.('input,textarea,[contenteditable]');
    if (!field || field === document.body || field.readOnly || field.disabled || field.closest('[data-sol-ai-exclude]') ||
      /password|credit-card|cc-|one-time-code/i.test(field.autocomplete || '') ||
      /password|passwd|otp|cvv|cvc|card.?number/i.test(`${field.name || ''} ${field.id || ''}`)) return null;
    return field instanceof HTMLTextAreaElement || field instanceof HTMLInputElement && ['text','search'].includes(field.type) || field.isContentEditable ? field : null;
  };
  const capture = target => {
    let active=target || document.activeElement;
    while(active?.shadowRoot?.activeElement)active=active.shadowRoot.activeElement;
    const selection=getSelection();
    const field=fieldOf(active) || fieldOf(selection?.anchorNode?.parentElement);
    // Ne pas interpréter une sélection dans un champ sensible comme du texte de page.
    if (!field && (active?.closest?.('input,textarea,[contenteditable]') || selection?.anchorNode?.parentElement?.closest('input,textarea,[contenteditable]'))) { snapshot=null;return null; }
    let original=field?value(field):selection?.toString() || '',start=0,end=original.length,range;
    if(original.length>50000){snapshot=null;return null;}
    if(field && 'selectionStart' in field && field.selectionStart!==field.selectionEnd){start=field.selectionStart;end=field.selectionEnd;}
    else if(field?.isContentEditable && selection?.rangeCount && field.contains(selection.anchorNode) && selection.toString()){
      range=selection.getRangeAt(0).cloneRange();const before=range.cloneRange();before.selectNodeContents(field);before.setEnd(range.startContainer,range.startOffset);start=before.toString().length;end=start+selection.toString().length;
    }
    snapshot={field,original,start,end,range,token:crypto.randomUUID(),url:location.href};
    return {text:original.slice(start,end),editable:!!field,token:snapshot.token,language:field?.lang || document.documentElement.lang || '',title:document.title};
  };
  document.addEventListener('contextmenu',e=>capture(e.composedPath()[0]),true);
  const apply = (token,text) => {
    const s=snapshot;
    if(!s || s.token!==token || s.url!==location.href || !s.field?.isConnected || s.field.readOnly || s.field.disabled || value(s.field)!==s.original || text.length>50000 || !fieldOf(s.field))return false;
    if(s.field.maxLength>=0 && s.original.length-(s.end-s.start)+text.length>s.field.maxLength)return false;
    s.field.focus();
    if('selectionStart' in s.field)s.field.setSelectionRange(s.start,s.end);
    else {const r=s.range || document.createRange();if(!s.range)r.selectNodeContents(s.field);getSelection().removeAllRanges();getSelection().addRange(r);}
    const changed=document.execCommand('insertText',false,text);
    if(changed)snapshot=null;
    return changed;
  };
  window.__solAI={capture:()=>capture(),current:()=>snapshot?{text:snapshot.original.slice(snapshot.start,snapshot.end),editable:!!snapshot.field,token:snapshot.token,language:snapshot.field?.lang || document.documentElement.lang || '',title:document.title}:capture(),apply};
})();
