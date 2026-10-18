// Inspection et aperçus temporaires : chaque propriété modifiée est restaurée à la fermeture.
(() => {
  const existing = document.getElementById('sol-design-view');
  if (existing?.solClose) { existing.solClose(); return false; }
  if (!document.body) return false;
  const t = (fr, en) => designOptions.en ? en : fr;
  const host = document.createElement('div'); host.id = 'sol-design-view';
  host.style.cssText = 'all:initial!important;display:block!important;position:fixed!important;inset:0!important;pointer-events:none!important;z-index:2147483647!important;';
  const root = host.attachShadow({mode:'open'});
  root.innerHTML = `<style>
    :host{--bg:#eef0e2;--fg:#171719;--accent:#64784e}*{box-sizing:border-box}
    .outline{position:fixed;border:1px solid var(--accent);border-radius:4px;pointer-events:none;display:none}
    .panel{position:fixed;right:14px;bottom:14px;width:350px;max-width:calc(100vw - 28px);max-height:calc(100vh - 28px);display:flex;flex-direction:column;overflow:hidden;pointer-events:auto;color:var(--fg);background:var(--bg);border:1px solid color-mix(in srgb,var(--fg) 12%,transparent);border-radius:22px;padding:14px;box-shadow:0 8px 28px #0002;font:12px/1.5 system-ui,sans-serif;font-variant-numeric:tabular-nums}
    .panel>header,.panel>.actions,.panel>.tabs,.panel>footer{flex:none}.inspect,.editor,.tokens{flex:1;min-height:0;overflow:auto;scrollbar-width:thin}.panel.minimized>*:not(header){display:none!important}.panel.minimized{overflow:visible}.head-actions{display:flex;gap:5px}@media(max-height:450px){.hint{display:none}}@media(max-height:330px){.crumbs{display:none}}
    header{display:flex;align-items:center;justify-content:space-between;gap:8px;font-weight:600;font-size:13px;cursor:grab;touch-action:none}button,input{font:inherit;color:inherit}button{background:color-mix(in srgb,var(--fg) 7%,transparent);border:0;border-radius:999px;padding:6px 10px;cursor:pointer}button:disabled{opacity:.45;cursor:default}button:focus-visible,input:focus-visible{outline:2px solid var(--accent);outline-offset:2px}.close{font-size:18px;padding:0 9px}
    .actions,.tabs,.crumbs{display:flex;flex-wrap:wrap;gap:6px;margin:9px 0}.tabs{background:color-mix(in srgb,var(--fg) 6%,transparent);border-radius:999px;padding:3px}.tabs button{flex:1;background:transparent}.tabs [aria-selected=true],button[aria-pressed=true]{background:color-mix(in srgb,var(--accent) 18%,var(--bg));color:var(--fg)}
    p{margin:8px 0;opacity:.7}.target{font-weight:600;color:var(--accent)}.selector{display:block;overflow-wrap:anywhere;margin-top:3px;opacity:.7;font:11px/1.5 ui-monospace,Consolas,monospace}.crumbs button{max-width:120px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:11px;padding:3px 8px}dl{margin:10px 0;display:grid;grid-template-columns:110px minmax(0,1fr);gap:6px}dt{opacity:.65}dd{margin:0;overflow-wrap:anywhere}.swatch{display:inline-block;width:12px;height:12px;border-radius:4px;margin-right:5px;vertical-align:middle;box-shadow:inset 0 0 0 1px #8886}
    .box{margin:12px 0;padding:8px;border-radius:14px;background:color-mix(in srgb,var(--accent) 10%,transparent);text-align:center}.box>span{display:block;font-size:10px;opacity:.7}.box .box{margin:4px 0}.box .content{padding:8px;background:var(--bg);border-radius:8px}.edit-row{display:grid;grid-template-columns:105px minmax(0,1fr);align-items:center;gap:8px;margin:7px 0}.edit-row input{width:100%;min-width:0;border:1px solid color-mix(in srgb,var(--fg) 14%,transparent);border-radius:10px;background:color-mix(in srgb,var(--fg) 4%,transparent);padding:6px;user-select:text}.edit-row input[aria-invalid=true]{border-color:#d64b52}
    footer{display:flex;flex-wrap:wrap;gap:6px;align-items:center;margin-top:10px}[role=status]{display:block;width:100%;font-size:11px;opacity:.7}[hidden]{display:none!important}@media(prefers-reduced-motion:no-preference){button{transition:background .12s}button:active{transform:scale(.96)}}
  </style><div class="outline"></div><section class="panel" aria-label="${t('Inspecteur de design','Design inspector')}">
    <header><span>${t('Design & CSS','Design & CSS')}</span><div class="head-actions"><button class="minimize" aria-label="${t('Réduire ou développer','Collapse or expand')}" aria-expanded="true">−</button><button class="close" aria-label="${t('Fermer','Close')}">×</button></div></header>
    <div class="actions"><button class="hover" aria-pressed="false">${t('Sélectionner un élément','Select an element')}</button><button class="lock" aria-pressed="false" disabled>${t('Verrouiller','Lock')}</button></div>
    <p class="hint">${t('Alt ou Maj + survol. Cliquer avec Sélectionner fige l’élément. Échap ferme.','Alt or Shift + hover. Click while selecting to lock the element. Escape closes.')}</p>
    <span class="target">${t('Aucun élément sélectionné','No element selected')}</span><code class="selector"></code><nav class="crumbs" aria-label="${t('Parents de l’élément','Element ancestors')}"></nav>
    <div class="tabs" role="tablist"><button role="tab" data-tab="inspect" aria-selected="true">${t('Inspecter','Inspect')}</button><button role="tab" data-tab="edit" aria-selected="false">${t('Modifier','Edit')}</button><button role="tab" data-tab="tokens" aria-selected="false">${t('Variables','Variables')}</button></div>
    <div class="inspect"><div class="box-model"></div><dl></dl></div><div class="editor" hidden></div><div class="tokens" hidden></div>
    <footer><button class="copy" disabled>${t('Copier le CSS','Copy CSS')}</button><button class="copy-selector" disabled>${t('Sélecteur','Selector')}</button><button class="copy-tokens" disabled>Tokens</button><button class="reset" disabled>${t('Réinitialiser','Reset')}</button><span role="status" aria-live="polite"></span></footer>
  </section>`;
  document.documentElement.append(host);
  const panel=root.querySelector('.panel'), outline=root.querySelector('.outline'), list=root.querySelector('dl'), status=root.querySelector('[role=status]'), hover=root.querySelector('.hover'), lock=root.querySelector('.lock');
  const properties=['display','position','width','height','font-family','font-size','font-weight','line-height','letter-spacing','color','background-color','padding','margin','gap','border-radius','box-shadow'];
  const editable=['display','position','width','height','min-width','max-width','font-family','font-size','font-weight','line-height','color','background-color','padding','margin','gap','align-items','justify-content','border-radius','box-shadow','transform'];
  let metadataTimer=0;
  const schedule=()=>{if(metadataTimer)return;metadataTimer=setTimeout(()=>{metadataTimer=0;render(false)},150)};
  const changes=new Map(), observer=new ResizeObserver(()=>{reposition();schedule()});
  const lifecycle=new MutationObserver(records=>{
    if(!selected)return;
    if(selected.isConnected){if(records.some(record=>record.target===selected||(record.target instanceof Element&&record.target.contains(selected))))schedule();return}
    restore(selected);selected=null;observer.disconnect();outline.style.display='none';list.replaceChildren();
    root.querySelector('.target').textContent=t('Élément retiré de la page','Element removed from the page');
    root.querySelector('.selector').textContent='';locked=false;lock.setAttribute('aria-pressed','false');
    for(const key of ['.editor','.tokens','.crumbs','.box-model'])root.querySelector(key).replaceChildren();
    for(const key of ['.copy','.copy-selector','.copy-tokens','.reset','.lock'])root.querySelector(key).disabled=true;
  });
  const canvas=document.createElement('canvas');canvas.width=canvas.height=1;
  const context=canvas.getContext('2d',{willReadFrequently:true}), colors=new Map();
  let selected=null, css='', tokenCss='', selector='', frame=0, point=null, alt=false, freeHover=false, locked=false, tab='inspect', dragging=null;
  const color=value=>{
    if(colors.has(value))return colors.get(value);
    if(!CSS.supports('color',value)||!context)return null;
    context.clearRect(0,0,1,1);context.fillStyle=value;context.fillRect(0,0,1,1);
    const data=context.getImageData(0,0,1,1).data, result=[data[0],data[1],data[2],data[3]/255];
    if(colors.size>128)colors.clear();colors.set(value,result);return result;
  };
  const luminance=values=>values.slice(0,3).map(v=>v/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4).reduce((sum,v,i)=>sum+v*[.2126,.7152,.0722][i],0);
  const parent=el=>el.parentElement??(el.getRootNode() instanceof ShadowRoot?el.getRootNode().host:null);
  const contrast=el=>{
    const fg=color(getComputedStyle(el).color);if(!fg)return null;
    const layers=[];let opaque=false;
    for(let node=el;node;node=parent(node)){
      const style=getComputedStyle(node);
      if(style.opacity!=='1'||style.filter!=='none'||style.mixBlendMode!=='normal'||style.backgroundImage!=='none')return null;
      if(!opaque){const bg=color(style.backgroundColor);if(!bg)return null;layers.push(bg);opaque=bg[3]===1}
    }
    if(layers.at(-1)?.[3]!==1)return null;
    let bg=layers.pop();for(const top of layers.reverse())bg=top.slice(0,3).map((v,i)=>v*top[3]+bg[i]*(1-top[3])).concat(1);
    const visible=fg.slice(0,3).map((v,i)=>v*fg[3]+bg[i]*(1-fg[3])), a=luminance(visible), b=luminance(bg);
    return ((Math.max(a,b)+.05)/(Math.min(a,b)+.05)).toFixed(2)+':1';
  };
  const selectorOf=el=>{
    const scope=el.getRootNode(), parts=[];
    for(let node=el;node;node=node.parentElement){
      if(node.id){const id='#'+CSS.escape(node.id);if(scope.querySelectorAll(id).length===1){parts.unshift(id);break}}
      let part=CSS.escape(node.tagName.toLowerCase());
      const classes=[...node.classList].slice(0,2);if(classes.length)part+=classes.map(name=>'.'+CSS.escape(name)).join('');
      const siblings=node.parentElement?[...node.parentElement.children].filter(other=>other.tagName===node.tagName):[];
      if(siblings.length>1)part+=`:nth-of-type(${siblings.indexOf(node)+1})`;
      parts.unshift(part);if(scope.querySelectorAll(parts.join(' > ')).length===1)break;
    }
    return parts.join(' > ');
  };
  const reposition=()=>{
    if(!selected?.isConnected){outline.style.display='none';return}
    const rect=selected.getBoundingClientRect();outline.style.cssText=`display:block;left:${rect.left}px;top:${rect.top}px;width:${rect.width}px;height:${rect.height}px`;
  };
  const restore=el=>{
    const saved=changes.get(el);if(!saved)return;
    for(const [name,old] of saved.properties){
      if(el.style.getPropertyValue(name)!==old.appliedValue||el.style.getPropertyPriority(name)!==old.appliedPriority)continue;
      if(old.value)el.style.setProperty(name,old.value,old.priority);else el.style.removeProperty(name);
    }
    if(!saved.hadStyle&&!el.style.length)el.removeAttribute('style');changes.delete(el);
  };
  const apply=(el,name,value,input)=>{
    if(!el?.isConnected)return;
    const valid=!value||CSS.supports(name,value);
    input.setAttribute('aria-invalid',String(!valid));if(!valid){status.textContent=t('Valeur CSS invalide.','Invalid CSS value.');return}
    if(!changes.has(el))changes.set(el,{hadStyle:el.hasAttribute('style'),properties:new Map()});
    const saved=changes.get(el).properties;if(!saved.has(name))saved.set(name,{value:el.style.getPropertyValue(name),priority:el.style.getPropertyPriority(name)});
    if(value)el.style.setProperty(name,value,'important');else el.style.removeProperty(name);
    const entry=saved.get(name);entry.appliedValue=el.style.getPropertyValue(name);entry.appliedPriority=el.style.getPropertyPriority(name);
    status.textContent=t('Aperçu temporaire — Réinitialiser ou fermer pour annuler.','Temporary preview — reset or close to undo.');render(false);
  };
  const editorRow=(el,name,value)=>{
    const row=document.createElement('label');row.className='edit-row';const title=document.createElement('span');title.textContent=name;
    const input=document.createElement('input');input.value=value;input.setAttribute('aria-label',name);input.spellcheck=false;input.autocomplete='off';
    input.onchange=()=>apply(el,name,input.value.trim(),input);input.onkeydown=e=>{if(e.key==='Enter'){e.preventDefault();input.dispatchEvent(new Event('change'))}};
    row.append(title,input);return row;
  };
  const render=(editors=true)=>{
    if(!selected?.isConnected)return;
    const style=getComputedStyle(selected), rect=selected.getBoundingClientRect();selector=selectorOf(selected);
    root.querySelector('.target').textContent=selected.tagName.toLowerCase();root.querySelector('.selector').textContent=selector;
    list.replaceChildren();const rows=properties.map(name=>[name,style.getPropertyValue(name).slice(0,2000)]);
    css=rows.map(([name,value])=>`  ${name}: ${value};`).join('\n');
    const tokens=[...style].filter(name=>name.startsWith('--')&&!name.startsWith('--tw-')).sort();
    rows.push([t('Dimensions rendues','Rendered dimensions'),`${Math.round(rect.width)} × ${Math.round(rect.height)} px`],[t('Contraste','Contrast'),contrast(selected)??t('Indéterminé','Unknown')]);
    rows.push(['role',selected.getAttribute('role')||({BUTTON:'button',A:'link',INPUT:'input',IMG:'img'}[selected.tagName]??'—')],['aria-label',selected.getAttribute('aria-label')||'—'],['data-slot',selected.getAttribute('data-slot')||'—'],['data-state',selected.getAttribute('data-state')||'—']);
    for(const [name,value] of rows){const dt=document.createElement('dt'),dd=document.createElement('dd');dt.textContent=name;if(CSS.supports('color',value)){const swatch=document.createElement('span');swatch.className='swatch';swatch.style.backgroundColor=value;dd.append(swatch)}dd.append(document.createTextNode(value));list.append(dt,dd)}
    const model=root.querySelector('.box-model');model.replaceChildren();let box=model;
    for(const name of ['margin','border','padding']){const next=document.createElement('div');next.className='box';const label=document.createElement('span');label.textContent=name+' · '+['top','right','bottom','left'].map(side=>style.getPropertyValue(name==='border'?`border-${side}-width`:`${name}-${side}`)).join(' / ');next.append(label);box.append(next);box=next}
    const size=document.createElement('div');size.className='content';size.textContent=style.width+' × '+style.height;box.append(size);
    const crumbs=root.querySelector('.crumbs');crumbs.replaceChildren();const ancestors=[];for(let el=selected;el&&ancestors.length<5;el=parent(el))ancestors.unshift(el);
    for(const el of ancestors){const button=document.createElement('button');button.textContent=el.tagName.toLowerCase()+(el.id?'#'+el.id:'');button.title=selectorOf(el);button.onclick=()=>{locked=true;select(el)};crumbs.append(button)}
    if(editors){const editor=root.querySelector('.editor');editor.replaceChildren();for(const name of editable)editor.append(editorRow(selected,name,style.getPropertyValue(name)))}
    tokenCss=tokens.map(name=>`  ${name}: ${style.getPropertyValue(name).slice(0,2000)};`).join('\n');
    if(editors){const variables=root.querySelector('.tokens');variables.replaceChildren();if(!tokens.length){const empty=document.createElement('p');empty.textContent=t('Aucune variable CSS sur cet élément.','No CSS variables on this element.');variables.append(empty)}for(const name of tokens)variables.append(editorRow(selected,name,style.getPropertyValue(name).slice(0,2000)))}
    root.querySelector('.reset').disabled=!changes.has(selected);lock.disabled=false;lock.setAttribute('aria-pressed',String(locked));
    root.querySelector('.copy').disabled=false;root.querySelector('.copy-selector').disabled=false;root.querySelector('.copy-tokens').disabled=!tokens.length;reposition();
  };
  const select=el=>{if(!el||el===host||host.contains(el)||!el.style)return;if(el===selected){reposition();return}observer.disconnect();lifecycle.disconnect();selected=el;observer.observe(el);const watch={childList:true,subtree:true,attributes:true,attributeFilter:['style','class','data-state','aria-label']};lifecycle.observe(document.documentElement,watch);if(el.getRootNode() instanceof ShadowRoot)lifecycle.observe(el.getRootNode(),watch);status.textContent='';render()};
  const refresh=()=>{frame=0;if(locked||!(alt||freeHover)||!point)return;select(point.target?.isConnected?point.target:document.elementFromPoint(point.x,point.y))};
  const move=e=>{if(dragging){const scale=Number(getComputedStyle(panel).zoom)||1,r=panel.getBoundingClientRect();panel.style.left=Math.max(0,Math.min(innerWidth-r.width,e.clientX-dragging.x))/scale+'px';panel.style.top=Math.max(0,Math.min(innerHeight-r.height,e.clientY-dragging.y))/scale+'px';panel.style.right=panel.style.bottom='auto';return}const path=e.composedPath();if(path.includes(host))return;alt=e.altKey||e.shiftKey;point={x:e.clientX,y:e.clientY,target:path.find(node=>node instanceof Element)};if((alt||freeHover)&&!frame)frame=requestAnimationFrame(refresh)};
  const stopDrag=()=>{dragging=null};
  root.querySelector('header').onpointerdown=e=>{if(e.button!==0||e.target.closest('button'))return;const r=panel.getBoundingClientRect();dragging={x:e.clientX-r.left,y:e.clientY-r.top};e.currentTarget.setPointerCapture(e.pointerId);e.preventDefault()};
  const click=e=>{if(!freeHover||e.composedPath().includes(host))return;e.preventDefault();e.stopImmediatePropagation();select(e.composedPath().find(node=>node instanceof Element));locked=true;freeHover=false;hover.setAttribute('aria-pressed','false');lock.setAttribute('aria-pressed','true')};
  hover.onclick=()=>{freeHover=!freeHover;locked=false;hover.setAttribute('aria-pressed',String(freeHover));lock.setAttribute('aria-pressed','false');refresh()};
  lock.onclick=()=>{locked=!locked;lock.setAttribute('aria-pressed',String(locked))};
  for(const button of root.querySelectorAll('[data-tab]'))button.onclick=()=>{tab=button.dataset.tab;render();for(const choice of root.querySelectorAll('[data-tab]'))choice.setAttribute('aria-selected',String(choice===button));root.querySelector('.inspect').hidden=tab!=='inspect';root.querySelector('.editor').hidden=tab!=='edit';root.querySelector('.tokens').hidden=tab!=='tokens'};
  root.querySelector('.reset').onclick=()=>{restore(selected);render();status.textContent=t('Styles restaurés.','Styles restored.')};
  root.querySelector('.minimize').onclick=e=>{const collapsed=panel.classList.toggle('minimized');e.currentTarget.setAttribute('aria-expanded',String(!collapsed));e.currentTarget.textContent=collapsed?'+':'−'};
  const copy=async text=>{try{await navigator.clipboard.writeText(text);status.textContent=t('Copié','Copied')}catch{status.textContent=t('Copie indisponible sur cette page','Copy unavailable on this page')}};
  root.querySelector('.copy').onclick=()=>copy(`${selector} {\n${css}\n}`);root.querySelector('.copy-selector').onclick=()=>copy(selector);root.querySelector('.copy-tokens').onclick=()=>copy(`${selector} {\n${tokenCss}\n}`);
  const keydown=e=>{if(e.key==='Escape'){host.solClose();return}if(e.key==='Alt'||e.key==='Shift'){alt=true;if(!frame)frame=requestAnimationFrame(refresh)}};
  const keyup=e=>{if(e.key==='Alt'||e.key==='Shift')alt=e.altKey||e.shiftKey};
  const hideOutline=()=>{outline.style.display='none';alt=false;stopDrag()};
  const resize=()=>{if(panel.style.left){const r=panel.getBoundingClientRect(),scale=Number(getComputedStyle(panel).zoom)||1;panel.style.left=Math.max(0,Math.min(innerWidth-r.width,r.left))/scale+'px';panel.style.top=Math.max(0,Math.min(innerHeight-r.height,r.top))/scale+'px'}reposition()};
  const startUrl=location.href,navigation=()=>{if(location.href!==startUrl)host.solClose()};
  host.solSource=startUrl;
  host.solClose=()=>{for(const el of changes.keys())restore(el);observer.disconnect();lifecycle.disconnect();cancelAnimationFrame(frame);clearTimeout(metadataTimer);window.removeEventListener('pointermove',move);window.removeEventListener('pointerup',stopDrag);window.removeEventListener('pointercancel',stopDrag);window.removeEventListener('click',click,true);window.removeEventListener('keydown',keydown,true);window.removeEventListener('keyup',keyup,true);window.removeEventListener('blur',hideOutline);window.removeEventListener('scroll',reposition,true);window.removeEventListener('resize',resize);window.removeEventListener('pagehide',host.solClose);window.removeEventListener('popstate',navigation);window.removeEventListener('hashchange',navigation);host.remove()};
  host.solTheme=(theme,scale)=>{const dark=theme.mode==='dark'||(theme.mode==='system'?theme.systemDark:theme.mode==='light'?false:theme.dark);host.style.setProperty('--bg',dark?'#121214':'#eef0e2');host.style.setProperty('--fg',dark?'#ececef':'#171719');host.style.setProperty('--accent',theme.accent||'#64784e');panel.style.zoom=scale;host.style.colorScheme=dark?'dark':'light'};
  root.querySelector('.close').onclick=host.solClose;
  window.addEventListener('pointermove',move,{passive:true});window.addEventListener('pointerup',stopDrag);window.addEventListener('pointercancel',stopDrag);window.addEventListener('click',click,true);window.addEventListener('keydown',keydown,true);window.addEventListener('keyup',keyup,true);window.addEventListener('blur',hideOutline);window.addEventListener('scroll',reposition,{passive:true,capture:true});window.addEventListener('resize',resize);window.addEventListener('pagehide',host.solClose);window.addEventListener('popstate',navigation);window.addEventListener('hashchange',navigation);
  return true;
})()
