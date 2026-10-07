// On-demand, read-only inspection. No page content or styles are persisted.
(() => {
  const existing = document.getElementById('sol-design-view');
  if (existing?.solClose) { existing.solClose(); return false; }
  if (!document.body) return false;
  const en = designOptions.en, t = (fr, english) => en ? english : fr;
  const host = document.createElement('div'); host.id = 'sol-design-view';
  host.style.cssText = 'all:initial!important;display:block!important;position:fixed!important;inset:0!important;pointer-events:none!important;z-index:2147483647!important;';
  const root = host.attachShadow({mode:'open'});
  root.innerHTML = `<style>
    :host{--bg:#eef0e2;--fg:#171719;--accent:#64784e}*{box-sizing:border-box}
    .outline{position:fixed;border:1px solid var(--accent);border-radius:3px;pointer-events:none;display:none}
    .panel{position:fixed;right:14px;bottom:14px;width:320px;max-width:calc(100vw - 28px);max-height:calc(100vh - 28px);overflow:auto;pointer-events:auto;color:var(--fg);background:color-mix(in srgb,var(--bg) 95%,transparent);backdrop-filter:blur(18px);border:1px solid color-mix(in srgb,var(--fg) 12%,transparent);border-radius:20px;padding:14px;box-shadow:0 8px 28px #0002;font:12px/1.5 system-ui,sans-serif}
    header{display:flex;align-items:center;justify-content:space-between;gap:8px;font-weight:600;font-size:13px}button{font:inherit;color:inherit;background:color-mix(in srgb,var(--fg) 7%,transparent);border:0;border-radius:999px;padding:6px 11px;cursor:pointer}button:disabled{opacity:.45;cursor:default}button:focus-visible{outline:2px solid var(--accent);outline-offset:2px}.close{font-size:16px;padding:1px 9px}
    p{margin:8px 0;opacity:.7}dl{margin:10px 0;display:grid;grid-template-columns:90px minmax(0,1fr);gap:6px}dt{opacity:.65}dd{margin:0;overflow-wrap:anywhere}footer{display:flex;flex-wrap:wrap;align-items:center;gap:8px}[role=status]{font-size:11px;opacity:.7}.target{font-weight:600;color:var(--accent)}@media(prefers-reduced-motion:no-preference){.panel{animation:appear .15s ease-out}@keyframes appear{from{opacity:0;transform:translateY(5px)}}}
  </style><div class="outline"></div><section class="panel" aria-label="${t('Inspecteur de design','Design inspector')}"><header><span>${t('Inspecteur de design','Design inspector')}</span><button class="close" aria-label="${t('Fermer','Close')}">×</button></header><p>${t('Alt ou Maj + survol pour inspecter. Sans clavier, activez Survol libre. Survolez ensuite la pilule pour copier. Échap ferme l’inspecteur.','Alt or Shift + hover to inspect. Without a keyboard, enable Free hover. Move over this pill to copy. Escape closes the inspector.')}</p><span class="target">${t('Aucun élément sélectionné','No element selected')}</span><dl></dl><footer><button class="copy" disabled>${t('Copier le CSS','Copy CSS')}</button><span role="status" aria-live="polite"></span></footer></section>`;
  document.documentElement.append(host);
  const panel = root.querySelector('.panel'), outline = root.querySelector('.outline'), list = root.querySelector('dl'), label = root.querySelector('.target'), copy = root.querySelector('.copy'), status = root.querySelector('[role=status]');
  let selected = null, css = '', frame = 0, point = null, alt = false, freeHover = false;
  const hover = document.createElement('button');hover.className='hover';hover.textContent=t('Survol libre','Free hover');hover.setAttribute('aria-pressed','false');root.querySelector('footer').prepend(hover);
  hover.onclick=()=>{freeHover=!freeHover;hover.setAttribute('aria-pressed',String(freeHover));hover.textContent=freeHover?t('Survol actif','Hover active'):t('Survol libre','Free hover');refresh()};
  const properties = ['font-family','font-size','font-weight','line-height','letter-spacing','color','background-color','padding','margin','border-radius'];
  const rgb = value => {const m=value.match(/^rgba?\((\d+)[, ]+([\d.]+)[, ]+([\d.]+)(?:[, /]+([\d.]+))?\)$/);return m && (!m[4] || Number(m[4])===1) ? m.slice(1,4).map(Number) : null;};
  const luminance = values => values.map(v=>v/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4).reduce((sum,v,i)=>sum+v*[.2126,.7152,.0722][i],0);
  const contrast = el => {
    const fg = rgb(getComputedStyle(el).color); let bg = null;
    for(let p=el;p;p=p.parentElement){
      const s=getComputedStyle(p);
      if(s.opacity!=='1'||s.filter!=='none'||s.mixBlendMode!=='normal'||s.backgroundImage!=='none')return null;
      if(!bg && s.backgroundColor!=='rgba(0, 0, 0, 0)' && s.backgroundColor!=='transparent'){
        bg=rgb(s.backgroundColor);if(!bg)return null;
      }
    }
    if(!fg||!bg)return null;
    const a=luminance(fg),b=luminance(bg);return ((Math.max(a,b)+.05)/(Math.min(a,b)+.05)).toFixed(2)+':1';
  };
  const refresh = () => {
    frame=0;if(!(alt||freeHover)||!point)return;
    const el=point.target?.isConnected ? point.target : document.elementFromPoint(point.x,point.y);
    if(!el||el===host||host.contains(el))return;
    selected=el;const style=getComputedStyle(el),rect=el.getBoundingClientRect();
    outline.style.cssText=`display:block;left:${rect.left}px;top:${rect.top}px;width:${rect.width}px;height:${rect.height}px`;
    label.textContent=el.tagName.toLowerCase();list.replaceChildren();
    const rows=properties.map(name=>[name,style.getPropertyValue(name).slice(0,2000)]);
    css=rows.map(([name,value])=>`  ${name}: ${value};`).join('\n');
    rows.push([t('Dimensions','Dimensions'),`${Math.round(rect.width)} × ${Math.round(rect.height)} px`],[t('Contraste','Contrast'),contrast(el)??t('Indéterminé','Unknown')]);
    for(const [name,value] of rows){const dt=document.createElement('dt'),dd=document.createElement('dd');dt.textContent=name;dd.textContent=value;list.append(dt,dd)}
    copy.disabled=false;status.textContent='';
  };
  const move = e => {const path=e.composedPath();if(path.includes(host))return;alt=e.altKey||e.shiftKey;point={x:e.clientX,y:e.clientY,target:path.find(node=>node instanceof Element)};if((alt||freeHover)&&!frame)frame=requestAnimationFrame(refresh)};
  const keydown = e => {if(e.key==='Escape'){host.solClose();return}if(e.key==='Alt'||e.key==='Shift'){alt=true;if(!frame)frame=requestAnimationFrame(refresh)}};
  const keyup = e => {if(e.key==='Alt'||e.key==='Shift')alt=e.altKey||e.shiftKey};
  const hideOutline = () => {outline.style.display='none';alt=false};
  const reposition = () => {if(selected?.isConnected){const r=selected.getBoundingClientRect();outline.style.cssText=`display:block;left:${r.left}px;top:${r.top}px;width:${r.width}px;height:${r.height}px`}else hideOutline()};
  const startUrl = location.href;
  const navigation = () => {if(location.href!==startUrl)host.solClose()};
  host.solSource=startUrl;
  host.solClose=()=>{cancelAnimationFrame(frame);window.removeEventListener('pointermove',move);window.removeEventListener('keydown',keydown,true);window.removeEventListener('keyup',keyup,true);window.removeEventListener('blur',hideOutline);window.removeEventListener('scroll',reposition,true);window.removeEventListener('resize',reposition);window.removeEventListener('pagehide',host.solClose);window.removeEventListener('popstate',navigation);window.removeEventListener('hashchange',navigation);host.remove()};
  host.solTheme=(theme,scale)=>{const dark=theme.mode==='dark'||(theme.mode==='system'?theme.systemDark:theme.mode==='light'?false:theme.dark);host.style.setProperty('--bg',dark?'#121214':'#eef0e2');host.style.setProperty('--fg',dark?'#ececef':'#171719');host.style.setProperty('--accent',theme.accent||'#64784e');panel.style.zoom=scale;host.style.colorScheme=dark?'dark':'light'};
  root.querySelector('.close').onclick=host.solClose;
  copy.onclick=async()=>{try{await navigator.clipboard.writeText(`${selected.tagName.toLowerCase()} {\n${css}\n}`);status.textContent=t('CSS copié','CSS copied')}catch{status.textContent=t('Copie indisponible sur cette page','Copy unavailable on this page')}};
  window.addEventListener('pointermove',move,{passive:true});window.addEventListener('keydown',keydown,true);window.addEventListener('keyup',keyup,true);window.addEventListener('blur',hideOutline);window.addEventListener('scroll',reposition,{passive:true,capture:true});window.addEventListener('resize',reposition);window.addEventListener('pagehide',host.solClose);window.addEventListener('popstate',navigation);window.addEventListener('hashchange',navigation);
  return true;
})()
