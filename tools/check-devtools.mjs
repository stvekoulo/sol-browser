// Real WinUI/WebView2 developer tools checks; only disposable profiles and local fixtures.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { once } from 'node:events';
import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { sidebarHost } from './sidebar-check-host.mjs';
import { matchesRequest, toCurl } from '../ui/src/networkFormat.ts';

assert.ok(matchesRequest({url:'https://site.test/api',method:'GET',type:'Fetch',status:404},'GET 404 api'));
assert.equal(matchesRequest({url:'https://site.test/api',method:'GET',type:'Fetch',status:200},'404'),false);
const curl=toCurl({url:'https://site.test/a',method:'POST',requestHeaders:{Authorization:'Bearer secret',Accept:'application/json'},postData:"it's fine"});
assert.ok(!curl.includes('Bearer secret')&&curl.includes("it'\\''s fine"),'curl omits credentials and quotes shell text');
assert.ok(!toCurl({url:'https://site.test',method:'POST',requestHeaders:{},postData:'[masqué]'}).includes('--data-raw'),'masked bodies are not replayed');

const fixture=`<!doctype html><html><head><meta charset="utf-8"><style>
body{margin:0;padding:20px;font:16px system-ui;--background:oklch(1 0 0);--foreground:oklch(0 0 0);--primary:oklch(.55 .2 270);--radius:.5rem;background:var(--background);color:var(--foreground)}
body.dark{--background:oklch(.15 0 0);--foreground:oklch(.95 0 0)}
.bg-background{background:var(--background)}.text-foreground{color:var(--foreground)}.rounded-lg{border-radius:var(--radius)}
#design-target{font:24px/36px Arial;padding:12px;margin:8px;border:1px solid #ddd}#gradient{height:50px;background:linear-gradient(red,blue)}
</style></head><body><main><button id="design-target" data-slot="button" data-state="closed" class="bg-background text-foreground rounded-lg">Design check</button><button id="ordinary">Click</button><div id="gradient"></div></main><script>window.__clicks=0;document.getElementById('ordinary').onclick=()=>window.__clicks++;</script></body></html>`;
const server=createServer((req,res)=>{
  if(req.url==='/dev'){res.writeHead(200,{'Content-Type':'text/html;charset=utf-8'});res.end(fixture);return}
  res.writeHead(req.url.startsWith('/404')?404:200,{'Content-Type':'application/json'});res.end(JSON.stringify({ok:true,secret:'fixture-secret'}));
}).listen(0,'127.0.0.1');await once(server,'listening');
const origin=`http://127.0.0.1:${server.address().port}`;
const host=await sidebarHost(process.argv[2]??'bin/sol-sidebar-preview/Sol.exe');
let ui,page,panel;
const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const state=()=>ui.evaluate('window.__sidebarState');
const network=()=>panel.evaluate('window.__network');
const click=selector=>panel.evaluate(`document.querySelector(${JSON.stringify(selector)}).click()`);
const query=async value=>{
  await panel.evaluate(`(()=>{const el=document.querySelector('.net-search input');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event('input',{bubbles:true}))})()`);
  await delay(100);
};
const design=expression=>page.evaluate(`(()=>{const root=document.getElementById('sol-design-view').shadowRoot;${expression}})()`);
const hover=async (selector,expected=selector)=>{
  await host.wait(()=>page.evaluate('!!document.getElementById("sol-design-view")'),'inspector ready before pointer selection');
  const point=await page.evaluate(`(()=>{const r=document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect();return {x:r.left+5,y:r.top+5}})()`);
  await page.call('Input.dispatchMouseEvent',{type:'mouseMoved',...point,modifiers:8});
  await host.wait(()=>design(`return root.querySelector('.selector').textContent===${JSON.stringify(expected)}`),'pointer selected the expected fixture element');
};
try{
  ui=await host.launch();await command({type:'tab.open',url:origin+'/dev'});
  page=await host.connect(origin+'/dev');await host.wait(()=>page.evaluate('!!document.querySelector("#design-target")').catch(()=>false),'fixture ready');
  const id=(await state()).activeId;
  await command({type:'network.toggle'});panel=await host.connect('#panel');
  await host.wait(()=>panel.evaluate('!!document.querySelector(".net")').catch(()=>false),'network UI ready');
  await panel.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.panel==='network')window.__network=e.data});navigator.clipboard.writeText=async text=>window.__copied=text`);
  await command({type:'network.ready'});await host.wait(async()=>(await network())?.tabId===id,'network follows active tab');
  await page.evaluate(`fetch('/api?token=fixture-secret',{headers:{Authorization:'Bearer fixture-secret'}}).then(r=>r.text());console.error('developer fixture error')`);
  await page.evaluate(`fetch('/404').then(r=>r.text())`);
  await host.wait(async()=>(await network()).items.some(r=>r.status===404&&r.ms!==null),'network rows captured');
  assert.ok(!JSON.stringify(await network()).includes('fixture-secret'),'known secrets redacted from list and console');
  await query('GET 404');assert.equal(await panel.evaluate('document.querySelectorAll(".net-row").length'),1,'URL/method/status search');
  await query('');await panel.evaluate(`document.querySelectorAll('.net-controls .net-filters')[1].querySelectorAll('[role=tab]')[1].click()`);
  await panel.evaluate(`document.querySelector('.net-row').click()`);
  await host.wait(()=>panel.evaluate('!!document.querySelector(".net-detail")'),'detail opens');
  await panel.evaluate(`Array.from(document.querySelectorAll('.net-detail .net-copy')).find(b=>b.textContent.includes('cURL')).click()`);
  await host.wait(()=>panel.evaluate('window.__copied?.startsWith("curl ")'),'curl copied');
  assert.ok(!(await panel.evaluate('window.__copied')).includes('fixture-secret'),'copy excludes recognized credentials');
  const count=(await network()).items.length;
  await click('[data-action="pause"]');await host.wait(async()=>(await network()).paused,'capture paused');
  await page.evaluate(`fetch('/paused').then(r=>r.text());console.error('not captured while paused')`);await delay(400);
  assert.equal((await network()).items.length,count,'paused capture does not add requests');
  assert.ok(!(await network()).console.some(line=>line.text.includes('not captured')),'paused console does not add messages');
  await click('[data-action="pause"]');await host.wait(async()=>!(await network()).paused,'capture resumed');
  await page.evaluate(`fetch('/resumed').then(r=>r.text())`);await host.wait(async()=>(await network()).items.some(r=>r.url.endsWith('/resumed')),'resumed capture records requests');
  await panel.evaluate(`document.querySelector('.net-preserve [role=checkbox]').click()`);await host.wait(async()=>(await network()).preserve===false,'preserve toggle');
  await click('[data-action="reload"]');await host.wait(()=>page.evaluate('!!document.querySelector("#design-target")').catch(()=>false),'reload done');
  await host.wait(async()=>(await network()).items.length>0&&!(await network()).items.some(r=>r.url.includes('/resumed')),'reload clears unpreserved requests');
  await command({type:'network.replay',id,input:JSON.stringify({Url:origin+'/replay',Method:'POST',Headers:{'Content-Type':'application/json'},Body:'{"ok":true}',Status:200,Delay:0,Mode:'response'})});
  await host.wait(async()=>(await network()).result?.status===200&&!(await network()).busy,'manual replay retained');
  for(const theme of [1,2])for(const scale of [0,1,2]){
    await command({type:'settings.set',id:'theme',index:theme});await command({type:'settings.set',id:'uiScale',index:scale});await delay(100);
    assert.ok(await panel.evaluate('document.documentElement.scrollWidth<=innerWidth'),'network fits both themes and all scales');
  }
  await panel.call('Emulation.setDeviceMetricsOverride',{width:480,height:420,deviceScaleFactor:1,mobile:false});
  assert.ok(await panel.evaluate('document.documentElement.scrollWidth<=innerWidth'),'network fits a narrow viewport');
  assert.ok(await panel.evaluate('getComputedStyle(document.querySelector(".net-preserve [role=checkbox]")).borderTopWidth!=="0px"'),'checkbox remains visible');
  await panel.call('Emulation.clearDeviceMetricsOverride');
  const shot=await panel.call('Page.captureScreenshot',{format:'png'});await writeFile(resolve(host.base,'devtools-network.png'),Buffer.from(shot.data,'base64'));
  await command({type:'network.toggle'});
  await command({type:'design.toggle',id});await host.wait(()=>page.evaluate('!!document.getElementById("sol-design-view")'),'design enabled');
  assert.equal(await page.evaluate('getComputedStyle(document.getElementById("sol-design-view")).zIndex'),'2147483647','inspector stays above the Sol decorative blur');
  await hover('#design-target');
  assert.ok(await design(`return root.querySelector('dl').textContent.includes('21.00:1')`),'OKLCH contrast calculated by browser color engine');
  assert.ok(await design(`return root.querySelector('.selector').textContent==='#design-target'`),'stable CSS selector');
  assert.ok(await design(`return root.querySelector('dl').textContent.includes('data-slot')`),'shadcn component metadata');
  await design(`root.querySelector('[data-tab="tokens"]').click()`);
  assert.ok(await design(`return root.querySelector('.tokens').textContent.includes('--foreground')`),'semantic CSS tokens available');
  await design(`const input=root.querySelector('.tokens input[aria-label="--foreground"]');input.value='oklch(.5 0 0)';input.dispatchEvent(new Event('change'))`);
  assert.match(await page.evaluate('document.querySelector("#design-target").style.getPropertyValue("--foreground")'),/oklch/,'token preview applied');
  await design(`root.querySelector('.reset').click()`);
  assert.equal(await page.evaluate('document.querySelector("#design-target").style.getPropertyValue("--foreground")'),'','token reset restores inheritance');
  await page.evaluate(`document.body.classList.add('dark')`);await delay(220);
  assert.ok(await design(`return root.querySelector('dl').textContent.includes('oklch(0.95')`),'computed styles follow dynamic theme classes');
  await page.evaluate(`document.body.classList.remove('dark')`);await delay(220);
  await design(`root.querySelector('[data-tab="edit"]').click()`);
  await design(`const input=root.querySelector('.editor input[aria-label="font-size"]');input.value='32px';input.dispatchEvent(new Event('change'))`);
  assert.equal(await page.evaluate('getComputedStyle(document.querySelector("#design-target")).fontSize'),'32px','live style preview');
  await design(`const input=root.querySelector('.editor input[aria-label="padding"]');input.value='not-valid';input.dispatchEvent(new Event('change'))`);
  assert.equal(await design(`return root.querySelector('.editor input[aria-label="padding"]').getAttribute('aria-invalid')`),'true','invalid CSS rejected');
  await design(`root.querySelector('.lock').click()`);await hover('#gradient','#design-target');
  assert.equal(await design(`return root.querySelector('.target').textContent`),'button','locked selection ignores hover');
  await design(`root.querySelector('.lock').click()`);await hover('#gradient');
  assert.ok(await design(`return /Unknown|Indéterminé/.test(root.querySelector('dl').textContent)`),'gradient contrast stays unknown');
  await hover('#design-target');await page.evaluate(`navigator.clipboard.writeText=async text=>window.__css=text`);
  await design(`root.querySelector('.copy-tokens').click()`);
  assert.ok(await page.evaluate(`window.__css.includes('--background:')&&window.__css.includes('oklch')`),'raw modern tokens copied');
  await design(`root.querySelector('[data-tab="inspect"]').click()`);
  for(const theme of [1,2])for(const scale of [0,1,2]){
    await command({type:'settings.set',id:'theme',index:theme});await command({type:'settings.set',id:'uiScale',index:scale});await delay(100);
    assert.ok(await design(`const r=root.querySelector('.panel').getBoundingClientRect();return r.left>=0&&r.top>=0&&r.right<=innerWidth&&r.bottom<=innerHeight`),'design fits both themes and all scales');
    assert.ok(await design(`const p=root.querySelector('.panel').getBoundingClientRect(),f=root.querySelector('footer').getBoundingClientRect();return f.bottom<=p.bottom&&f.top>=p.top`),'copy/reset actions remain visible');
  }
  await design(`root.querySelector('.minimize').click()`);
  assert.ok(await design(`return root.querySelector('.panel').getBoundingClientRect().height<70`),'inspector can collapse out of the way');
  await design(`root.querySelector('.minimize').click()`);
  const dragPoint=await design(`const r=root.querySelector('header').getBoundingClientRect();return {x:r.left+5,y:r.top+5}`);
  await page.call('Input.dispatchMouseEvent',{type:'mousePressed',...dragPoint,button:'left',buttons:1,clickCount:1});
  await page.call('Input.dispatchMouseEvent',{type:'mouseMoved',x:40,y:40,buttons:1});
  await page.call('Input.dispatchMouseEvent',{type:'mouseReleased',x:40,y:40,button:'left',buttons:0,clickCount:1});
  assert.ok(await design(`const r=root.querySelector('.panel').getBoundingClientRect();return r.left>=0&&r.top>=0`),'drag stays within viewport');
  const designShot=await page.call('Page.captureScreenshot',{format:'png'});await writeFile(resolve(host.base,'devtools-design.png'),Buffer.from(designShot.data,'base64'));
  await command({type:'design.toggle',id});await host.wait(()=>page.evaluate('!document.getElementById("sol-design-view")'),'design closed');
  assert.equal(await page.evaluate('getComputedStyle(document.querySelector("#design-target")).fontSize'),'24px','closing restores all previews');
  const ordinary=await page.evaluate(`(()=>{const r=document.getElementById('ordinary').getBoundingClientRect();return {x:r.left+5,y:r.top+5}})()`);
  for(const type of ['mousePressed','mouseReleased'])await page.call('Input.dispatchMouseEvent',{type,...ordinary,button:'left',clickCount:1});
  assert.equal(await page.evaluate('window.__clicks'),1,'ordinary page clicks unchanged');
  await command({type:'design.toggle',id});await hover('#design-target');
  await design(`root.querySelector('[data-tab="edit"]').click();const input=root.querySelector('.editor input[aria-label="font-size"]');input.value='32px';input.dispatchEvent(new Event('change'))`);
  await page.evaluate(`document.getElementById('design-target').style.fontSize='28px'`);
  await command({type:'design.toggle',id});await host.wait(()=>page.evaluate('!document.getElementById("sol-design-view")'),'second inspector closed before reopening');
  assert.equal(await page.evaluate(`document.getElementById('design-target').style.fontSize`),'28px','cleanup preserves later style updates from React or the site');
  await command({type:'design.toggle',id});await hover('#design-target');
  await page.evaluate(`document.getElementById('design-target').remove()`);
  await host.wait(()=>design(`return root.querySelector('.copy').disabled&&root.querySelector('.target').textContent.includes('retiré')`),'removed React/Next elements do not leave stale controls');
  await command({type:'design.toggle',id});await host.wait(()=>page.evaluate('!document.getElementById("sol-design-view")'),'removed-element inspector closed');
  await command({type:'devtools.open'});await delay(700);
  const developerTargets=(await host.targets()).filter(target=>target.url.startsWith('devtools://'));
  console.log(`Native DevTools frontend targets: ${developerTargets.length}`);
  for(const target of developerTargets){const developer=await host.connect(target.url);await developer.call('Page.close').catch(()=>{})}
  if(process.argv.includes('--live-shadcn')){
    const url='https://ui.shadcn.com/docs/theming';await command({type:'tab.open',url});
    const live=await host.connect(url);await host.wait(()=>live.evaluate('!!document.querySelector("button")').catch(()=>false),'live shadcn page',45000);
    const liveId=(await state()).activeId;await command({type:'design.toggle',id:liveId});
    await host.wait(()=>live.evaluate('!!document.getElementById("sol-design-view")'),'live inspector');
    const point=await live.evaluate(`(()=>{const r=document.querySelector('button').getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2}})()`);
    await live.call('Input.dispatchMouseEvent',{type:'mouseMoved',...point,modifiers:8});await delay(300);
    assert.ok(await live.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('.tokens').textContent.includes('--background')`),'real shadcn theme tokens inspected');
    const image=await live.call('Page.captureScreenshot',{format:'png'});await writeFile(resolve(host.base,'devtools-live-shadcn.png'),Buffer.from(image.data,'base64'));
    await command({type:'design.toggle',id:liveId});console.log('Live shadcn theming page passed');
  }
  console.log(`Developer integration passed. Screenshots: ${host.base}`);
}finally{
  if(ui)await host.close(ui);await host.dispose();await new Promise(resolve=>server.close(resolve));
}
