(() => {
  if(window.__solDeviceSync)return;window.__solDeviceSync=true;
  const key='__DEVICE_SYNC_KEY__';let applying=false,frame=0,enabled=false;
  const wv=window.chrome?.webview;
  wv?.addEventListener?.('message',e=>{if(e.data?.event==='device-sync')enabled=e.data.enabled===true});
  const blocked=el=>!el||el.closest('input[type=password],input[type=file],input[autocomplete=current-password],input[autocomplete=new-password],[contenteditable]');
  const path=el=>{if(el.id)return '#'+CSS.escape(el.id);if(el.getAttribute?.('name'))return el.tagName.toLowerCase()+'[name="'+CSS.escape(el.getAttribute('name'))+'"]';let parts=[];for(let n=el;n&&n!==document.body&&parts.length<8;n=n.parentElement){parts.unshift(n.tagName.toLowerCase()+':nth-of-type('+(Array.from(n.parentElement?.children||[]).filter(x=>x.tagName===n.tagName).indexOf(n)+1)+')')}return 'body>'+parts.join('>')};
  const emit=data=>{if(enabled&&!applying&&wv)wv.postMessage({solDeviceSync:key,page:location.href,...data})};
  addEventListener('scroll',()=>{if(applying||frame)return;frame=requestAnimationFrame(()=>{frame=0;emit({kind:'scroll',x:scrollX/Math.max(1,document.documentElement.scrollWidth-innerWidth),y:scrollY/Math.max(1,document.documentElement.scrollHeight-innerHeight)})})},{passive:true});
  for(const kind of ['input','change','click'])document.addEventListener(kind,e=>{
    if(!e.isTrusted||applying||blocked(e.target))return;
    const el=kind==='click'?e.target.closest('a,button,input[type=checkbox],input[type=radio],[role=button]'):e.target;
    if(blocked(el))return;
    emit({kind,selector:path(el),value:String(el.value??'').slice(0,10000),checked:!!el.checked});
  },true);
  window.__solDeviceApply=data=>{
    applying=true;
    try{
      if(data.kind==='scroll'){scrollTo(data.x*Math.max(0,document.documentElement.scrollWidth-innerWidth),data.y*Math.max(0,document.documentElement.scrollHeight-innerHeight));return}
      const el=document.querySelector(data.selector);if(blocked(el))return;
      if(data.kind==='click')el.click();
      else {const proto=el instanceof HTMLTextAreaElement?HTMLTextAreaElement.prototype:el instanceof HTMLSelectElement?HTMLSelectElement.prototype:HTMLInputElement.prototype;
        Object.getOwnPropertyDescriptor(proto,'value')?.set?.call(el,data.value);if(el.type==='checkbox'||el.type==='radio')el.checked=data.checked;el.dispatchEvent(new Event(data.kind,{bubbles:true}));}
    }finally{requestAnimationFrame(()=>{applying=false})}
  };
})();
