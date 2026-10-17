import assert from 'node:assert/strict';
import {readFile,writeFile,access} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';
const payload=Buffer.alloc(4*1024*1024);for(let i=0;i<payload.length;i++)payload[i]=i%251;
function serve(req,res){
  if(req.url.startsWith('/suggest?')){const q=new URL(req.url,'http://localhost').searchParams.get('q');setTimeout(()=>{res.writeHead(200,{'Content-Type':'application/json'});res.end(JSON.stringify([q,[`${q} du bénin demain`,`${q} du bénin cette semaine`]]));},250);return true;}
  if(!req.url.startsWith('/file/'))return false;
  const start=Number(req.headers.range?.match(/bytes=(\d+)/)?.[1]??0);
  const name=req.url.split('/').at(-1);res.writeHead(start?206:200,{'Content-Type':'text/plain','Content-Disposition':`attachment; filename="${name}"`,...(name.includes('unknown')&&!start?{}:{'Content-Length':payload.length-start}),'Accept-Ranges':'bytes',ETag:'"sol-controls"',...(start?{'Content-Range':`bytes ${start}-${payload.length-1}/${payload.length}`}:{})});
  let offset=start;const timer=setInterval(()=>{if(offset>=payload.length){clearInterval(timer);res.end();return;}res.write(payload.subarray(offset,Math.min(payload.length,offset+32768)));offset+=32768},30);res.on('close',()=>clearInterval(timer));return true;
}
const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{serve,downloads:true});
const settings=JSON.parse(await readFile(resolve(host.profile,'settings.json'),'utf8'));settings.OnlineSuggestions=true;settings.SuggestUrlTemplate=host.origin+'/suggest?q={0}';settings.SearchUrlTemplate=host.origin+'/search?q={0}';await writeFile(resolve(host.profile,'settings.json'),JSON.stringify(settings));
await writeFile(resolve(host.profile,'notes.json'),JSON.stringify([{Id:'legacy',Title:'Une note',Text:'Texte conservé',Url:'',Updated:new Date().toISOString()}]));
let sidebar,top,dock,popup,notes;
const cmd=(ui,c)=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const state=()=>sidebar.evaluate('window.__sidebarState');
const input=async(ui,selector,text)=>{
  const point=await ui.evaluate(`(()=>{const r=document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
  await ui.call('Input.dispatchMouseEvent',{type:'mousePressed',...point,button:'left',buttons:1,clickCount:1});await ui.call('Input.dispatchMouseEvent',{type:'mouseReleased',...point,button:'left',buttons:0,clickCount:1});
  await ui.evaluate(`(()=>{const el=document.querySelector(${JSON.stringify(selector)});el.focus();Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(el,${JSON.stringify(text)});el.dispatchEvent(new Event('input',{bubbles:true}))})()`);
};
const key=(ui,key,code=key,virtual)=>ui.call('Input.dispatchKeyEvent',{type:'keyDown',key,code,windowsVirtualKeyCode:virtual,...(key==='Enter'?{text:'\r',unmodifiedText:'\r'}:{})});
const press=async(ui,keyName,virtual)=>{await key(ui,keyName,keyName,virtual);await ui.call('Input.dispatchKeyEvent',{type:'keyUp',key:keyName,code:keyName,windowsVirtualKeyCode:virtual});};
const latest=()=>dock.evaluate('window.__downloads?.items??[]');
const file=async name=>(await latest()).find(d=>d.name===name);
const action=(name,label)=>dock.evaluate(`(()=>{const card=[...document.querySelectorAll('.download-card')].find(c=>c.querySelector('.row-title').textContent===${JSON.stringify(name)});const button=[...card.querySelectorAll('button')].find(b=>${JSON.stringify(label)}.includes(b.textContent.trim()));if(!button||button.disabled)throw Error('download action unavailable');button.click()})()`);
const download=async name=>{
  const source=(await state()).tabs.find(t=>t.url===host.origin+'/2');assert(source);
  const before=new Set((await state()).tabs.map(t=>t.id));
  await cmd(sidebar,{type:'tab.activate',id:source.id});const page=await host.connect(source.url);
  await page.evaluate(`void window.open(${JSON.stringify(host.origin+'/file/'+name)})`);
  dock??=await host.connect('#dock');await host.wait(()=>dock.evaluate('document.readyState==="complete"').catch(()=>false),'dock loaded');
  await dock.evaluate(`if(!window.__downloadListener){window.__downloadListener=true;chrome.webview.addEventListener('message',e=>{if(e.data.popup==='downloads')window.__downloads=e.data});}chrome.webview.postMessage('{"type":"dock.ready"}')`);
  await host.wait(()=>file(name).then(d=>d?.state==='InProgress'&&d.received>0),'download starts');return (await state()).tabs.find(t=>!before.has(t.id))?.id;
};
try{
  sidebar=await host.launch();top=await host.connect('');await host.wait(()=>top.evaluate('!!document.querySelector(".topbar")'),'topbar');
  assert.equal(await top.evaluate(`document.querySelectorAll('button[aria-label="Notes rapides"]').length`),0);
  assert.equal(await sidebar.evaluate(`!!document.querySelector('.sidebar-quick-actions [data-action="notes.manage"]')`),true);
  const source=(await state()).activeId,sourceUrl=(await state()).tabs.find(t=>t.id===source).url;
  await sidebar.evaluate(`document.querySelector('[data-action="notes.manage"]').dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,button:2}))`);
  popup=await host.connect('#popup');await host.wait(()=>popup.evaluate(`document.body.textContent.includes('Ouvrir à côté')`),'notes context menu');
  await popup.evaluate(`[...document.querySelectorAll('[role="menuitem"]')].find(b=>b.textContent.includes('Ouvrir à côté')).dispatchEvent(new MouseEvent('mousedown',{bubbles:true,button:0}))`);
  await host.wait(()=>state().then(s=>s.split.visible&&s.tabs.find(t=>t.id===s.split.secondaryId)?.url==='https://start.sol/index.html#notes'),'notes beside page');
  assert.equal((await state()).split.primaryId,source);assert.equal((await state()).tabs.find(t=>t.id===source).url,sourceUrl);
  const noteId=(await state()).split.secondaryId;await cmd(sidebar,{type:'notes.split',id:source});assert.equal((await state()).tabs.filter(t=>t.url==='https://start.sol/index.html#notes').length,1);
  await cmd(sidebar,{type:'notes.split',id:'missing'});assert.equal((await state()).split.secondaryId,noteId);
  notes=await host.connect('https://start.sol/index.html#notes');await host.wait(()=>notes.evaluate('!!document.querySelector(".notes-search input")'),'notes search');
  await notes.evaluate(`document.querySelector('.notes-search input').focus()`);assert.equal(await notes.evaluate(`getComputedStyle(document.querySelector('.notes-search input')).outlineStyle`),'none');
  assert.equal(await notes.evaluate(`getComputedStyle(document.querySelector('.notes-search')).outlineStyle`),'solid');
  await notes.call('Page.captureScreenshot',{format:'png'}).then(r=>writeFile(resolve(host.base,'notes-search-fixed.png'),Buffer.from(r.data,'base64')));
  await sidebar.call('Emulation.setDeviceMetricsOverride',{width:180,height:500,deviceScaleFactor:1,mobile:false});
  assert.equal(await sidebar.evaluate(`(()=>{const r=document.querySelector('.sidebar-card').getBoundingClientRect();return [...document.querySelectorAll('.sidebar-quick-btn')].every(b=>{const q=b.getBoundingClientRect();return q.left>=r.left&&q.right<=r.right+1&&q.bottom<=r.bottom+1})})()`),true);
  await sidebar.call('Emulation.clearDeviceMetricsOverride');await cmd(sidebar,{type:'split.close'});
  await sidebar.evaluate(`document.querySelector('[data-action="notes.manage"]').click()`);await host.wait(()=>state().then(s=>s.activeId===noteId),'notes left click');
  await cmd(sidebar,{type:'tab.activate',id:source});
  await host.wait(()=>top.evaluate(`document.querySelector('.address-pill.focused')?.dataset.tabId===${JSON.stringify(source)}`),'source address ready');
  await popup.evaluate(`window.__suggestionOpens=0;chrome.webview.addEventListener('message',e=>{if(e.data.fresh)window.__suggestionOpens++})`);
  const selector='.address-pill.focused input';await input(top,selector,'climat');
  await host.wait(()=>top.evaluate(`document.querySelector(${JSON.stringify(selector)}).getAttribute('aria-expanded')==='true'`),'suggestions available');
  await host.wait(()=>popup.evaluate(`document.body.textContent.includes('climat du bénin demain')`),'online fixture suggestions');
  assert.equal(await popup.evaluate('window.__suggestionOpens'),1,'local and online results share one opening');
  await delay(250);
  await popup.evaluate(`window.__suggestionPanel=document.querySelector('[data-slot="morphing-modal"]');window.__suggestionStable=true;window.__watchSuggestions=true;const sample=()=>{if(!window.__watchSuggestions)return;const panel=window.__suggestionPanel;window.__suggestionStable&&=panel.isConnected&&panel===document.querySelector('[data-slot="morphing-modal"]')&&Number(getComputedStyle(panel).opacity)>.99;requestAnimationFrame(sample)};sample()`);
  for(const text of ['s',' voyages']){
    await top.call('Input.insertText',{text});
    const typed=await top.evaluate(`document.querySelector(${JSON.stringify(selector)}).value`);
    await host.wait(()=>popup.evaluate(`document.querySelector('[role="option"]')?.textContent.includes(${JSON.stringify(typed)})`),'local results update while typing');
    await host.wait(()=>popup.evaluate(`document.body.textContent.includes(${JSON.stringify(typed+' du bénin demain')})`),'online results update while typing');
  }
  assert.equal(await popup.evaluate('window.__suggestionOpens'),1,'typing never reopens suggestions');
  assert.equal(await popup.evaluate('window.__suggestionStable'),true,'the same suggestion panel stays opaque throughout typing');
  await popup.evaluate('window.__watchSuggestions=false');
  await popup.evaluate(`chrome.webview.dispatchEvent(new MessageEvent('message',{data:{popup:'suggestions',items:[{url:'https://${'long-domain-'.repeat(12)}.test/path',title:'${'Texte très long '.repeat(20)}',kind:'history'},{url:'https://search.test',title:'${'Recherche '.repeat(30)}',kind:'search'}]}}))`);
  for(const width of [488,360,280]){
    await popup.call('Emulation.setDeviceMetricsOverride',{width,height:600,deviceScaleFactor:1,mobile:false});
    await host.wait(()=>popup.evaluate(`(()=>{const panel=document.querySelector('.suggestions-popup');return panel&&panel.getBoundingClientRect().right<=innerWidth+1&&[...panel.querySelectorAll('.row,.row-title,.row-pill')].every(el=>{const r=el.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth+1})})()`),'suggestion text fits narrow popup');
    assert.equal(await popup.evaluate(`getComputedStyle(document.querySelector('.row-pill>span')).textOverflow`),'ellipsis','long domains truncate inside their badge');
    assert.equal(await popup.evaluate(`(()=>{const label=document.querySelectorAll('.row-pill>span')[1];return label.clientWidth+1>=label.scrollWidth})()`),true,'the search label remains readable beside long text');
  }
  await popup.call('Page.captureScreenshot',{format:'png'}).then(r=>writeFile(resolve(host.base,'suggestions-narrow.png'),Buffer.from(r.data,'base64')));
  await popup.call('Emulation.clearDeviceMetricsOverride');
  await input(top,selector,'climat');await host.wait(()=>popup.evaluate(`document.body.textContent.includes('climat du bénin demain')`),'original query restored');
  await press(top,'ArrowDown',40);await press(top,'ArrowDown',40);assert.equal(await top.evaluate(`document.querySelector(${JSON.stringify(selector)}).value`),'climat du bénin demain');
  await press(top,'ArrowDown',40);await press(top,'ArrowDown',40);assert.equal(await top.evaluate(`document.querySelector(${JSON.stringify(selector)}).value`),'climat');
  await press(top,'ArrowUp',38);await press(top,'ArrowUp',38);await host.wait(()=>top.evaluate(`document.querySelector(${JSON.stringify(selector)}).selectionStart===document.querySelector(${JSON.stringify(selector)}).value.length`),'caret at end');
  await top.call('Input.insertText',{text:' en octobre'});await press(top,'Enter',13);
  await host.wait(()=>state().then(s=>{const t=s.tabs.find(t=>t.id===source);return !t.loading&&new URL(t.url).searchParams.get('q')==='climat du bénin demain en octobre'}),'complete editable suggestion submitted');
  // Restore the local page before download-only windows are created.
  await cmd(sidebar,{type:'nav.go',id:source,input:host.origin+'/2'});await host.wait(()=>state().then(s=>s.tabs.some(t=>t.id===source&&t.url===host.origin+'/2'&&!t.loading)),'local page restored');
  const downloadTab=await download('pause-resume.txt');await action('pause-resume.txt',['Pause']);await host.wait(()=>file('pause-resume.txt').then(d=>d?.paused&&d.canResume),'paused and resumable');
  await delay(300);const received=(await file('pause-resume.txt')).received;await delay(6500);assert.equal((await file('pause-resume.txt')).received,received);
  assert((await state()).tabs.some(t=>t.id===downloadTab),'paused download-only tab stays open');assert.equal(await dock.evaluate('document.querySelectorAll(".download-card").length>0'),true);
  await action('pause-resume.txt',['Reprendre','Resume']);await host.wait(()=>file('pause-resume.txt').then(d=>d?.state==='Completed'),'resumed download completed');
  assert.deepEqual(await readFile(resolve(host.base,'downloads/pause-resume.txt')),payload);
  await download('cancel-active.txt');await action('cancel-active.txt',['Annuler','Cancel']);await host.wait(()=>file('cancel-active.txt').then(d=>d?.canceled&&!d.canResume),'active cancellation');
  await download('cancel-paused-unknown.txt');assert.equal((await file('cancel-paused-unknown.txt')).total,null);await action('cancel-paused-unknown.txt',['Pause']);await host.wait(()=>file('cancel-paused-unknown.txt').then(d=>d?.paused&&d.canResume),'second pause');
  assert.equal(await dock.evaluate(`getComputedStyle(document.querySelector('.download-bar.unknown.paused .download-bar-fill')).animationPlayState`),'paused');
  await action('cancel-paused-unknown.txt',['Annuler','Cancel']);await host.wait(()=>file('cancel-paused-unknown.txt').then(d=>d?.canceled&&!d.canResume&&!d.paused),'paused cancellation');
  await delay(300);assert.deepEqual(await readFile(resolve(host.base,'downloads/pause-resume.txt')),payload);
  for(const name of ['cancel-active.txt','cancel-paused-unknown.txt'])assert.equal(await access(resolve(host.base,'downloads',name)).then(()=>true,()=>false),false);
  assert.equal(await dock.evaluate(`document.querySelector('.popup-header-title').textContent`),'Téléchargements');
  await dock.call('Page.captureScreenshot',{format:'png'}).then(r=>writeFile(resolve(host.base,'downloads-canceled.png'),Buffer.from(r.data,'base64')));
  console.log(JSON.stringify({passed:'single search focus outline; notes bottom and context split/reuse; narrow footer; suggestions open once and stay opaque while typing and online results arrive; editable toolbar suggestions; native pause/resume and exact bytes; cancel active/paused; completed file preserved',evidence:host.base}));
}catch(error){if(sidebar){console.error('STATE',await state().catch(()=>null));console.error('SIDEBAR INPUT',await sidebar.evaluate(`({focus:document.activeElement?.tagName,input:document.querySelector('.sidebar-url-form input').outerHTML})`).catch(()=>null))}if(top)console.error('ADDRESS',await top.evaluate(`({focus:document.activeElement?.tagName,inputs:[...document.querySelectorAll('.address-pill')].map(p=>({class:p.className,input:p.querySelector('input')?.outerHTML}))})`).catch(()=>null));if(popup)console.error('POPUP',await popup.evaluate('document.body.textContent').catch(()=>null));if(dock)console.error('DOWNLOADS',await latest().catch(()=>null));throw error;}finally{if(sidebar)await host.close(sidebar).catch(()=>{});await host.dispose();}
