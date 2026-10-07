// Windows integration check: node tools/check-navigation.mjs [path/to/Sol.exe]
// Uses a disposable profile and a local server; never touches the user's session.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdir, writeFile, readFile, readdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const exe = resolve(process.argv[2] ?? 'src/Browser.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
const base = resolve(`bin/navigation-check-${Date.now()}`);
const profile = resolve(base, 'profiles/robustness');
const echoRequests = [];
await mkdir(profile, { recursive: true });
await mkdir(resolve(base, 'downloads'), { recursive: true });
await mkdir(resolve(base, 'captures'), { recursive: true });
const server = createServer((req, res) => {
  if(req.url==='/sw.js'){res.writeHead(200,{'Content-Type':'application/javascript'});return res.end(`self.addEventListener('install',()=>self.skipWaiting());self.addEventListener('activate',e=>e.waitUntil(self.clients.claim()));self.addEventListener('fetch',e=>{if(new URL(e.request.url).pathname==='/api-real')e.respondWith(new Response('{"serviceworker":true}',{headers:{'Content-Type':'application/json'}}))});`)}
  if (req.url === '/echo') {
    let body='';req.on('data',chunk=>body+=chunk);req.on('end',()=>{echoRequests.push({headers:req.headers,method:req.method,body});res.writeHead(200,{'Content-Type':'application/json'});res.end(JSON.stringify({received:body,password:'fixture-secret'}))});return;
  }
  if (req.url === '/api-real') { res.writeHead(200,{'Content-Type':'application/json'});return res.end('{"real":true}'); }
  if (req.url === '/redirect-once') {res.writeHead(307,{Location:'/echo'});return res.end();}
  if (req.url === '/document') {
    const objects = ['<< /Type /Catalog /Pages 2 0 R >>', '<< /Type /Pages /Kids [3 0 R] /Count 1 >>', '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 500] /Contents 4 0 R >>', '<< /Length 25 >>\nstream\n0 0 1 rg 30 30 90 90 re f\nendstream'];
    let pdf = '%PDF-1.4\n'; const offsets = [];
    objects.forEach((o,i) => { offsets.push(Buffer.byteLength(pdf)); pdf += `${i+1} 0 obj\n${o}\nendobj\n`; });
    const xref = Buffer.byteLength(pdf);
    pdf += `xref\n0 5\n0000000000 65535 f \n${offsets.map(o=>String(o).padStart(10,'0')+' 00000 n \n').join('')}trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
    res.writeHead(200, {'Content-Type':'application/pdf'}); return res.end(pdf);
  }
  if (req.url === '/large-icon.svg') {
    res.writeHead(200, { 'Content-Type': 'image/svg+xml' });
    return res.end('<svg xmlns="http://www.w3.org/2000/svg" width="192" height="192"><rect width="192" height="192" fill="blue"/></svg>');
  }
  if (req.url === '/favicon.ico') {
    res.writeHead(200, { 'Content-Type': 'image/svg+xml' });
    return res.end('<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32"><rect width="32" height="32" fill="green"/></svg>');
  }
  if (req.url === '/reset') return req.socket.resetAndDestroy();
  if (req.url === '/redirect') { res.writeHead(302, { Location: '/redirect' }); return res.end(); }
  if (req.url === '/download') {
    res.writeHead(200, { 'Content-Type': 'application/octet-stream', 'Content-Disposition': 'attachment; filename="sol-check.txt"' });
    return res.end('Sol navigation regression check');
  }
  res.writeHead(req.url === '/missing' ? 404 : 200, { 'Content-Type': 'text/html' });
  const respond = () => res.end(`<html><head><title>Check ${req.url}</title><link href='/favicon.ico' rel='icon' sizes='16x16'><link sizes='any' href='/large-icon.svg' rel='icon'></head><body style="background:#f5f6fa;color:#172233;font:20px system-ui;margin:0"><div style="background:#2769d4;height:100px"></div><main style="padding:40px"><h1>Server page ${req.url}</h1></main></body></html>`);
  if (req.url === '/slow') setTimeout(respond, 1500); else respond();
});
server.listen(0, '127.0.0.1');
await once(server, 'listening');
const origin = `http://127.0.0.1:${server.address().port}`;
const debugPort = createServer();
debugPort.listen(0, '127.0.0.1');
await once(debugPort, 'listening');
const port = debugPort.address().port;
await new Promise(r => debugPort.close(r));
await writeFile(resolve(profile, 'settings.json'), JSON.stringify({ Onboarded: true, Theme: 'dark', HomePage: `${origin}/a`, BlockAds: false, OnlineSuggestions: false, LiveColor: false }));
let app, ui;
if (process.argv.includes('--history')) {
  const { DatabaseSync } = await import('node:sqlite');
  const db = new DatabaseSync(resolve(profile, 'browser.db'));
  db.exec('CREATE TABLE visits (url TEXT PRIMARY KEY, title TEXT NOT NULL, count INTEGER NOT NULL, last INTEGER NOT NULL); CREATE TABLE bookmarks (url TEXT PRIMARY KEY, title TEXT NOT NULL, added INTEGER NOT NULL)');
  const insert = db.prepare('INSERT INTO visits VALUES (?, ?, 1, ?)');
  for (let i = 0; i < 125; i++) insert.run(`https://import.test/${i}`, i === 0 ? 'Older imported target' : `Imported ${i}`, Date.now() - 30 * 86400000 + i * 60000);
  db.prepare('INSERT INTO bookmarks VALUES (?, ?, ?)').run('https://import.test/favorite', 'Older imported target favorite', Date.now());
  db.close();
}
const connections = [];
const waitFor = async (get, description, timeout = 20000) => {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    try { const value = await get(); if (value) return value; }
    catch (e) { if (!/Cannot find default execution context|Execution context was destroyed/.test(e.message)) throw e; }
    await delay(100);
  }
  throw new Error(`Timed out: ${description}`);
};
async function connect(target) {
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await once(ws, 'open');
  let seq = 0;
  const pending = new Map();
  ws.addEventListener('message', e => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) {
      const { resolve, reject, timer } = pending.get(m.id);
      clearTimeout(timer); pending.delete(m.id);
      if (m.error) reject(new Error(m.error.message)); else resolve(m.result);
    }
  });
  const call = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++seq;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 10000);
    pending.set(id, { resolve, reject, timer });
    ws.send(JSON.stringify({ id, method, params }));
  });
  const evaluate = async expression => {
    const result = await call('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true, userGesture: true });
    if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
  };
  connections.push(ws);
  return { call, evaluate };
}
const targets = () => fetch(`http://127.0.0.1:${port}/json/list`).then(r => r.json()).catch(() => []);
const command = c => ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const state = () => ui.evaluate('window.__checkState');
// Dispatch only to this disposable Sol window; do not send keys to the user's foreground app.
async function hotkey(action, withoutAlt = false) {
  const registry = await readFile(resolve('src/Browser.Core/BrowserShortcuts.cs'),'utf8');
  const actions = [...registry.matchAll(/new\(0x[0-9A-F]+,(\d+),"([^"]+)"/g)].map(m=>({modifiers:Number(m[1]),action:m[2]}));
  const index = actions.findIndex(s=>s.action===action && (!withoutAlt || !(s.modifiers&1)));
  assert.ok(index>=0,`registered shortcut: ${action}`);
  const script = `Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public class SolCheck{[DllImport("user32.dll")]public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);}';$p=Get-Process -Id ${app.pid};if($p.MainWindowHandle -eq 0){exit 2};if(-not [SolCheck]::PostMessage($p.MainWindowHandle,0x312,[IntPtr]${index},[IntPtr]::Zero)){exit 3}`;
  const proc = spawn('powershell.exe',['-NoProfile','-NonInteractive','-Command',script],{windowsHide:true,stdio:'ignore'});
  assert.equal((await once(proc,'exit'))[0],0,'native shortcut dispatch');
}
async function launch(extra = []) {
  app = spawn(exe, ['--profile=robustness', process.argv.includes('--small') ? '--bounds=-9000,-9000,400,400' : '--bounds=-9000,-9000,1100,800', ...extra], { windowsHide: true, env: { ...process.env, NAVIGATEUR_DATA_DIR: base, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port} --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows`, NAVIGATEUR_DEBUG: '1', NAVIGATEUR_TEST_DOWNLOAD_DIR: resolve(base, 'downloads'), NAVIGATEUR_CAPTURE_DIR: resolve(base,'captures'), NAVIGATEUR_SAVER_TEST: '1' } });
  const target = await waitFor(async () => (await targets()).find(t => t.url === 'https://ui.navigateur/index.html'), 'toolbar');
  ui = await connect(target);
  await waitFor(async () => {
    await ui.evaluate(`if (!window.__checkListening) {
      window.__checkListening = true;
      chrome.webview.addEventListener('message', e => { window.__checkLastMessage = e.data; if (e.data.tabs) window.__checkState = e.data; });
    } chrome.webview.postMessage(JSON.stringify({type:'ui.ready'}));`);
    return (await state())?.tabs?.length;
  }, 'browser state', 40000);
}
async function pageAt(path) {
  const target = await waitFor(async () => (await targets()).find(t => t.url === `${origin}${path}`), `page ${path}`);
  const page = await connect(target);
  await waitFor(() => page.evaluate(`document.readyState === "complete" && location.href === ${JSON.stringify(`${origin}${path}`)}`), 'requested document ready');
  return page;
}
async function stop() {
  if (!app || app.exitCode !== null) return;
  const exited = once(app, 'exit');
  await command({ type: 'window.close' });
  await exited;
}
try {
  await launch(process.argv.includes('--private-check') ? ['--private'] : []);
  if(process.argv.includes('--network-lot')) {
    if(process.argv.includes('--private-check'))assert.equal((await state()).privateMode,true);
    const page=await pageAt('/a'),id=(await state()).activeId;
    await command({type:'network.toggle'});
    const panel=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#panel')),'network panel'));
    await waitFor(()=>panel.evaluate(`document.readyState==='complete'&&!!document.querySelector('.net')`),'panel ready');
    await panel.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.panel==='network')window.__tools=e.data})`);
    await command({type:'network.ready'});
    const tools=()=>panel.evaluate('window.__tools');
    await waitFor(async()=>(await tools())?.tabId===id,'network tab');
    await page.evaluate(`document.cookie='session=fixture-cookie';console.warn('password=fixture-secret');fetch('/api-real')`);
    await waitFor(async()=>(await tools()).console.some(line=>line.text.includes('[masqué]')),'console capture with redaction');
    assert.ok(!(await tools()).console.some(line=>line.text.includes('fixture-secret')));
    const draft={Url:origin+'/echo',Method:'POST',Headers:{'Content-Type':'text/plain'},Body:'explicit payload',Status:200,Delay:0,Mode:'response'};
    await command({type:'network.replay',id,input:JSON.stringify(draft)});
    await waitFor(async()=>(await tools()).result?.status===200 && !(await tools()).busy,'replay result');
    assert.equal(echoRequests.at(-1).body,'explicit payload');
    assert.equal(echoRequests.at(-1).headers.cookie,undefined,'replay has no site cookie');
    assert.ok(!(await tools()).result.body.includes('fixture-secret'),'response secret redacted');
    const before=echoRequests.length;
    await command({type:'network.replay',id,input:JSON.stringify({...draft,Url:origin+'/redirect-once'})});
    await waitFor(async()=>(await tools()).result?.status===307 && !(await tools()).busy,'redirect response');
    assert.equal(echoRequests.length,before,'redirect not followed');
    const mock={...draft,Url:origin+'/api-real',Method:'GET',Headers:{'Content-Type':'application/json'},Body:'{"mock":true}',Status:503};
    const activate=async options=>{await command({type:'network.mock',id,input:JSON.stringify(options)});await waitFor(async()=>(await tools()).mock?.Mode===options.Mode&&!(await tools()).mockBusy,'mock ready');};
    const fetchResult=()=>page.evaluate(`fetch('/api-real').then(async r=>({status:r.status,body:await r.text()})).catch(()=>({error:true}))`);
    await activate(mock);
    assert.deepEqual(await fetchResult(),{status:503,body:'{"mock":true}'},'real Fetch interception');
    assert.equal(await page.evaluate(`new Promise(resolve=>{const r=new XMLHttpRequest();r.onload=()=>resolve(r.status);r.open('GET','/api-real');r.send()})`),503,'XHR interception');
    await activate({...mock,Mode:'invalid'});assert.equal((await fetchResult()).body,'{');
    await activate({...mock,Mode:'empty'});assert.equal((await fetchResult()).body,'');
    await activate({...mock,Mode:'timeout'});assert.equal((await fetchResult()).error,true);
    await activate({...mock,Mode:'response',Delay:180});const start=Date.now();await fetchResult();assert.ok(Date.now()-start>=150,'mock delay');
    await command({type:'network.unmock',id});await waitFor(async()=>!(await tools()).mock,'mock disabled');
    assert.equal((await fetchResult()).body,'{"real":true}','normal request after disable');
    await activate({...mock,Mode:'response',Delay:5000});
    await page.evaluate(`window.__delayed=fetch('/api-real').then(r=>r.text()).catch(()=> 'failed');true`);await delay(150);
    await command({type:'network.unmock',id});await waitFor(async()=>!(await tools()).mock,'cancel delayed mock');
    assert.equal(await page.evaluate('window.__delayed'),' {"real":true}'.trim(),'disable resumes paused request');
    const crossUrl=origin.replace('127.0.0.1','localhost')+'/api-real';
    await activate({...mock,Url:crossUrl,Mode:'response'});
    assert.equal(await page.evaluate(`fetch(${JSON.stringify(crossUrl)}).then(()=>true).catch(()=>false)`),false,'CORS remains enforced');
    await command({type:'network.unmock',id});await waitFor(async()=>!(await tools()).mock,'cross mock disabled');
    await page.evaluate(`fetch('/api-real')`);await waitFor(async()=>(await tools()).items.some(r=>r.url.endsWith('/api-real')),'request row');
    const row=(await tools()).items.find(r=>r.url.endsWith('/api-real'));
    await command({type:'network.detail',id:row.id});
    await waitFor(()=>panel.evaluate(`!!document.querySelector('.net-edit')`),'edit request');
    await panel.evaluate(`document.querySelector('.net-edit').click()`);
    await waitFor(()=>panel.evaluate(`!!document.querySelector('.net-composer')`),'composer shown');
    await panel.evaluate(`document.querySelector('.net-compose-url input').focus();document.querySelector('.net-compose-url input').select()`);
    await panel.call('Input.insertText',{text:origin+'/echo'});
    const uiBefore=echoRequests.length;
    await panel.evaluate(`document.querySelector('.net-compose-actions button').click()`);
    await waitFor(async()=>echoRequests.length>uiBefore&&(await tools()).result?.status===200&&!(await tools()).busy,'composer sends request');
    assert.ok(await panel.evaluate(`document.querySelector('.net-composer .net-body').textContent.includes('received')`),'reply visible in composer');
    await command({type:'network.replay',id,input:JSON.stringify({...draft,Url:origin+'/slow'})});
    await waitFor(async()=>(await tools()).busy,'cancelable replay started');
    await panel.evaluate(`Array.from(document.querySelectorAll('.net-compose-actions button')).find(b=>b.textContent==='Annuler').click()`);
    await waitFor(async()=>(await tools()).result?.error==='canceled'&&!(await tools()).busy,'replay canceled');
    for(const theme of [1,2])for(const scale of [0,1,2]){
      await command({type:'settings.set',id:'theme',index:theme});await command({type:'settings.set',id:'uiScale',index:scale});await delay(100);
      assert.ok(await panel.evaluate(`document.documentElement.scrollWidth<=innerWidth`),'network fits all scales');
    }
    await panel.call('Page.captureScreenshot',{format:'png'}).then(r=>writeFile(resolve(base,'network-composer.png'),Buffer.from(r.data,'base64')));
    await activate(mock);await command({type:'network.toggle'});
    assert.equal((await fetchResult()).body,'{"real":true}','closing tools disables mock');
    await command({type:'network.toggle'});await waitFor(async()=>(await tools()).tabId===id,'reopen tools');
    await page.evaluate(`navigator.serviceWorker.register('/sw.js').then(()=>navigator.serviceWorker.ready)`);
    await waitFor(()=>page.evaluate('!!navigator.serviceWorker.controller'),'service worker controls page');
    await activate(mock);const swResult=await fetchResult();
    assert.ok(['{"serviceworker":true}','{"mock":true}'].includes(swResult.body),'service worker response or supported interception');
    console.log('PASS service worker: '+swResult.body);
    await command({type:'network.unmock',id});await waitFor(async()=>!(await tools()).mock,'stop service worker mock');
    await activate(mock);await command({type:'tab.open',url:origin+'/b'});const other=await pageAt('/b');
    await waitFor(async()=>(await tools()).tabId!==id,'tools follow second tab');
    assert.equal(await other.evaluate(`fetch('/api-real').then(r=>r.text())`),'{"serviceworker":true}','mock does not leak into another tab');
    await command({type:'tab.activate',id});await waitFor(async()=>(await tools()).tabId===id,'return to original tab');
    await command({type:'network.unmock',id});
    await stop();console.log('PASS network replay isolation, redirects, Fetch/XHR mocks, delay/timeout/invalid/empty, CORS, console, redaction and UI');process.exit(0);
  }
  if (process.argv.includes('--design')) {
    if(process.argv.includes('--private-check'))assert.equal((await state()).privateMode,true);
    const page=await pageAt('/a'),id=(await state()).activeId;
    await page.evaluate(`document.body.style.cssText='background:rgb(255,255,255);color:rgb(0,0,0);margin:0';document.body.innerHTML='<h1 id="target" style="font:24px/36px Arial;padding:12px;margin:8px;color:rgb(0,0,0)">Design check</h1><button id="ordinary">Click</button><div id="gradient" style="height:80px;background:linear-gradient(red,blue)">Gradient</div>';window.__clicks=0;document.querySelector('#ordinary').onclick=()=>window.__clicks++;window.__css='';navigator.clipboard.writeText=async text=>{window.__css=text}`);
    await hotkey('design.toggle');
    await waitFor(()=>page.evaluate(`!!document.getElementById('sol-design-view')`),'design activated');
    const hover=async selector=>{
      const r=await page.evaluate(`(()=>{const r=document.querySelector('${selector}').getBoundingClientRect();return {x:r.x+5,y:r.y+5}})()`);
      await page.call('Input.dispatchMouseEvent',{type:'mouseMoved',x:r.x,y:r.y,modifiers:1});
      await delay(100);
    };
    await hover('#target');
    assert.ok(await page.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('dl').textContent.includes('21.00:1')`),'known black/white contrast');
    assert.ok(await page.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('dl').textContent.includes('24px')`),'computed font size');
    await page.call('Input.dispatchMouseEvent',{type:'mouseMoved',x:5,y:5,modifiers:0});
    const gradientPoint=await page.evaluate(`(()=>{const r=document.querySelector('#gradient').getBoundingClientRect();return {x:r.x+5,y:r.y+5}})()`);
    await page.call('Input.dispatchMouseEvent',{type:'mouseMoved',...gradientPoint,modifiers:8});await delay(100);
    assert.equal(await page.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('.target').textContent`),'div','Shift hover works without Alt');
    await page.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('.hover').click()`);
    const titlePoint=await page.evaluate(`(()=>{const r=document.querySelector('#target').getBoundingClientRect();return {x:r.x+5,y:r.y+5}})()`);
    await page.call('Input.dispatchMouseEvent',{type:'mouseMoved',...titlePoint,modifiers:0});await delay(100);
    assert.equal(await page.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('.target').textContent`),'h1','mouse-only inspection works');
    await page.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('.hover').click()`);
    await page.call('Input.dispatchMouseEvent',{type:'mouseMoved',...gradientPoint,modifiers:0});await delay(100);
    assert.equal(await page.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('.target').textContent`),'h1','disabling free hover freezes selection');
    await page.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('.copy').click()`);
    assert.ok(await page.evaluate(`window.__css.includes('font-size: 24px;') && !window.__css.includes('Design check')`),'CSS only copied on request');
    const button=await page.evaluate(`(()=>{const r=document.querySelector('#ordinary').getBoundingClientRect();return {x:r.x+5,y:r.y+5}})()`);
    for(const type of ['mousePressed','mouseReleased'])await page.call('Input.dispatchMouseEvent',{type,x:button.x,y:button.y,button:'left',clickCount:1});
    assert.equal(await page.evaluate('window.__clicks'),1,'normal page clicks remain functional');
    await hover('#gradient');
    assert.ok(await page.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('dl').textContent.match(/Unknown|Indéterminé/)`),'gradient contrast is unknown');
    const shadowPoint=await page.evaluate(`(()=>{const h=document.createElement('sol-check');h.id='shadow-check';document.body.append(h);const s=h.attachShadow({mode:'open'});s.innerHTML='<h2 style="font-size:28px;margin:0">Shadow</h2>';const r=s.querySelector('h2').getBoundingClientRect();return {x:r.x+5,y:r.y+5}})()`);
    await page.call('Input.dispatchMouseEvent',{type:'mouseMoved',...shadowPoint,modifiers:1});await delay(100);
    assert.ok(await page.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('dl').textContent.includes('28px')`),'open shadow DOM styles');
    for(const theme of [1,2]){
      await command({type:'settings.set',id:'theme',index:theme});
      for(const scale of [0,1,2]){
        await command({type:'settings.set',id:'uiScale',index:scale});await delay(150);
        assert.ok(await page.evaluate(`(()=>{const r=document.getElementById('sol-design-view').shadowRoot.querySelector('.panel').getBoundingClientRect();return r.left>=0&&r.top>=0&&r.right<=innerWidth&&r.bottom<=innerHeight})()`),'inspector fits at all scales');
      }
    }
    const panelWidth=await page.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('.panel').getBoundingClientRect().width`);
    await command({type:'zoom.in',id});await delay(200);
    const zoomWidth=await page.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('.panel').getBoundingClientRect().width`);
    assert.ok(Math.abs(zoomWidth*1.1-panelWidth)<2,'inspector keeps its physical size when page zoom changes');
    await command({type:'zoom.reset',id});await delay(150);
    await command({type:'split.open',id});await delay(300);
    assert.ok(await page.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('.panel').getBoundingClientRect().left>=0`),'actual split pane');
    await command({type:'split.close'});await delay(150);
    await page.call('Emulation.setDeviceMetricsOverride',{width:360,height:400,deviceScaleFactor:1,mobile:false});
    assert.ok(await page.evaluate(`document.getElementById('sol-design-view').shadowRoot.querySelector('.panel').getBoundingClientRect().left>=0`),'narrow split pane');
    await page.call('Emulation.clearDeviceMetricsOverride');
    await page.call('Page.captureScreenshot',{format:'png'}).then(r=>writeFile(resolve(base,'design.png'),Buffer.from(r.data,'base64')));
    await page.call('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',windowsVirtualKeyCode:27});
    await waitFor(()=>page.evaluate(`!document.getElementById('sol-design-view')`),'Escape closes inspector');
    await hotkey('design.toggle');
    await waitFor(()=>page.evaluate(`!!document.getElementById('sol-design-view')`),'reopen inspector');
    await page.evaluate(`history.pushState({},'',location.href+'#new')`);
    await waitFor(()=>page.evaluate(`!document.getElementById('sol-design-view')`),'SPA navigation removes inspector');
    await hotkey('nav.back',true);
    await waitFor(()=>page.evaluate(`!location.hash`),'back without Alt');
    await hotkey('nav.forward',true);
    await waitFor(()=>page.evaluate(`location.hash==='#new'`),'forward without Alt');
    await hotkey('notes.manage',true);
    await waitFor(async()=>{const s=await state();return s.tabs.find(t=>t.id===s.activeId)?.url==='https://start.sol/index.html#notes'},'notes without Alt');
    await hotkey('notes.open',true);
    const quick=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'quick note popup'));
    await waitFor(()=>quick.evaluate(`!!document.querySelector('.notes-text')`),'quick note without Alt');
    await stop();console.log('PASS design Alt hover, CSS, contrast, ordinary clicks, close/navigation, themes and 100/90/80 scale');process.exit(0);
  }
  if (process.argv.includes('--anchors')) {
    await pageAt('/a');
    const sidebar = await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#sidebar')),'sidebar'));
    for (const view of [ui,sidebar]) await view.evaluate(`window.__originalPost=chrome.webview.postMessage.bind(chrome.webview);chrome.webview.postMessage=m=>{try{window.__buttonCommand=JSON.parse(m)}catch{}window.__originalPost(m)}`);
    for (const scale of [0,1,2]) {
      await command({type:'settings.set',id:'uiScale',index:scale});
      await delay(200);
      for (const [action,button,view] of [['history.open','history.open',sidebar],['downloads.open','downloads.open',sidebar],['settings.open','settings.open',ui],['profile.menu','profile.menu',ui],['translation.open','translation.open',ui],['extensions.open','extensions.menu',ui]]) {
        await waitFor(()=>view.evaluate(`!!document.querySelector('[data-action="${button}"]')`),'action button ready');
        await view.evaluate(`document.querySelector('[data-action="${button}"]').click()`);
        const clicked = await view.evaluate('window.__buttonCommand');
        assert.equal(clicked.type,button);
        await command({type:'popup.close'});
        await delay(250);
        await view.evaluate('window.__buttonCommand=null');
        await hotkey(action);
        const keyboard = await waitFor(()=>view.evaluate(`window.__buttonCommand?.type==='${button}' && window.__buttonCommand`),'shortcut invokes actual button');
        assert.deepEqual(keyboard,clicked,`same anchor and action: ${action}, scale ${scale}`);
        await command({type:'popup.close'});
        await delay(250);
      }
    }
    await stop(); console.log('PASS keyboard menus use their button positions and actions at 100/90/80 scale'); process.exit(0);
  }
  if (process.argv.includes('--palette')) {
    if (process.argv.includes('--small')) assert.ok(await ui.evaluate('innerWidth<=400 && innerHeight<=400'),'test window is actually small');
    const first = (await state()).activeId;
    await pageAt('/a');
    assert.equal(await ui.evaluate(`Array.from(document.querySelectorAll('button')).filter(b=>/Palette de commandes|Command palette/.test(b.getAttribute('aria-label'))).length`),0,'commands belong in settings');
    const open = async () => {
      await command({type:'popup.close'});
      await delay(400); // Respect the existing menu's double-click close guard.
      await command({type:'settings.open',x:500,y:70});
      const popup = await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'palette popup'));
      await waitFor(()=>popup.evaluate(`!!document.querySelector('.settings-nav')`),'settings visible');
      await popup.evaluate(`Array.from(document.querySelectorAll('.settings-nav button')).find(b=>/Général|General/.test(b.textContent)).click()`);
      assert.ok(await popup.evaluate(`Array.from(document.querySelectorAll('.shortcuts-list kbd')).some(k=>k.textContent==='Ctrl+H')`),'history shortcut shown in settings');
      await popup.evaluate(`document.querySelector('.shortcuts-heading button').click()`);
      await waitFor(()=>popup.evaluate(`!!document.querySelector('.palette-search input')`),'palette visible');
      await popup.call('Emulation.setFocusEmulationEnabled',{enabled:true});
      await popup.evaluate(`document.querySelector('.palette-search input').focus()`);
      return popup;
    };
    const query = async (popup,text) => {
      await popup.evaluate(`document.querySelector('.palette-search input').select()`);
      await popup.call('Input.insertText',{text});
      await waitFor(()=>popup.evaluate(`document.querySelector('.palette-search input').value === ${JSON.stringify(text)}`),'palette query');
    };
    const key = async (popup,key,code) => {
      await popup.call('Input.dispatchKeyEvent',{type:'keyDown',key,code:key,windowsVirtualKeyCode:code});
      await popup.call('Input.dispatchKeyEvent',{type:'keyUp',key,code:key,windowsVirtualKeyCode:code});
    };
    for (const [action,title] of [['history.open','Historique|History'],['downloads.open','Téléchargements|Downloads']]) {
      await hotkey(action);
      const view = await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'shortcut popup'));
      await waitFor(()=>view.evaluate(`new RegExp(${JSON.stringify(title)}).test((document.querySelector('.popup-header-title')||document.querySelector('[aria-selected=true]'))?.textContent||'')`),'shortcut opens expected action');
    }
    await hotkey('settings.open');
    const settings = await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'settings shortcut popup'));
    await waitFor(()=>settings.evaluate(`!!document.querySelector('.settings-nav')`),'settings shortcut');
    await settings.evaluate(`document.querySelector('.shortcuts-settings details').open=true`);
    assert.ok(await settings.evaluate(`document.querySelector('.shortcuts-list').clientHeight>100`),'shortcut list is visible');
    let popup = await open();
    const initialList = await popup.evaluate(`(()=>{const r=document.querySelector('.palette-results');return {height:r.clientHeight,scroll:r.scrollHeight};})()`);
    assert.ok(initialList.height> (process.argv.includes('--small') ? 80 : 250),'palette uses available window height');
    assert.ok(initialList.scroll>=initialList.height);
    const stale = await popup.evaluate(`document.querySelector('[role=option]').id.replace('palette-result-','')`);
    await query(popup,'telechargements');
    await waitFor(()=>popup.evaluate(`document.querySelectorAll('[role=option]').length===1`),'accent insensitive action search');
    await key(popup,'Enter',13);
    await waitFor(()=>popup.evaluate(`!document.querySelector('.palette-search') && /Téléchargements|Downloads/.test(document.querySelector('.popup-header-title')?.textContent || '')`),'downloads opened');
    popup = await open();
    const before = (await state()).tabs.length;
    await command({type:'palette.pick',id:stale});
    await command({type:'palette.pick',id:'window.close'});
    assert.equal((await state()).tabs.length,before);
    assert.ok(await popup.evaluate(`!!document.querySelector('.palette-search')`));
    await query(popup,'no-such-command-zzz');
    await waitFor(()=>popup.evaluate(`!!document.querySelector('.palette-empty')`),'empty search');
    await key(popup,'Enter',13);
    assert.equal((await state()).tabs.length,before);
    await key(popup,'Escape',27);
    await command({type:'tab.open',url:origin+'/b'});
    await pageAt('/b');
    await hotkey('tab.1');
    await waitFor(async()=>(await state()).activeId===first,'Ctrl+1 selects first tab');
    await hotkey('tab.9');
    await waitFor(async()=>{const s=await state();return s.activeId===s.tabs.at(-1).id;},'Ctrl+9 selects last tab');
    popup = await open();
    await query(popup,'/a');
    await waitFor(()=>popup.evaluate(`document.querySelectorAll('[role=option]').length===1`),'tab URL search');
    await key(popup,'Enter',13);
    await waitFor(async()=>(await state()).activeId===first,'switch to matching tab');
    popup = await open();
    await query(popup,'notes');
    await waitFor(()=>popup.evaluate(`document.querySelectorAll('[role=option]').length===2`),'notes actions');
    await key(popup,'ArrowDown',40);
    assert.equal(await popup.evaluate(`document.querySelector('[aria-selected=true]').dataset.paletteIndex`),'1');
    await key(popup,'ArrowUp',38);
    await key(popup,'Enter',13);
    await waitFor(async()=>{const s=await state();return s.tabs.find(t=>t.id===s.activeId)?.url==='https://start.sol/index.html#notes';},'notes opened from palette');
    popup = await open();
    assert.ok(await popup.evaluate(`!Array.from(document.querySelectorAll('[role=option]')).some(b=>/Mode lecteur|Reader mode|Traduire la page|Translate page/.test(b.textContent))`),'reader/translation excluded on internal notes page');
    for (const [index,color] of [[1,'light'],[2,'dark']]) {
      await command({type:'settings.set',id:'theme',index});
      await waitFor(()=>popup.evaluate(`document.documentElement.dataset.theme === '${color}'`),'palette theme');
      for (const scale of [0,1,2]) {
        await command({type:'settings.set',id:'uiScale',index:scale});
        await waitFor(()=>popup.evaluate(`document.body.style.zoom === '${[.92,.89,.86][scale]}'`),'palette scale');
        assert.ok(await popup.evaluate(`document.querySelector('.palette-search input').getBoundingClientRect().width>100`));
        assert.ok(await popup.evaluate(`document.querySelector('.palette-footer').getBoundingClientRect().bottom <= innerHeight + 2`),'footer is visible without outer scrolling');
      }
      const shot = await popup.call('Page.captureScreenshot',{format:'png'});
      await writeFile(resolve(base,`palette-${color}.png`),Buffer.from(shot.data,'base64'));
    }
    await query(popup,'preferences');
    await command({type:'settings.set',id:'uiScale',index:0});
    assert.equal(await popup.evaluate(`document.querySelector('.palette-search input').value`),'preferences');
    await command({type:'popup.close'});
    await stop(); console.log('PASS palette actions, tabs, accents, keyboard, stale choices, context, themes and 100/90/80 scale'); process.exit(0);
  }
  if (process.argv.includes('--reader')) {
    const page = await pageAt('/a'), id = (await state()).activeId;
    const originalUrl = await page.evaluate('location.href');
    await page.evaluate(`document.body.innerHTML='<nav>Navigation sans intérêt</nav><input value="conservé"><article><h1>Article de test Sol</h1><p rel="author">Auteur Sol</p>'+Array.from({length:20},(_,i)=>'<p>Paragraphe '+i+' : '+('Ce texte décrit le fonctionnement du mode lecture et garde les paragraphes de cet article. '.repeat(8)))+'</article>';document.querySelector('input').focus();window.scrollTo(0,300)`);
    const scroll = await page.evaluate('scrollY');
    await command({type:'reader.toggle',id});
    await waitFor(()=>page.evaluate(`!!document.getElementById('sol-reader-view')?.shadowRoot.querySelector('main')`),'reader shown');
    assert.equal((await state()).activeId,id);
    const readerPill = await page.evaluate(`(()=>{const r=document.getElementById('sol-reader-view').shadowRoot.querySelector('nav').getBoundingClientRect();return {bottom:innerHeight-r.bottom,center:r.left+r.width/2,width:innerWidth};})()`);
    assert.ok(Math.abs(readerPill.center-readerPill.width/2)<2);
    assert.ok(readerPill.bottom>=16 && readerPill.bottom<=20);
    assert.equal(await page.evaluate('location.href'),originalUrl);
    assert.ok(await page.evaluate(`document.getElementById('sol-reader-view').solArticle.blocks.length >= 20`));
    await delay(1200); // Windows enumerates voices asynchronously through voiceschanged.
    const voiceCount = await page.evaluate(`speechSynthesis.getVoices().filter(v=>v.localService).length`);
    if (voiceCount) {
      await page.evaluate(`(()=>{const r=document.getElementById('sol-reader-view').shadowRoot,s=r.querySelector('select');s.value=s.options[1].value;s.dispatchEvent(new Event('change'));r.querySelectorAll('button')[2].click();})()`);
      await waitFor(()=>page.evaluate(`speechSynthesis.speaking || speechSynthesis.pending`),'native speech started');
      await page.evaluate(`document.getElementById('sol-reader-view').shadowRoot.querySelectorAll('button')[2].click()`);
      assert.equal(await page.evaluate(`document.getElementById('sol-reader-view').dataset.speech`),'paused');
      await waitFor(()=>page.evaluate(`speechSynthesis.paused`),'native speech paused');
      await page.evaluate(`document.getElementById('sol-reader-view').shadowRoot.querySelectorAll('button')[2].click()`);
      assert.equal(await page.evaluate(`document.getElementById('sol-reader-view').dataset.speech`),'playing');
      await page.evaluate(`document.getElementById('sol-reader-view').shadowRoot.querySelectorAll('button')[3].click()`);
      await waitFor(()=>page.evaluate(`!speechSynthesis.speaking && !speechSynthesis.pending`),'speech stopped');
      await hotkey('reader.listen');
      await waitFor(()=>page.evaluate(`speechSynthesis.speaking || speechSynthesis.pending`),'shortcut starts speech');
      await hotkey('reader.listen');
      await waitFor(()=>page.evaluate(`document.getElementById('sol-reader-view').dataset.speech==='paused'`),'shortcut pauses speech');
      await hotkey('reader.stop');
      await waitFor(()=>page.evaluate(`!speechSynthesis.speaking && !speechSynthesis.pending`),'shortcut stops speech');
      console.log('PASS installed local voices, native speech start/pause/resume/stop');
    } else {
      assert.ok(await page.evaluate(`document.getElementById('sol-reader-view').shadowRoot.querySelectorAll('button')[2].disabled`));
      console.log('PASS missing local voices: listening disabled with useful message');
    }
    assert.equal(await page.evaluate(`document.getElementById('sol-reader-view').shadowRoot.querySelectorAll('script,iframe,img').length`),0);
    await page.evaluate(`document.getElementById('sol-reader-view').shadowRoot.querySelectorAll('button')[1].click()`);
    assert.equal(await page.evaluate(`getComputedStyle(document.getElementById('sol-reader-view').shadowRoot.querySelector('.view')).fontSize`),'22px');
    for (const index of [0,1,2]) {
      await command({type:'settings.set',id:'uiScale',index});
      await delay(150);
      assert.equal(await page.evaluate(`getComputedStyle(document.getElementById('sol-reader-view').shadowRoot.querySelector('.view')).fontSize`),'22px');
    }
    for (const [index,color] of [[1,'rgb(238, 240, 226)'],[2,'rgb(18, 18, 20)']]) {
      await command({type:'settings.set',id:'theme',index});
      await waitFor(()=>page.evaluate(`getComputedStyle(document.getElementById('sol-reader-view').shadowRoot.querySelector('.view')).backgroundColor === '${color}'`),'reader theme');
    }
    await page.call('Page.captureScreenshot',{format:'png'}).then(r=>writeFile(resolve(base,'reader.png'),Buffer.from(r.data,'base64')));
    if (voiceCount) {
      await page.evaluate(`document.getElementById('sol-reader-view').shadowRoot.querySelectorAll('button')[2].click()`);
      await waitFor(()=>page.evaluate(`speechSynthesis.speaking || speechSynthesis.pending`),'speech restarted before exit');
    }
    await command({type:'reader.toggle',id});
    await waitFor(()=>page.evaluate(`!document.getElementById('sol-reader-view')`),'reader removed');
    assert.equal(await page.evaluate('speechSynthesis.pending || speechSynthesis.speaking'),false);
    assert.equal(await page.evaluate(`document.querySelector('input').value`),'conservé');
    assert.equal(await page.evaluate('scrollY'),scroll);
    await page.evaluate(`window.__installedVoices=speechSynthesis.getVoices.bind(speechSynthesis);speechSynthesis.getVoices=()=>[]`);
    await command({type:'reader.toggle',id});
    await waitFor(()=>page.evaluate(`document.getElementById('sol-reader-view')?.shadowRoot.querySelectorAll('button')[2].disabled`),'missing voices disables listening');
    assert.ok(await page.evaluate(`document.getElementById('sol-reader-view').shadowRoot.querySelector('[role=status]').textContent.includes('No local voice') || document.getElementById('sol-reader-view').shadowRoot.querySelector('[role=status]').textContent.includes('Aucune voix')`));
    await page.evaluate(`speechSynthesis.getVoices=window.__installedVoices;history.pushState({},'',location.href+'#navigation')`);
    await waitFor(()=>page.evaluate(`!document.getElementById('sol-reader-view') && !document.body.inert`),'SPA navigation exits reader and restores page');
    assert.equal(await page.evaluate('speechSynthesis.pending || speechSynthesis.speaking'),false);
    await page.evaluate(`document.body.innerHTML='<h1>Connexion</h1><input>'`);
    await command({type:'reader.toggle',id});
    const popup = await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'reader notice'));
    await waitFor(()=>popup.evaluate(`document.body.textContent.includes('Reader mode unavailable') || document.body.textContent.includes('Mode lecteur indisponible')`),'unsupported page notice');
    assert.equal(await page.evaluate(`!!document.getElementById('sol-reader-view')`),false);
    await stop(); console.log('PASS reader in same tab, safe article text, text sizing, original form/scroll preserved and unsupported pages'); process.exit(0);
  }
  if (process.argv.includes('--notes')) {
    await command({type:'notes.open',x:900,y:60});
    const popup = await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'notes popup'));
    await popup.call('Emulation.setFocusEmulationEnabled',{enabled:true});
    await waitFor(()=>popup.evaluate(`!!document.querySelector('.notes-text')`),'notes editor');
    await command({type:'capture.run',query:'visible',index:1});
    await waitFor(()=>readFile(resolve(base,'captures/capture.png')).then(b=>b.length>10000).catch(()=>false),'centered notes native capture');
    await popup.evaluate(`document.querySelector('.notes-text').focus()`);
    await popup.call('Input.insertText',{text:'Une note conservée dans Sol.'});
    await popup.evaluate(`Array.from(document.querySelectorAll('button')).find(b=>/^Enregistrer$|^Save$/.test(b.textContent)).click()`);
    await waitFor(()=>popup.evaluate(`document.querySelector('.notes-text').value === ''`),'note saved');
    const saved = JSON.parse(await readFile(resolve(profile,'notes.json'),'utf8'));
    assert.equal(saved[0].Text,'Une note conservée dans Sol.');
    const id = saved[0].Id;
    await command({type:'notes.draft',id:'',title:'',input:'Brouillon non enregistré'});
    await command({type:'popup.close'});
    await command({type:'notes.open',x:900,y:60});
    await waitFor(()=>popup.evaluate(`document.querySelector('.notes-text')?.value === 'Brouillon non enregistré'`),'draft restored');
    await command({type:'notes.edit',id});
    await waitFor(()=>popup.evaluate(`!!document.querySelector('[role=alert]')`),'unsaved draft protected');
    await command({type:'notes.discard'});
    await command({type:'nav.go',id:(await state()).activeId,input:origin+'/a'});
    const page = await pageAt('/a');
    await command({type:'popup.close'});
    await page.call('Emulation.setFocusEmulationEnabled',{enabled:true});
    const select = async () => {
      const point = await page.evaluate(`(() => {const el=document.querySelector('h1'),r=document.createRange();r.selectNodeContents(el);const s=getSelection();s.removeAllRanges();s.addRange(r);const b=el.getBoundingClientRect();return {x:b.left+40,y:b.top+15};})()`);
      await page.call('Input.dispatchMouseEvent',{type:'mousePressed',button:'right',clickCount:1,...point});
      await page.call('Input.dispatchMouseEvent',{type:'mouseReleased',button:'right',clickCount:1,...point});
      await waitFor(()=>popup.evaluate(`Array.from(document.querySelectorAll('.row')).some(b=>/Ajouter à une note|Add to a note/.test(b.textContent))`),'selection notes action');
      await popup.evaluate(`Array.from(document.querySelectorAll('.row')).find(b=>/Ajouter à une note|Add to a note/.test(b.textContent)).dispatchEvent(new MouseEvent('mousedown',{bubbles:true}))`);
      await waitFor(()=>popup.evaluate(`document.body.textContent.includes('Une nouvelle note') || document.body.textContent.includes('A new note')`),'choose note');
    };
    await select();
    await popup.evaluate(`document.querySelector('.popup-list .row').click()`);
    await waitFor(async()=>JSON.parse(await readFile(resolve(profile,'notes.json'),'utf8'))[0].Text.includes('Server page /a'),'selection appended to existing note');
    assert.ok(JSON.parse(await readFile(resolve(profile,'notes.json'),'utf8'))[0].Text.includes(origin+'/a'));
    await command({type:'popup.close'});
    await select();
    await popup.evaluate(`document.querySelector('.row').click()`);
    await waitFor(()=>popup.evaluate(`document.querySelector('.notes-text')?.value === 'Server page /a'`),'selection in new note');
    await popup.evaluate(`Array.from(document.querySelectorAll('button')).find(b=>/^Enregistrer$|^Save$/.test(b.textContent)).click()`);
    await waitFor(async()=>JSON.parse(await readFile(resolve(profile,'notes.json'),'utf8')).length === 2,'second note saved');
    await command({type:'notes.manage'});
    const notes = await connect(await waitFor(async()=>(await targets()).find(t=>t.url === 'https://start.sol/index.html#notes'),'notes workspace'));
    await notes.call('Emulation.setFocusEmulationEnabled',{enabled:true});
    await waitFor(()=>notes.evaluate(`document.querySelectorAll('.notes-preview').length === 2`),'notes workspace list');
    assert.ok(await notes.evaluate(`!window.__navBlurHost`),'notes footer controls are never blurred');
    await notes.evaluate(`chrome.webview.postMessage(JSON.stringify({type:'window.close'}))`);
    await delay(200);
    assert.ok((await state()).activeId,'notes page accepts only its own commands');
    await notes.evaluate(`document.querySelector('.notes-preview').click()`);
    await waitFor(()=>notes.evaluate(`!!document.querySelector('.notes-writing')`),'document editor');
    await command({type:'tab.activate',id:(await state()).tabs.find(t=>t.url === origin+'/a').id});
    await select();
    await popup.evaluate(`document.querySelector('.row').click()`);
    await waitFor(()=>popup.evaluate(`document.querySelector('.notes-text')?.value === 'Server page /a'`),'saved note does not block a new selection note');
    await command({type:'notes.discard'});
    await command({type:'notes.manage'});
    await notes.evaluate(`document.querySelector('.notes-preview').click()`);
    await waitFor(()=>notes.evaluate(`!!document.querySelector('.notes-writing')`),'document editor returns');
    for (const index of [0,1,2]) {
      await command({type:'settings.set',id:'uiScale',index});
      await waitFor(()=>notes.evaluate(`document.body.style.zoom === '${[.92,.89,.86][index]}'`),'notes interface scale');
      const geometry = await notes.evaluate(`(() => {const r=document.querySelector('.notes-document').getBoundingClientRect(); return {right:r.right,bottom:r.bottom,width:innerWidth,height:innerHeight};})()`);
      assert.ok(geometry.right <= geometry.width+1 && geometry.bottom <= geometry.height+1,`notes workspace fits: ${JSON.stringify(geometry)}`);
      await delay(300);
      await writeFile(resolve(base,`notes-scale-${index}.png`),Buffer.from((await notes.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    }
    const notesRatio = await notes.evaluate('devicePixelRatio');
    await command({type:'zoom.out'});
    assert.equal(await notes.evaluate('devicePixelRatio'),notesRatio,'website zoom never changes the notes interface');
    await command({type:'settings.set',id:'theme',index:1});
    await delay(300);
    await writeFile(resolve(base,'notes-light.png'),Buffer.from((await notes.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await command({type:'split.open'});
    await delay(300);
    assert.ok(await notes.evaluate(`document.querySelector('.notes-document').getBoundingClientRect().right <= innerWidth+1`),'notes fit split view');
    await command({type:'split.close'});
    await stop(); await launch();
    await command({type:'notes.manage'});
    const reopened = await connect(await waitFor(async()=>(await targets()).find(t=>t.url === 'https://start.sol/index.html#notes'),'reopened notes'));
    await waitFor(()=>reopened.evaluate(`document.querySelectorAll('.notes-preview').length === 2`),'notes persist after restart');
    await reopened.call('Emulation.setFocusEmulationEnabled',{enabled:true});
    await reopened.evaluate(`document.querySelector('input[type=search]').focus()`);
    await reopened.call('Input.insertText',{text:'Une note conservée'});
    await waitFor(()=>reopened.evaluate(`document.querySelectorAll('.notes-preview').length === 1`),'notes search');
    await reopened.evaluate(`document.querySelector('.notes-preview').click()`);
    await waitFor(()=>reopened.evaluate(`document.querySelector('.notes-writing')?.value.includes('Source :')`),'edit note with source');
    await command({type:'notes.draft',id,title:'Texte modifié',input:'Edition vérifiée'});
    await command({type:'notes.save'});
    await waitFor(async()=>JSON.parse(await readFile(resolve(profile,'notes.json'),'utf8')).find(n=>n.Id === id)?.Title === 'Texte modifié','edited note persisted');
    await command({type:'notes.delete',id});
    await waitFor(async()=>JSON.parse(await readFile(resolve(profile,'notes.json'),'utf8')).length === 1,'note deleted');
    await stop(); await launch(['--private']);
    await command({type:'notes.open',x:900,y:60});
    const privatePopup = await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'private notes'));
    await waitFor(()=>privatePopup.evaluate(`!!document.querySelector('.notes-text')`),'private notes ready');
    await command({type:'notes.manage'});
    const privateNotes = await connect(await waitFor(async()=>(await targets()).find(t=>t.url === 'https://start.sol/index.html#notes'),'private workspace'));
    await waitFor(()=>privateNotes.evaluate(`!!document.querySelector('.notes-browser')`),'private workspace ready');
    assert.equal(await privateNotes.evaluate(`document.querySelectorAll('.notes-preview').length`),0);
    await command({type:'notes.draft',id:'',title:'',input:'Privé uniquement'});
    await command({type:'notes.save'});
    await waitFor(()=>privateNotes.evaluate(`document.querySelectorAll('.notes-preview').length === 1`),'private note saved in memory');
    assert.equal(JSON.parse(await readFile(resolve(profile,'notes.json'),'utf8')).length,1,'private notes never overwrite normal notes');
    await stop();
    console.log('PASS notes UI, draft safety, new/existing selection, source URLs, scales, persistence and private isolation');
    console.log(`Evidence: ${base}`);
    process.exit(0);
  }
  if (process.argv.includes('--languages')) {
    await command({type:'settings.open',x:1000,y:50});
    const settingsPopup=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'language settings'));
    await waitFor(()=>settingsPopup.evaluate(`/Orthographe|Spelling/.test(document.body.textContent)`),'spelling settings');
    console.log('Windows dictionaries:', await settingsPopup.evaluate(`Array.from(document.querySelectorAll('.row-hint')).find(p=>/dictionnaires Windows|Windows dictionaries/.test(p.textContent))?.textContent`));
    await command({type:'popup.close'});
    const page = await pageAt('/a');
    await page.call('Emulation.setFocusEmulationEnabled', {enabled:true});
    await page.evaluate(`document.querySelector('main').innerHTML = '<h1>Orthographe locale</h1><textarea id="writing" style="font:24px/1.5 system-ui;width:600px;height:150px;padding:16px">Bonjour hello bonjourr helooo</textarea><p contenteditable="true" style="font:24px system-ui">Bonjour hello bonjourr</p>'`);
    await page.evaluate(`document.querySelector('#writing').focus()`);
    await waitFor(()=>page.evaluate(`document.querySelector('[data-sol-spelling]')?.dataset.solSpelling === '2'`),'bilingual local spelling only flags errors');
    await writeFile(resolve(base,'spelling.png'),Buffer.from((await page.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    const wordPoint = await page.evaluate(`(() => {const el=document.querySelector('#writing'),box=el.getBoundingClientRect(),g=document.createElement('canvas').getContext('2d');g.font=getComputedStyle(el).font;return {x:box.left+18+g.measureText('Bonjour hello bon').width,y:box.top+35};})()`);
    await page.call('Input.dispatchMouseEvent',{type:'mousePressed',button:'right',clickCount:1,...wordPoint});
    await page.call('Input.dispatchMouseEvent',{type:'mouseReleased',button:'right',clickCount:1,...wordPoint});
    const corrections=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'correction menu'));
    await waitFor(()=>corrections.evaluate(`Array.from(document.querySelectorAll('.row')).some(r=>/^bonjour$/i.test(r.textContent.trim()))`),'right click suggests the corrected word');
    await writeFile(resolve(base,'spelling-menu.png'),Buffer.from((await corrections.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await corrections.evaluate(`Array.from(document.querySelectorAll('.row')).find(r=>/^bonjour$/i.test(r.textContent.trim())).dispatchEvent(new MouseEvent('mousedown',{bubbles:true}))`);
    await waitFor(()=>page.evaluate(`!document.querySelector('#writing').value.includes('bonjourr')`),'suggestion replaces only the misspelled word');
    await page.evaluate(`document.querySelector('#writing').focus();document.execCommand('undo')`);
    await waitFor(()=>page.evaluate(`document.querySelector('#writing').value === 'Bonjour hello bonjourr helooo'`),'correction can be undone');
    await command({type:'settings.set',id:'spelling',index:0});
    await waitFor(()=>page.evaluate(`!document.querySelector('[data-sol-spelling]') && document.querySelector('#writing').getAttribute('spellcheck') === null`),'off restores page attributes');
    await command({type:'settings.set',id:'spelling',index:1});
    await waitFor(()=>page.evaluate(`document.querySelector('[data-sol-spelling]')?.dataset.solSpelling === '2'`),'French dictionary selection');
    await command({type:'settings.set',id:'spelling',index:3});
    await page.evaluate(`document.querySelector('p[contenteditable]').focus()`);
    await waitFor(()=>page.evaluate(`document.querySelector('[data-sol-spelling]')?.dataset.solSpelling === '1'`),'contenteditable spelling');
    await writeFile(resolve(base,'spelling-editor.png'),Buffer.from((await page.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await page.evaluate(`window.__solSpelling.read = () => ({unexpected:true})`);
    await delay(600);
    assert.ok((await state()).activeId,'untrusted page response cannot crash Sol');
    const id=(await state()).activeId;
    await command({type:'translation.open',id,x:1000,y:45});
    const popup=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'translation pill'));
    await waitFor(()=>popup.evaluate(`!!document.querySelector('.translation-popup .popup-seg-btn')`),'translation language');
    for (const [language, browser, expected] of [['fr-FR',1,'anglais'],['en-US',1,'français'],['es',2,'English'],['ja',1,'français']]) {
      await command({type:'popup.close'});
      await page.evaluate(`document.documentElement.lang=${JSON.stringify(language)}`);
      await command({type:'settings.set',id:'language',index:browser});
      await command({type:'translation.open',id,x:1000,y:45});
      await waitFor(()=>popup.evaluate(`document.querySelector('.translation-popup .popup-seg-btn')?.textContent.includes(${JSON.stringify(expected)})`),`single target for ${language}`);
      assert.equal(await popup.evaluate(`document.querySelectorAll('.translation-popup .popup-seg-btn').length`),1,'only the appropriate language is offered');
    }
    assert.ok(await popup.evaluate(`Array.from(document.querySelectorAll('.translation-popup .popup-seg-btn')).every(b=>b.disabled)`),'local pages cannot be sent to Google');
    await writeFile(resolve(base,'translation.png'),Buffer.from((await popup.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await command({type:'popup.close'});
    await command({type:'nav.go',id,input:'https://example.com/'});
    await waitFor(async()=>(await state()).tabs.find(t=>t.id===id)?.url==='https://example.com/','public page address');
    await ui.evaluate(`document.querySelector('button[aria-label="Traduire la page"],button[aria-label="Translate page"]').click()`);
    await waitFor(()=>popup.evaluate(`document.querySelector('.translation-popup .popup-seg-btn')?.disabled === false`),'public page translation enabled');
    await delay(300);
    await writeFile(resolve(base,'translation-public.png'),Buffer.from((await popup.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await popup.evaluate(`document.querySelector('.translation-popup .popup-seg-btn').click()`);
    await waitFor(async()=>(await state()).tabs.find(t=>t.id===id)?.translated,'Google translation stays in the same tab');
    assert.equal((await state()).activeId,id,'translation retains the active tab');
    await waitFor(async()=>JSON.parse(await readFile(resolve(profile,'session.json'),'utf8').catch(()=>'{}')).TranslationOriginals?.includes('https://example.com/'),'original page retained in the session');
    await command({type:'translation.open',id,x:1000,y:45});
    await waitFor(()=>popup.evaluate(`/Voir l.original|View original/.test(document.querySelector('.translation-popup')?.textContent)`),'return to original action');
    await delay(300);
    await writeFile(resolve(base,'translation-original.png'),Buffer.from((await popup.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await popup.evaluate(`Array.from(document.querySelectorAll('.translation-popup .pill-action')).find(b=>/Voir l.original|View original/.test(b.textContent)).click()`);
    await waitFor(async()=>(await state()).tabs.find(t=>t.id===id)?.url==='https://example.com/','original restored in the same tab');
    assert.equal((await state()).activeId,id,'return to original retains the active tab');
    console.log('PASS local dictionary, correction menu, undo, contenteditable and Google translation flow');
    await stop();
    console.log(`Evidence: ${base}`);
    process.exit(0);
  }
  if (process.argv.includes('--history')) {
    const page = await pageAt('/a');
    await waitFor(() => page.evaluate('document.readyState === "complete" && document.body.textContent.includes("Server page")'), 'initial visit recorded');
    const sidebar = await connect((await targets()).find(t => t.url.endsWith('#sidebar')));
    await waitFor(() => sidebar.evaluate('!!document.querySelector("button[aria-label=Historique]")'), 'history shortcut');
    await sidebar.evaluate('document.querySelector("button[aria-label=Historique]").click()');
    const library = await connect((await targets()).find(t => t.url.endsWith('#popup')));
    await waitFor(() => library.evaluate('document.querySelectorAll(".popup-list .row").length === 100'), 'first 100 history pages');
    await library.evaluate('document.querySelector("button[aria-label=\\"Page suivante\\"]").click()');
    await waitFor(() => library.evaluate('document.querySelector(".library-pages")?.textContent.includes("101–126")'), 'older imported history page');
    assert.ok(await library.evaluate('document.querySelector(".popup-list").textContent.includes("Older imported target")'));
    await library.evaluate('document.querySelector(".library-search").focus()');
    await library.call('Input.insertText', { text: 'Older imported target' });
    await waitFor(() => library.evaluate('document.querySelectorAll(".popup-list .row").length === 1 && !document.querySelector(".library-pages")'), 'search includes old imports');
    assert.ok(await library.evaluate('document.querySelector("[role=tab][aria-selected=true]").textContent.includes("Historique")'), 'search keeps the history tab selected');
    await library.evaluate('document.querySelector("[role=tab]").dispatchEvent(new MouseEvent("mousedown", { bubbles:true }))');
    await waitFor(() => library.evaluate('document.querySelector(".popup-list").textContent.includes("Older imported target favorite")'), 'imported favorite accessible');
    await writeFile(resolve(base, 'imported-library.png'), Buffer.from((await library.call('Page.captureScreenshot', { format:'png' })).data, 'base64'));
    console.log('PASS older imported history pagination, search, active tab and bookmarks');
    await stop();
  } else {
  if (process.argv.includes('--scale')) {
    const sidebar = await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#sidebar')),'sidebar'));
    const page = await pageAt('/a');
    const pageZoom = await page.evaluate('devicePixelRatio');
    for (const [index, factor] of [[0,0.92],[1,0.89],[2,0.86],[0,0.92]]) {
      await command({type:'settings.set',id:'uiScale',index});
      await waitFor(()=>ui.evaluate(`document.body.style.zoom === '${factor}'`),'toolbar scale');
      await waitFor(()=>sidebar.evaluate(`document.body.style.zoom === '${factor}'`),'sidebar scale');
      assert.ok(await sidebar.evaluate(`Array.from(document.querySelectorAll('.sidebar-quick-actions button')).length > 0 && Array.from(document.querySelectorAll('.sidebar-quick-actions button')).every(b=>{const r=b.getBoundingClientRect();return r.top>=0 && r.bottom<=innerHeight && r.right<=innerWidth;})`),'sidebar shortcuts remain visible');
      assert.equal(await page.evaluate('devicePixelRatio'),pageZoom,'website zoom stays independent');
      await writeFile(resolve(base,`sidebar-scale-${index}.png`),Buffer.from((await sidebar.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    }
    console.log('PASS 100/90/80 interface sizes, visible sidebar shortcuts and independent website zoom');
    await stop();
    console.log(`Evidence: ${base}`);
    process.exit(0);
  }
  if (process.argv.includes('--extensions')) {
    const page = await pageAt('/a');
    const initialWidth = await page.evaluate('innerWidth');
    await command({type:'zoom.out'});
    await waitFor(async()=>await page.evaluate('innerWidth') > initialWidth*1.08, 'page zoom');
    const id = (await state()).activeId;
    await command({type:'nav.go',id,input:`${origin}/document`});
    const pdf = await pageAt('/document');
    await waitFor(async()=>(await state()).tabs.find(t=>t.id===id).zoom===100, 'PDF independent zoom');
    await delay(800);
    await writeFile(resolve(base,'pdf.png'),Buffer.from((await pdf.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await command({type:'zoom.out'});
    await command({type:'nav.go',id,input:`${origin}/a`});
    await pageAt('/a');
    assert.equal((await state()).tabs.find(t=>t.id===id).zoom,90,'PDF leaves website zoom unchanged');
    console.log('PASS native PDF served without .pdf extension, independent from website zoom');
    await command({type:'capture.menu',x:900,y:50});
    await delay(300);
    const captureUi=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#capture')),'capture controls'));
    await captureUi.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.event==='capture-state')window.__captureResult=e.data;});`);
    await waitFor(()=>captureUi.evaluate('!!document.querySelector(".capture-action")'),'screenshot options');
    await captureUi.evaluate('document.querySelector(".capture-action").click()');
    const capture = await waitFor(async()=>readFile(resolve(base,'captures/capture.png')).catch(()=>null),'4K screenshot');
    assert.equal(capture.readUInt32BE(16),3840,'screenshot renders at 3840 pixels wide');
    await waitFor(async()=>readFile(resolve(base,'captures/capture.pdf')).catch(()=>null),'PDF image capture');
    console.log('PASS 4K PNG and high quality PDF screenshot');
    await command({type:'capture.menu'});
    await waitFor(()=>captureUi.evaluate('!!document.querySelector(".capture-action")'),'capture options reopened');
    await captureUi.evaluate('document.querySelector(".capture-browser").click()');
    assert.match(await captureUi.evaluate('document.body.innerText'),/Fenêtre entière|Entire window/);
    await captureUi.evaluate('window.__captureResult=null;document.querySelector(".capture-action").click()');
    await waitFor(()=>captureUi.evaluate('window.__captureResult?.kind === "done"'),'whole browser screenshot',40000);
    const fullBrowser=await readFile(resolve(base,'captures/capture.png'));
    assert.equal(fullBrowser.readUInt32BE(20),2160,'whole browser capture fits within 4K');
    assert.ok(!fullBrowser.equals(capture),'window capture differs from page-only capture');
    await writeFile(resolve(base,'whole-browser.png'),fullBrowser);
    console.log('PASS native whole browser screenshot including toolbar and sidebar, without screen sharing');
    await command({type:'capture.menu'});
    await waitFor(()=>captureUi.evaluate('!!document.querySelector(".capture-action")'),'whole browser area options');
    await captureUi.evaluate('document.querySelector(".capture-modes button").click()');
    await captureUi.evaluate('window.__captureResult=null;document.querySelector(".capture-action").click()');
    await waitFor(()=>captureUi.evaluate('!!document.querySelector(".capture-selection img")?.naturalWidth'),'window region preview',40000);
    const dimensions=await captureUi.evaluate('({w:innerWidth,h:innerHeight})');
    await captureUi.call('Input.dispatchMouseEvent',{type:'mousePressed',x:dimensions.w*.1,y:dimensions.h*.2,button:'left',clickCount:1});
    await captureUi.call('Input.dispatchMouseEvent',{type:'mouseMoved',x:dimensions.w*.6,y:dimensions.h*.7,button:'left',buttons:1});
    await captureUi.call('Input.dispatchMouseEvent',{type:'mouseReleased',x:dimensions.w*.6,y:dimensions.h*.7,button:'left',clickCount:1});
    await waitFor(()=>captureUi.evaluate('window.__captureResult?.kind === "done"'),'whole browser selected area',40000);
    const selectedArea=await readFile(resolve(base,'captures/capture.png'));
    assert.ok(Math.abs(selectedArea.readUInt32BE(16)-fullBrowser.readUInt32BE(16)*.5)<=2);
    assert.ok(Math.abs(selectedArea.readUInt32BE(20)-fullBrowser.readUInt32BE(20)*.5)<=2);
    console.log('PASS full interface region selection and cropped screenshot');
    await command({type:'settings.set',id:'uiScale',index:2});
    await command({type:'capture.menu'});
    await waitFor(()=>captureUi.evaluate('!!document.querySelector(".capture-action")'),'capture options at 80%');
    await captureUi.call('Emulation.setDeviceMetricsOverride',{width:360,height:160,deviceScaleFactor:1,mobile:false});
    assert.ok(await captureUi.evaluate('document.querySelector(".capture-action").getBoundingClientRect().right <= innerWidth && document.querySelector(".capture-close").getBoundingClientRect().bottom <= innerHeight'),'capture controls fit narrow window at 80%');
    await captureUi.call('Emulation.clearDeviceMetricsOverride');
    const sizesBefore=(await readFile(resolve(profile,'debug.log'),'utf8')).split('commande capture.size').length;
    await command({type:'capture.menu'});await delay(150);await command({type:'capture.menu'});
    await waitFor(async()=>(await readFile(resolve(profile,'debug.log'),'utf8')).split('commande capture.size').length>sizesBefore,'capture controls reappear after toolbar toggle');
    await captureUi.evaluate('document.querySelector(".capture-close").click()');
    await command({type:'settings.set',id:'uiScale',index:0});
    console.log('PASS responsive capture controls at 80% interface scale');
    await command({type:'extensions.open'});
    const popup = await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'extensions manager'));
    await popup.evaluate(`chrome.webview.addEventListener('message',e=>{ if(e.data.popup) window.__extensions=e.data; })`);
    await command({type:'extension.store'});
    await waitFor(async()=>(await state()).tabs.some(t=>t.url.startsWith('https://chromewebstore.google.com/')),'store opens in a browser tab');
    const storeTab=(await state()).activeId;
    await command({type:'nav.go',id:storeTab,input:'https://chromewebstore.google.com/detail/icloud-passwords/pejdijmoenmkgeppbflobdenhhabjlaj'});
    await waitFor(()=>ui.evaluate('!!document.querySelector(".store-install")'),'native Add to Sol action');
    await ui.evaluate('document.querySelector(".store-install").click()');
    await waitFor(()=>popup.evaluate('window.__extensions?.popup === "extension-confirm" && window.__extensions.access.some(a => /applications|apps/.test(a))'), 'extension permissions review',90000);
    await command({type:'extension.confirm',id:'pejdijmoenmkgeppbflobdenhhabjlaj'});
    await waitFor(()=>popup.evaluate('window.__extensions?.installed?.some(e=>e.id === "pejdijmoenmkgeppbflobdenhhabjlaj" && e.enabled)'), 'signed iCloud extension installation',90000);
    console.log('PASS signed Chrome Web Store package installs with original iCloud identity');
    await command({type:'extension.pin',id:'pejdijmoenmkgeppbflobdenhhabjlaj'});
    await waitFor(async()=>(await state()).extensions?.some(e=>e.id==='pejdijmoenmkgeppbflobdenhhabjlaj'),'extension pinned');
    assert.ok(await ui.evaluate('document.querySelector(".pinned-extension img")?.naturalWidth >= 32'));
    assert.ok(JSON.parse(await readFile(resolve(profile,'settings.json'),'utf8')).PinnedExtensions.includes('pejdijmoenmkgeppbflobdenhhabjlaj'),'pin is persisted in profile settings');
    await command({type:'extension.show',id:'pejdijmoenmkgeppbflobdenhhabjlaj'});
    const extension = await connect(await waitFor(async()=>(await targets()).find(t=>t.url.startsWith('chrome-extension://pejdijmoenmkgeppbflobdenhhabjlaj/') && t.type==='page'), 'iCloud popup'));
    await waitFor(()=>extension.evaluate('document.readyState === "complete"').catch(()=>false),'iCloud popup ready');
    await waitFor(()=>extension.evaluate('innerWidth >= document.body.getBoundingClientRect().right && innerHeight >= document.body.getBoundingClientRect().bottom'), 'extension content fits its popup');
    for (const [width, height] of [[240,160], [520,320], [280,180]]) {
      await extension.evaluate(`document.body.className=''; document.body.style.cssText='width:${width}px;height:${height}px;min-height:0;margin:0;padding:0'; document.body.innerHTML='<div style="width:100%;height:100%">Popup size check</div>'`);
      await waitFor(()=>extension.evaluate(`Math.abs(innerWidth-${width}) <= 1 && Math.abs(innerHeight-${height}) <= 1`), `popup resizes to ${width}x${height}`);
    }
    console.log('PASS extension popup content fits, grows and shrinks to its own dimensions');
    await extension.evaluate("document.documentElement.style.cssText='width:460px;height:240px'");
    await waitFor(()=>extension.evaluate('innerWidth === 460 && innerHeight === 240'), 'extension explicit root dimensions preserved');
    console.log('PASS iCloud popup opens (no passwords inspected; real autofill remains a manual check)');
    await command({type:'extension.reload',id:'pejdijmoenmkgeppbflobdenhhabjlaj'});
    await command({type:'extension.toggle',id:'pejdijmoenmkgeppbflobdenhhabjlaj'});
    await waitFor(async()=>!(await state()).extensions?.some(e=>e.id==='pejdijmoenmkgeppbflobdenhhabjlaj'),'disabled extension leaves quick access');
    await command({type:'extensions.open'});
    await waitFor(()=>popup.evaluate('window.__extensions?.installed?.some(e=>!e.enabled)'), 'disable extension');
    await waitFor(async()=>(await state()).extensions?.length===0,'disabled extension hidden from quick access');
    await command({type:'extension.remove',id:'pejdijmoenmkgeppbflobdenhhabjlaj'});
    await waitFor(async()=>!(await readdir(resolve(profile,'extensions'))).includes('pejdijmoenmkgeppbflobdenhhabjlaj'), 'extension files removed');
    await popup.evaluate('window.__extensions = null');
    await command({type:'extensions.open'});
    await waitFor(()=>popup.evaluate('window.__extensions?.installed?.length === 0'), 'remove extension');
    console.log('PASS extension reload, disable and removal');
    await stop();
  } else {
  let s = await state();
  const first = s.activeId;
  const page = await pageAt('/a');
  const iconSidebar = await connect((await targets()).find(t=>t.url.endsWith('#sidebar')));
  await waitFor(()=>iconSidebar.evaluate('Array.from(document.querySelectorAll("img.favicon")).some(i=>i.naturalWidth === 192)'), 'high resolution icon also used in sidebar');
  const initialWidth = await page.evaluate('innerWidth');
  await command({ type: 'zoom.out' });
  await waitFor(async () => (await state()).tabs.find(t => t.id === first)?.zoom === 90, '90% zoom');
  await waitFor(async () => (await page.evaluate('innerWidth')) > initialWidth * 1.08, 'zoom changes layout');
  await command({ type: 'tab.open' });
  await waitFor(async () => (await state()).tabs.length === 2, 'new tab');
  s = await state();
  assert.equal(s.tabs.find(t => t.id === s.activeId).zoom, 90, 'new pages must inherit the profile zoom');
  await waitFor(() => ui.evaluate('document.activeElement?.matches(".address-pill input")'), 'new tab search focus');
  await waitFor(() => ui.evaluate('document.documentElement.classList.contains("painted")'), 'first painted toolbar');
  assert.equal(await ui.evaluate('document.documentElement.dataset.theme'), 'dark', 'startup uses configured theme');
  await waitFor(() => ui.evaluate('document.activeElement.selectionEnd === document.activeElement.value.length && document.activeElement.selectionStart === 0'), 'typing replaces the new tab address');
  console.log('PASS zoom applies to existing and new tabs');
  const second = s.activeId;
  const sidebarTarget = await waitFor(async () => (await targets()).find(t => t.url.endsWith('#sidebar')), 'sidebar');
  const sidebar = await connect(sidebarTarget);
  for (const index of [1,2,0]) {
    await command({type:'settings.set',id:'uiScale',index});
    await waitFor(()=>sidebar.evaluate(`document.body.style.zoom === '${[.92,.89,.86][index]}'`), 'sidebar interface scale');
    await delay(150);
    assert.ok(await sidebar.evaluate('Array.from(document.querySelectorAll(".sidebar-quick-actions button")).every(b => b.getBoundingClientRect().top >= 0 && b.getBoundingClientRect().bottom <= innerHeight)'), 'bottom shortcuts remain visible at every interface scale');
  }
  await sidebar.evaluate('document.querySelector(".sidebar-url-pill").click()');
  assert.equal(await sidebar.evaluate('document.activeElement?.matches(".sidebar-url-form input")'), true, 'sidebar pill focuses search');
  await sidebar.evaluate(`const field = document.querySelector('.sidebar-url-form input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(field, ${JSON.stringify(`${origin}/b`)}); field.dispatchEvent(new Event('input', {bubbles:true}));`);
  await sidebar.evaluate('document.querySelector(".sidebar-url-form").requestSubmit()');
  const other = await pageAt('/b');
  console.log('PASS new tab focus and sidebar search');
  await ui.evaluate('chrome.webview.postMessage(JSON.stringify({type:"sidebar.toggle"}))');
  await waitFor(() => sidebar.evaluate('document.body.classList.contains("floating") && getComputedStyle(document.body).backgroundColor === "rgba(0, 0, 0, 0)"'), 'transparent sidebar transition');
  await delay(200);
  await ui.evaluate('chrome.webview.postMessage(JSON.stringify({type:"sidebar.toggle"}))');
  await waitFor(() => sidebar.evaluate('!document.body.classList.contains("floating")'), 'sidebar docked');
  await waitFor(async () => (await other.evaluate('innerWidth')) > initialWidth * 1.08, 'zoom after navigation');
  await command({ type: 'nav.reload', id: second });
  await waitFor(async () => !(await state()).tabs.find(t => t.id === second).loading, 'reload');
  assert.ok((await other.evaluate('innerWidth')) > initialWidth * 1.08, 'reload must preserve zoom');
  await command({ type: 'split.open', id: first });
  await command({type:'split.resize',ratio:0.2});
  await waitFor(() => ui.evaluate('document.querySelectorAll(".split-addresses .address-pill").length === 2'), 'two independent split addresses');
  await sidebar.evaluate(`document.querySelector('.split-proportions').click()`);
  const splitPopup = await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'split layout popup'));
  await waitFor(()=>splitPopup.evaluate('document.querySelectorAll(".split-layout-choice").length===3'),'three visual layout choices');
  await writeFile(resolve(base,'split-layout-menu.png'),Buffer.from((await splitPopup.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
  await splitPopup.evaluate('document.querySelectorAll(".split-layout-choice")[2].click()');
  await waitFor(async()=>Math.abs((await state()).split.ratio-2/3)<0.001,'two-thirds preset');
  await sidebar.evaluate(`document.querySelector('.split-proportions').click()`);
  await waitFor(()=>splitPopup.evaluate('document.querySelectorAll(".split-layout-choice").length===3'),'layout menu reopened');
  await splitPopup.evaluate('document.querySelectorAll(".split-layout-choice")[1].click()');
  await waitFor(async()=>(await state()).split.ratio===0.5,'equal-width preset');
  await ui.call('Emulation.setDeviceMetricsOverride',{width:850,height:52,deviceScaleFactor:1,mobile:false});
  await ui.evaluate(`document.querySelector('[data-tab-id="${first}"] input').focus()`);
  await waitFor(async () => (await state()).activeId === first, 'editing the second address activates its own page');
  assert.ok(await ui.evaluate(`(() => {const pill=document.querySelector('[data-tab-id="${first}"]');return pill.querySelector('input').getBoundingClientRect().right <= pill.querySelector('.address-trail').getBoundingClientRect().left + 1;})()`), 'address text does not overlap action buttons');
  assert.ok(await ui.evaluate('document.querySelector(".topbar-center").getBoundingClientRect().right <= document.querySelector(".topbar-right").getBoundingClientRect().left'), 'split addresses stay inside the toolbar');
  await ui.evaluate('document.activeElement.blur()');
  await ui.call('Emulation.clearDeviceMetricsOverride');
  await command({type:'tab.activate',id:second});
  console.log('PASS split address focus, compact toolbar and text/button separation');
  await command({ type: 'split.close' });
  await command({ type: 'nav.go', id: second, input: `${origin}/missing` });
  await pageAt('/missing');
  // La liste des cibles annonce l'adresse dès le départ de la navigation : on attend le document lui-même.
  await waitFor(() => other.evaluate('!!document.body && document.body.innerText.includes("Server page /missing")'), 'keep the server 404 page');
  await command({ type: 'nav.go', id: second, input: `${origin}/slow` });
  await command({ type: 'nav.go', id: second, input: `${origin}/b` });
  await pageAt('/b');
  assert.equal(await other.evaluate('!!document.querySelector("[data-nav-error]")'), false, 'replaced navigation must not show an error');
  await command({ type: 'nav.go', id: second, input: `${origin}/download` });
  await waitFor(async () => { try { return (await readFile(resolve(base, 'downloads/sol-check.txt'), 'utf8')).includes('regression'); } catch { return false; } }, 'download');
  assert.equal(await other.evaluate('!!document.querySelector("[data-nav-error]")'), false, 'downloads must not become error pages');
  console.log('PASS navigation replacement, HTTP 404 and downloads');
  await command({ type: 'tab.activate', id: second }); // Une capture native nécessite une vue réellement visible.
  await command({ type: 'nav.go', id: second, input: `${origin}/reset` });
  await waitFor(() => other.evaluate('!!document.querySelector("[data-nav-error]")'), 'connection reset error');
  assert.equal(await other.evaluate('document.querySelector("h1").textContent'), 'Connexion interrompue');
  const errorShot = await other.call('Page.captureScreenshot', { format: 'png' });
  await writeFile(resolve(base, 'error.png'), Buffer.from(errorShot.data, 'base64'));
  await command({ type: 'nav.go', id: second, input: `${origin}/b` });
  await pageAt('/b');
  console.log('PASS download does not suppress the following real error');
  await command({type:'settings.set',id:'theme',index:0});
  await other.evaluate(`document.body.style.cssText='background:#121212;min-height:100vh;color:white'; document.body.firstElementChild.style.background='transparent';`);
  await waitFor(() => ui.evaluate('document.documentElement.dataset.theme === "dark" && getComputedStyle(document.documentElement).getPropertyValue("--page-bg").trim() === "rgb(18, 18, 18)"'), 'dark page theme');
  await other.evaluate('document.body.style.backgroundColor = "#fafafa"');
  await waitFor(() => ui.evaluate('document.documentElement.dataset.theme === "light"'), 'site switches to light without navigation');
  await other.evaluate('document.body.style.backgroundColor = "#121212"');
  await waitFor(() => ui.evaluate('document.documentElement.dataset.theme === "dark"'), 'site switches back to dark');
  console.log('PASS default theme follows dark content and live site theme changes');
  await other.evaluate(`document.body.style.backgroundColor='oklch(0.2 0 0)'; const accent=document.createElement('button');accent.textContent='Accent';accent.style.cssText='background:oklch(0.65 0.2 140);color:white;width:160px;height:48px';document.body.append(accent);`);
  await waitFor(() => ui.evaluate('document.documentElement.dataset.theme === "dark" && document.documentElement.style.getPropertyValue("--site-accent").startsWith("rgb(")'), 'modern CSS background and accent');
  let forcedDarkBackground;
  for (const [index, theme] of [[1,'light'],[2,'dark'],[3,'dark'],[0,'dark']]) {
    await command({type:'settings.set',id:'theme',index});
    await waitFor(() => ui.evaluate(`document.documentElement.dataset.theme === ${JSON.stringify(theme)} && document.documentElement.style.getPropertyValue('--site-accent').startsWith('rgb(') && document.documentElement.style.getPropertyValue('--site-chrome-accent').startsWith('rgb(')`), `dynamic accent in theme ${index}`);
    if (index !== 3) {
      await delay(300);
      const background = await ui.evaluate('document.documentElement.style.getPropertyValue("--page-bg")');
      if (index === 2) forcedDarkBackground = background;
      if (index === 0) assert.equal(background, forcedDarkBackground, 'default and dark themes share the dark site background');
      const shot = await sidebar.call('Page.captureScreenshot', {format:'png'});
      await writeFile(resolve(base, `colors-${index === 0 ? 'default' : theme}.png`), Buffer.from(shot.data, 'base64'));
    }
  }
  await other.evaluate('document.body.style.backgroundColor = "color(display-p3 0.035 0.035 0.035)"');
  await waitFor(() => ui.evaluate('document.documentElement.style.getPropertyValue("--page-bg") === "rgb(9, 9, 9)"'), 'display-p3 dark background');
  console.log('PASS modern CSS colors and dynamic accents in all browser themes');
  if (process.env.SOL_CHECK_DEVBENIN === '1') {
    await command({type:'nav.go',id:second,input:'https://www.devbenin.bj'});
    await waitFor(() => other.evaluate('location.hostname.endsWith("devbenin.bj") && document.readyState === "complete"'), 'DevBenin loaded', 40000);
    await delay(1500);
    await other.evaluate('localStorage.setItem("theme","dark");location.reload()');
    await waitFor(() => other.evaluate('document.documentElement?.classList.contains("dark") && document.readyState === "complete"'), 'DevBenin dark mode', 40000);
    await delay(2000);
    const colors = await other.evaluate(`JSON.stringify({html:getComputedStyle(document.documentElement).backgroundColor,body:getComputedStyle(document.body).backgroundColor,classes:document.documentElement.className,stack:document.elementsFromPoint(innerWidth/2,12).map(el=>({tag:el.tagName,class:el.className,bg:getComputedStyle(el).backgroundColor,image:getComputedStyle(el).backgroundImage}))})`);
    console.log('DevBenin colors: ' + colors);
    console.log('Sol theme: ' + await ui.evaluate('JSON.stringify({theme:document.documentElement.dataset.theme,bg:document.documentElement.style.getPropertyValue("--page-bg")})'));
    await waitFor(() => ui.evaluate('document.documentElement.dataset.theme === "dark"'), 'DevBenin drives dark browser theme');
    console.log('PASS DevBenin dark mode drives the default browser theme');
    const screenshot = await sidebar.call('Page.captureScreenshot',{format:'png'});
    await writeFile(resolve(base,'devbenin-sidebar.png'),Buffer.from(screenshot.data,'base64'));
    await command({type:'nav.go',id:second,input:`${origin}/b`});
    await pageAt('/b');
  }
  await other.evaluate(`document.body.style.cursor = 'none'; window.__pointer = null; document.addEventListener('pointermove', e => window.__pointer = {x:e.clientX,y:e.clientY});`);
  await other.call('Input.dispatchMouseEvent', {type:'mouseMoved',x:100,y:100});
  await waitFor(() => other.evaluate('window.__pointer !== null'), 'custom pointer movement at zoom');
  assert.equal(await other.evaluate('getComputedStyle(document.body).cursor'), 'none', 'site controls its own cursor');
  await other.evaluate(`document.body.style.cursor = 'url("data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 width=%2224%22 height=%2224%22%3E%3Ccircle cx=%2212%22 cy=%2212%22 r=%228%22 fill=%22red%22/%3E%3C/svg%3E") 12 12, auto';`);
  assert.match(await other.evaluate('getComputedStyle(document.body).cursor'), /url\(/, 'CSS image cursor preserved');
  console.log('PASS site cursor styles and pointer events at zoom');
  await other.evaluate(`(async () => {
    const canvas = document.createElement('canvas'); canvas.width = 160; canvas.height = 280;
    const ctx = canvas.getContext('2d'); function draw(){ctx.fillStyle='green';ctx.fillRect(0,0,160,280);window.__videoDraw=requestAnimationFrame(draw)} draw();
    const video = document.createElement('video'); video.muted=true; video.style.cssText='width:160px;height:280px';
    video.srcObject=canvas.captureStream(20); document.body.append(video); await video.play();
  })()`);
  await command({ type: 'tab.activate', id: first });
  await waitFor(async () => (await readFile(resolve(profile, 'debug.log'), 'utf8')).includes(`pip.enter ${second}`), 'portrait video PiP');
  await command({ type: 'tab.activate', id: second });
  await waitFor(async () => (await readFile(resolve(profile, 'debug.log'), 'utf8')).includes(`pip.exit ${second}`), 'PiP returns to tab');
  await other.evaluate('document.querySelector("video").pause(); cancelAnimationFrame(window.__videoDraw)');
  const pipEntries = (await readFile(resolve(profile, 'debug.log'), 'utf8')).split(`pip.enter ${second}`).length;
  await command({ type: 'tab.activate', id: first });
  await delay(500);
  assert.equal((await readFile(resolve(profile, 'debug.log'), 'utf8')).split(`pip.enter ${second}`).length, pipEntries, 'paused video must not reopen stale PiP');
  console.log('PASS portrait PiP, return to tab and paused video');
  await command({ type: 'network.toggle' });
  const panelTarget = await waitFor(async () => (await targets()).find(t => t.url.endsWith('#panel')), 'network panel');
  const panel = await connect(panelTarget);
  await page.evaluate(`fetch(${JSON.stringify(`${origin}/before-sleep`)})`);
  await waitFor(() => panel.evaluate('document.body.innerText.includes("before-sleep")'), 'network events before discard');
  await command({ type: 'tab.activate', id: second });
  await waitFor(async () => (await state()).tabs.find(t => t.id === first)?.asleep, 'inactive tab discarded', 25000);
  await command({ type: 'tab.activate', id: first });
  const awake = await pageAt('/a');
  await waitFor(async () => (await awake.evaluate('innerWidth')) > initialWidth * 1.08, 'waking a discarded tab preserves zoom after the engine restores it');
  await awake.evaluate(`fetch(${JSON.stringify(`${origin}/after-sleep`)})`);
  await waitFor(() => panel.evaluate('document.body.innerText.includes("after-sleep")'), 'network events after discard');
  await command({ type: 'network.toggle' });
  console.log('PASS zoom survives memory discard and wake');
  console.log('PASS network inspector reconnects after memory discard');
  await waitFor(async () => {
    const saved = JSON.parse(await readFile(resolve(profile, 'session.json'), 'utf8').catch(() => '{}'));
    return saved.Urls?.[saved.ActiveIndex] === `${origin}/a`;
  }, 'session autosaved before closing');
  console.log('PASS session autosaved while the browser is running');
  await stop();
  const saved = JSON.parse(await readFile(resolve(profile, 'session.json'), 'utf8'));
  const expectedUrl = saved.Urls[saved.ActiveIndex];
  await launch();
  await waitFor(async () => { const s = await state(); return s.tabs.find(t => t.id === s.activeId)?.url === expectedUrl; }, 'last active tab restored');
  assert.equal((await state()).tabs.filter(t => !t.asleep).length, 1, 'restart loads only the last active tab');
  await command({ type:'tab.close', id:(await state()).activeId });
  await waitFor(async () => { const s = await state(); return s.tabs.find(t => t.id === s.activeId)?.url === `${origin}/a`; }, 'new tab after closing last loaded tab');
  assert.equal((await state()).tabs.filter(t => t.asleep).length, 1, 'closing does not wake the remaining inactive tab');
  console.log('PASS restore and close never wake inactive tabs');
  await waitFor(async () => (await state()).tabs.every(t => t.zoom === 90), 'zoom after restart');
  await command({ type: 'zoom.reset' });
  await waitFor(async () => (await state()).tabs.every(t => t.zoom === 100), 'reset all pages');
  await command({ type: 'settings.open', x: 900, y: 58 });
  const popupTarget = await waitFor(async () => (await targets()).find(t => t.url.endsWith('#popup')), 'settings popup');
  const popup = await connect(popupTarget);
  await waitFor(() => popup.evaluate('document.body.innerText.includes("Conservé pour toutes les pages")'), 'profile zoom label');
  assert.equal(await popup.evaluate('document.querySelectorAll(".settings-nav button").length'),5);
  await popup.evaluate('Array.from(document.querySelectorAll(".settings-nav button")).find(b=>b.textContent === "Apparence").click()');
  assert.ok(await popup.evaluate('Array.from(document.querySelectorAll("[role=radio]")).some(b=>b.textContent === "80 %" && b.getBoundingClientRect().height > 0)'), 'interface size controls visible');
  const cookiePage = await pageAt('/a');
  await command({type:'popup.close'});
  await cookiePage.evaluate('window.__geoDenied = false; navigator.geolocation.getCurrentPosition(() => {}, e => window.__geoDenied = e.code === 1)');
  await waitFor(() => popup.evaluate('document.body.innerText.includes("connaître votre position")'), 'native site permission prompt');
  await command({type:'permission.answer',index:0});
  await waitFor(() => cookiePage.evaluate('window.__geoDenied'), 'geolocation denied');
  const permissions = await waitFor(async () => {
    try { return JSON.parse(await readFile(resolve(profile,'permissions.json'),'utf8')); }
    catch (e) { if (e.code === 'ENOENT') return null; throw e; }
  }, 'permission denial persisted');
  assert.equal(permissions[`${origin} Geolocation`],false);
  console.log('PASS native permission prompt and persisted denial');
  await delay(650); // fermeture sur blur (150 ms), puis protection contre le recliquage (350 ms)
  await command({type:'settings.open',x:900,y:58});
  await waitFor(() => popup.evaluate('!!document.querySelector(".settings-nav")'), 'settings sections');
  await popup.evaluate('Array.from(document.querySelectorAll(".settings-nav button")).find(b => b.textContent === "Sécurité").click()');
  await waitFor(() => popup.evaluate('document.body.innerText.includes("Cookies et données des sites")'), 'cookie settings reopened');
  await cookiePage.call('Network.setCookie', {name:'sol-check',value:'test',url:origin,httpOnly:true,sameSite:'Lax'});
  await cookiePage.evaluate('localStorage.setItem("sol-check", "test")');
  assert.ok((await cookiePage.call('Network.getCookies',{urls:[origin]})).cookies.some(c => c.name === 'sol-check'));
  await command({type:'site-data.clear'});
  await waitFor(() => popup.evaluate('document.body.innerText.includes("Données effacées")'), 'site data cleared');
  assert.equal((await cookiePage.call('Network.getCookies',{urls:[origin]})).cookies.some(c => c.name === 'sol-check'),false);
  assert.equal(await cookiePage.evaluate('localStorage.getItem("sol-check")'),null);
  console.log('PASS profile cookie and site-storage deletion, including HttpOnly cookies');
  const settingsShot = await popup.call('Page.captureScreenshot', { format: 'png' });
  await writeFile(resolve(base, 'settings.png'), Buffer.from(settingsShot.data, 'base64'));
  await command({ type: 'popup.close' });
  console.log('PASS zoom survives restart and resets across all tabs');
  await delay(650);
  const designSidebar = await connect((await targets()).find(t => t.url.endsWith('#sidebar')));
  await waitFor(() => designSidebar.evaluate('document.querySelectorAll(".sidebar-quick-actions button").length === 4 && document.querySelector(".sidebar-brand img").naturalWidth > 0'), 'sidebar branding and quick actions');
  assert.equal(await ui.evaluate('!!document.querySelector("button[aria-label=Téléchargements],button[aria-label=\\"Favoris et historique\\"]")'), false, 'no duplicate toolbar shortcuts');
  await designSidebar.evaluate('document.querySelector("button[aria-label=Historique]").click()');
  await waitFor(() => popup.evaluate('document.querySelector("[role=tab][aria-selected=true]")?.textContent.includes("Historique")'), 'history shortcut selects history directly');
  await command({type:'popup.close'});
  const sidebarShot = await designSidebar.call('Page.captureScreenshot',{format:'png'});
  await writeFile(resolve(base,'sidebar-design.png'),Buffer.from(sidebarShot.data,'base64'));
  await ui.call('Emulation.setDeviceMetricsOverride',{width:850,height:52,deviceScaleFactor:1,mobile:false});
  assert.ok(await ui.evaluate('document.querySelector(".navigation-actions").getBoundingClientRect().right < document.querySelector(".topbar-center").getBoundingClientRect().left && document.querySelector(".topbar-center").getBoundingClientRect().right <= document.querySelector(".topbar-right").getBoundingClientRect().left'), 'toolbar groups do not overlap at 850px');
  await ui.call('Emulation.clearDeviceMetricsOverride');
  console.log('PASS sidebar branding, quick history action and toolbar layout');
  await command({type:'tab.open',url:`${origin}/preview-check`});
  await pageAt('/preview-check');
  const previewTab=(await state()).activeId;
  await command({type:'tab.close',id:previewTab});
  await waitFor(async()=>!(await state()).tabs.some(t=>t.id===previewTab),'preview source tab closed');
  await command({type:'bookmark.add',url:`${origin}/b`,title:'Favorite check'});
  await command({type:'group.create',id:(await state()).activeId,title:'Start group'});
  await command({type:'settings.text',id:'home',input:''});
  await command({type:'tab.open'});
  const startUrl = 'https://start.sol/index.html#start';
  const startTarget = await waitFor(async () => (await targets()).find(t=>t.url===startUrl), 'local start target');
  const startPage = await connect(startTarget);
  await waitFor(()=>startPage.evaluate('document.querySelector(".start-favorites")?.textContent.includes("Favorite check") && document.querySelector(".start-groups")?.textContent.includes("Start group") && document.querySelectorAll(".start-history button").length > 0'), 'real profile data on start page');
  await waitFor(()=>startPage.evaluate('document.querySelector(".start-favorites img")?.naturalWidth > 0 && document.querySelector(".start-preview-image > img")?.naturalWidth > 0'),'real favicon and page preview');
  await waitFor(()=>startPage.evaluate('document.querySelector(".start-favorites img")?.naturalWidth === 192'), 'high resolution declared icon preferred');
  assert.equal(await startPage.evaluate('document.documentElement.dataset.wallpaper'),'false');
  assert.equal(await startPage.evaluate('getComputedStyle(document.body).backgroundImage'),'none');
  await command({type:'settings.set',id:'startWallpaper',index:1});
  await waitFor(()=>startPage.evaluate('document.documentElement.dataset.wallpaper === "true" && getComputedStyle(document.body).backgroundImage.includes("start-background-")'),'optional start wallpaper enabled');
  assert.equal(await startPage.evaluate('getComputedStyle(document.body).backgroundSize'),'cover');
  assert.equal(await startPage.evaluate('document.querySelectorAll(".start-preview-placeholder img").length'),0);
  assert.equal(await startPage.evaluate('document.documentElement.dataset.theme'),await ui.evaluate('document.documentElement.dataset.theme'));
  const startWidth = await startPage.evaluate('innerWidth');
  await command({type:'zoom.out'});
  await delay(200);
  assert.equal(await startPage.evaluate('innerWidth'), startWidth, 'start ignores page zoom');
  const startState = await state();
  assert.equal(startState.tabs.find(t=>t.id===startState.activeId)?.zoom,100);
  await command({type:'settings.set',id:'uiScale',index:2});
  await waitFor(()=>ui.evaluate('document.body.style.zoom === "0.86"'),'80% interface scale');
  assert.ok(await designSidebar.evaluate('document.body.style.zoom === "0.86"'));
  assert.ok(await startPage.evaluate('!document.body.style.zoom || document.body.style.zoom === "1"'), 'start remains at 100%');
  assert.equal(JSON.parse(await readFile(resolve(profile,'settings.json'),'utf8')).UiScale,0.8);
  await command({type:'settings.set',id:'uiScale',index:0});
  await waitFor(()=>ui.evaluate('document.body.style.zoom === "0.92"'),'restore interface scale');
  const tabCount = (await state()).tabs.length;
  await command({type:'split.open'});
  await waitFor(async()=> (await state()).tabs.length === tabCount + 1,'new split start tab');
  await waitFor(async()=> (await targets()).filter(t=>t.url===startUrl).length === 2,'both split panes show start');
  await command({type:'split.close'});
  await waitFor(async()=> (await state()).tabs.length === tabCount,'untouched split start cleaned up');
  console.log('PASS start zoom isolation, interface scaling and new split start page');
  const savedPreviews = await readdir(resolve(profile,'start-previews'));
  assert.ok(savedPreviews.some(file=>file.endsWith('.png')), 'URL previews persist in the profile');
  assert.equal(await ui.evaluate('document.querySelector(".address-pill input").value'), '');
  assert.equal(await startPage.evaluate('document.querySelector(".start-history").textContent.includes("start.sol")'), false);
  const startShot=await startPage.call('Page.captureScreenshot',{format:'png'});
  await writeFile(resolve(base,'start-page.png'),Buffer.from(startShot.data,'base64'));
  await command({type:'settings.set',id:'theme',index:1});
  await waitFor(()=>startPage.evaluate('document.documentElement.dataset.theme === "light"'),'start page follows light browser theme');
  await startPage.call('Emulation.setDeviceMetricsOverride',{width:360,height:650,deviceScaleFactor:1,mobile:false});
  assert.ok(await startPage.evaluate('document.documentElement.scrollWidth <= innerWidth'),'start page fits a narrow split pane');
  assert.equal(await startPage.evaluate('getComputedStyle(document.body).backgroundSize'),'cover');
  await startPage.call('Emulation.clearDeviceMetricsOverride');
  await command({type:'settings.set',id:'startWallpaper',index:0});
  await command({type:'settings.set',id:'theme',index:2});
  await startPage.evaluate('Array.from(document.querySelectorAll(".start-favorites button")).find(b=>b.textContent.includes("Favorite check")).click()');
  const navigated = await connect(await waitFor(async ()=>(await targets()).find(t=>t.id===startTarget.id && t.url===`${origin}/b`),'favorite navigation'));
  await waitFor(()=>navigated.evaluate('document.readyState === "complete" && document.body.textContent.includes("Server page")'),'favorite page loaded');
  await navigated.evaluate('try { window.chrome?.webview?.postMessage(JSON.stringify({type:"start.navigate",input:"https://example.com"})); } catch {}');
  await delay(300);
  assert.equal(await navigated.evaluate('location.href'), `${origin}/b`, 'websites cannot control the start bridge');
  console.log('PASS custom start page, real profile data, favorite navigation and bridge isolation');
  await stop();
  await launch(['--private']);
  await waitFor(() => ui.evaluate('document.documentElement.dataset.private === "true" && document.documentElement.style.getPropertyValue("--tab-accent") === "#8b5cf6"'), 'native private window purple identity');
  const privateSidebar = await connect((await targets()).find(t => t.url.endsWith('#sidebar')));
  await waitFor(() => privateSidebar.evaluate('document.querySelector(".sidebar-profile")?.textContent.includes("Privé")'), 'private profile label');
  const privateShot = await privateSidebar.call('Page.captureScreenshot',{format:'png'});
  await writeFile(resolve(base,'sidebar-private.png'),Buffer.from(privateShot.data,'base64'));
  await command({type:'tab.open'});
  const privateStart = await connect(await waitFor(async ()=>(await targets()).find(t=>t.url==='https://start.sol/index.html#start'),'private start page'));
  await waitFor(()=>privateStart.evaluate('document.documentElement?.dataset.private === "true"'),'private start data');
  assert.equal(await privateStart.evaluate('document.querySelectorAll(".start-history button,.start-frequent button").length'),0);
  console.log('PASS private window purple theme and no visits on private start page');
  await stop();
  }
  }
} catch (error) {
  const manager = (await targets()).find(t=>t.url.endsWith('#popup'));
  if (manager) console.error('MANAGER',await (await connect(manager)).evaluate('JSON.stringify(window.__extensions,(key,value)=>key === "icon" ? undefined : value)'));
  if (ui) console.error(await ui.evaluate('JSON.stringify({ready:document.readyState,listening:window.__checkListening,lastEvent:window.__checkLastMessage?.event,text:document.body?.innerText.slice(0,300)})').catch(() => 'UI unavailable'));
  console.error(await readFile(resolve(profile, 'debug.log'), 'utf8').catch(() => 'No diagnostic log'));
  throw error;
} finally {
  for (const ws of connections) ws.close();
  if (app && app.exitCode === null) app.kill();
  server.close();
}
