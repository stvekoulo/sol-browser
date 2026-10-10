// Windows integration check: node tools/check-navigation.mjs [path/to/Sol.exe]
// Uses a disposable profile and a local server; never touches the user's session.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdir, writeFile, readFile, readdir, cp, rm } from 'node:fs/promises';
import { resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const exe = resolve(process.argv[2] ?? 'src/Browser.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
const base = resolve(`bin/navigation-check-${Date.now()}`);
const profile = resolve(base, 'profiles/robustness');
const echoRequests = [];
const sessionRequests = [];
const siteRequests = [];
let markdownImageRequests=0;
await mkdir(profile, { recursive: true });
const aiCache = process.argv.find(arg => arg.startsWith('--ai-cache='))?.slice(11);
if (aiCache) await cp(resolve(aiCache), resolve(base, 'ai'), {recursive:true});
await mkdir(resolve(base, 'downloads'), { recursive: true });
await mkdir(resolve(base, 'captures'), { recursive: true });
const server = createServer((req, res) => {
  if(req.url==='/markdown-image.svg'){markdownImageRequests++;res.writeHead(200,{'Content-Type':'image/svg+xml'});return res.end('<svg xmlns="http://www.w3.org/2000/svg" width="160" height="60"><rect width="160" height="60" fill="green"/></svg>');}
  if(process.argv.includes('--site-sessions'))siteRequests.push({host:req.headers.host,url:req.url,cookie:req.headers.cookie});
  if(req.url==='/site-redirect'){res.writeHead(302,{Location:`http://localhost:${server.address().port}/signed-in`});return res.end();}
  if(['/document','/download'].includes(req.url))sessionRequests.push({url:req.url,cookie:req.headers.cookie});
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
await writeFile(resolve(profile, 'settings.json'), JSON.stringify({ Onboarded: true, Theme: 'dark', HomePage: `${origin}/a`, OnlineSuggestions: false, LiveColor: false, ...(process.argv.includes('--start-refresh') ? {Shortcuts:[],StartWallpaper:false} : {}), ...(process.argv.includes('--ai-hidden') ? {SolAiEnabled:true,Shortcuts:[]} : {}) }));
if(process.argv.includes('--site-reset'))await writeFile(resolve(profile,'permissions.json'),JSON.stringify({[origin+' Camera']:true,[`http://localhost:${server.address().port} Camera`]:true}));
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
      catch (e) { if (e.code !== 'EBUSY' && !/Cannot find default execution context|Execution context was destroyed|CDP target unavailable/.test(e.message)) throw e; }
    await delay(100);
  }
  throw new Error(`Timed out: ${description}`);
};
async function connect(target) {
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  try { await once(ws, 'open', {signal:AbortSignal.timeout(5000)}); }
  catch { ws.close(); throw new Error('CDP target unavailable'); }
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
  app = spawn(exe, ['--profile=robustness', process.argv.includes('--small') ? '--bounds=-9000,-9000,400,400' : '--bounds=-9000,-9000,1100,800', ...extra], { windowsHide: true, env: { ...process.env, NAVIGATEUR_DATA_DIR: base, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port} --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows`, NAVIGATEUR_DEBUG: '1', NAVIGATEUR_TEST_DOWNLOAD_DIR: resolve(base, 'downloads'), NAVIGATEUR_CAPTURE_DIR: resolve(base,'captures'), NAVIGATEUR_SAVER_TEST: '1', ...(process.argv.includes('--ai-offline') ? {SOL_AI_OFFLINE_TEST:'1'} : {}) } });
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
  return waitFor(async () => {
    const target=(await targets()).find(t => t.url === `${origin}${path}`);
    if (!target) return;
    const page=await connect(target);
    if (await page.evaluate(`document.readyState === "complete" && location.href === ${JSON.stringify(`${origin}${path}`)}`)) return page;
  }, `page ${path}`);
}
async function stop() {
  if (!app || app.exitCode !== null) return;
  const exited = once(app, 'exit');
  await command({ type: 'window.close' });
  await exited;
}
try {
  await launch(process.argv.includes('--private-check') ? ['--private'] : []);
  if(process.argv.includes('--ai-hidden')) {
    await pageAt('/a');
    assert.equal(JSON.parse(await readFile(resolve(profile,'settings.json'),'utf8')).SolAiEnabled,false,'upgrade disables old AI preference');
    assert.ok((await state()).tabs.every(t=>!t.ai),'AI page actions hidden');
    await command({type:'ai.enable',index:1});await delay(200);
    assert.equal(JSON.parse(await readFile(resolve(profile,'settings.json'),'utf8')).SolAiEnabled,false,'hidden AI cannot reactivate through an old command');
    await command({type:'settings.open',x:900,y:58});
    const popup=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'settings popup'));
    await waitFor(()=>popup.evaluate('!!document.querySelector(".settings-nav")'),'settings ready');
    await popup.evaluate("[...document.querySelectorAll('.settings-nav button')].find(b=>b.textContent.trim()==='Sol').click()");
    assert.equal(await popup.evaluate('document.body.innerText.includes("Sol AI")'),false,'Sol AI absent from Sol settings');
    await stop();console.log('PASS Sol AI hidden, disabled on upgrade and unable to reactivate');process.exit(0);
  } else if(process.argv.includes('--start-refresh')) {
    await pageAt('/a');
    await command({type:'tab.open',url:'https://start.sol/index.html#start'});
    const start=await connect(await waitFor(async()=>(await targets()).find(t=>t.url==='https://start.sol/index.html#start'),'start page'));
    await waitFor(()=>start.evaluate('document.readyState==="complete" && !!document.querySelector(".start-history")'),'start interface ready');
    await start.evaluate("chrome.webview.addEventListener('message',e=>{if(e.data.event==='start')window.__home=e.data});chrome.webview.postMessage(JSON.stringify({type:'start.ready'}))");
    await waitFor(()=>start.evaluate('window.__home?.history.length>0'),'initial history');
    const {DatabaseSync}=await import('node:sqlite');
    const db=new DatabaseSync(resolve(profile,'browser.db'),{readOnly:true});
    assert.equal(db.prepare('SELECT count FROM visits WHERE url=?').get(origin+'/a').count,1,'one record per load');
    const url=`http://localhost:${server.address().port}/visited`;
    await command({type:'tab.open',url});
    const visited=await connect(await waitFor(async()=>(await targets()).find(t=>t.url===url),'visited site'));
    await waitFor(()=>visited.evaluate('document.readyState==="complete"'),'visited loaded');
    await waitFor(()=>start.evaluate(`window.__home.history.some(v=>v.url===${JSON.stringify(url)})`),'history updates without reloading start');
    const id=(await waitFor(async()=>(await state()).tabs.find(t=>t.url===url),'visited tab')).id;
    await command({type:'nav.reload',id});
    await waitFor(()=>db.prepare('SELECT count FROM visits WHERE url=?').get(url)?.count===2,'revisit recorded once');
    await waitFor(()=>start.evaluate(`window.__home.frequent[0].url===${JSON.stringify(url)}`),'frequent ranking updates');
    await waitFor(()=>start.evaluate(`(()=>{const img=[...document.querySelectorAll('.start-history button')].find(b=>b.title===${JSON.stringify(url)})?.querySelector('img');return img?.complete&&img.naturalWidth===192})()`),'original high resolution site favicon');
    await delay(200);
    assert.equal(await start.evaluate('getComputedStyle(document.querySelector(".start-history img")).filter'),'none','colored favicon keeps its original colors');
    await command({type:'tab.activate',id:(await state()).tabs.find(t=>t.url==='https://start.sol/index.html#start').id});
    const shot=await start.call('Page.captureScreenshot',{format:'png'});
    await writeFile(resolve(base,'start-refresh.png'),Buffer.from(shot.data,'base64'));
    db.close();await stop();console.log('PASS live start history/frequent ranking, single visit counting and high resolution site favicons');process.exit(0);
  } else if(process.argv.includes('--ai-warm')) {
    const page=await pageAt('/a'), id=(await state()).activeId;
    const popup=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'AI popup'));
    await popup.call('Emulation.setFocusEmulationEnabled',{enabled:true});
    await popup.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup){window.__aiMessage=e.data;window.__phases??=[];window.__partials??=[];if(e.data.popup==='ai-action'&&e.data.busy){window.__phases.push(e.data.phase);if(e.data.result)window.__partials.push({phase:e.data.phase,text:e.data.result})}}})`);
    const content=()=>popup.evaluate('window.__aiMessage');
    await command({type:'settings.open',x:900,y:60});await command({type:'ai.enable',index:1});
    await waitFor(async()=>{const c=await content();return c?.popup==='settings'&&!c.ai.busy&&c.ai.warm&&c.ai.models.some(m=>m.cached)},'cached AI catalog and prepared model',60000);
    assert.equal((await content()).aiPreferences.keepMinutes,45);
    assert.equal((await content()).aiPreferences.thinking,true);
    await page.evaluate(`chrome.webview.postMessage(JSON.stringify({type:'ai.enable',index:0}))`);await delay(150);
    assert.equal((await content()).ai.enabled,true,'site cannot invoke browser commands');
    for(const minutes of [90,0,45]){await command({type:'settings.set',id:'aiKeepMinutes',index:minutes});await waitFor(async()=>(await content()).aiPreferences.keepMinutes===minutes,'keep duration setting');}
    await page.evaluate(`document.body.innerHTML='<main><h1>Garden</h1><p>The garden opens Saturday. Volunteers plant ten apple trees.</p></main>'`);
    await command({type:'ai.open',id});await waitFor(async()=>(await content())?.popup==='ai-action','AI action');
    await popup.evaluate(`Object.defineProperty(document,'hasFocus',{configurable:true,value:()=>false});window.dispatchEvent(new Event('blur'))`);await delay(350);
    assert.equal((await content()).popup,'ai-action','blur does not dismiss AI');await popup.evaluate('delete document.hasFocus');
    await command({type:'ai.run',query:'summary',title:'fr',input:''});
    await waitFor(async()=>{const c=await content();if(c?.error)throw Error(c.error);return c?.popup==='ai-action'&&!c.busy&&c.result},'Think summary',600000);
    assert.equal((await content()).error,null);assert.match((await content()).result,/samedi/i);assert.equal((await content()).warm,true);
    assert.match((await content()).result,/pommier|pomme/i);assert.doesNotMatch((await content()).result,/terre|okay|let me|the user|<think>/i);
    const partials=await popup.evaluate('window.__partials');assert.ok(partials.length>0,'answer streamed while busy');
    assert.ok(partials.every(p=>p.phase==='generating'&&!/okay|let me|the user|<think>/i.test(p.text)),'thinking stays hidden');
    console.log('PASS native Think final answer; model retained');
    await popup.evaluate(`document.querySelector('.ai-think-toggle').click()`);
    await waitFor(async()=>(await content()).thinking===false,'Think disabled in panel');
    await popup.evaluate('window.__phases=[]');
    await command({type:'ai.run',query:'translate',title:'fr',input:''});
    await waitFor(async()=>{const c=await content();return c?.popup==='ai-action'&&c.busy},'warm generation');
    await writeFile(resolve(base,'ai-thinking.png'),Buffer.from((await popup.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await waitFor(async()=>{const c=await content();if(c?.error)throw Error(c.error);return c?.popup==='ai-action'&&!c.busy&&c.result},'warm translation',600000);
    assert.ok(!(await popup.evaluate('window.__phases')).includes('loading'),'no second model load');
    assert.ok(!(await popup.evaluate('window.__phases')).includes('thinking'),'Think disabled');
    assert.match((await content()).result,/samedi/i);
    console.log('PASS native Think off; second answer without reloading');
    await command({type:'settings.open',x:900,y:60});await waitFor(async()=>(await content()).popup==='settings','settings');
    assert.equal((await content()).ai.warm,true);await popup.evaluate(`[...document.querySelectorAll('.settings-nav button')].find(b=>b.textContent==='Sol').click()`);
    assert.equal((await content()).aiPreferences.thinking,false);
    for(const scale of [0,1,2]){await command({type:'settings.set',id:'uiScale',index:scale});await delay(150);assert.ok(await popup.evaluate('document.documentElement.scrollWidth<=innerWidth+1'));}
    await writeFile(resolve(base,'ai-availability.png'),Buffer.from((await popup.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await command({type:'settings.set',id:'aiKeepMinutes',index:1});
    await waitFor(async()=>!(await content()).ai.warm,'idle model expires after custom minute',75000);
    await command({type:'settings.set',id:'aiKeepMinutes',index:45});
    console.log('PASS custom idle expiry');
    await command({type:'ai.open',id});await waitFor(async()=>(await content()).popup==='ai-action','AI reopened');
    await popup.call('Emulation.setFocusEmulationEnabled',{enabled:false});
    await page.call('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,x:40,y:180});
    await page.call('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,x:40,y:180});
    await delay(350);await command({type:'ai.run',query:'summary',title:'fr',input:''});await delay(200);
    assert.equal((await content()).busy,false,'outside click dismissed the AI action');
    await command({type:'ai.enable',index:0});await stop();console.log('PASS native Think, warm model reuse, 45/custom/session duration, blur retention, outside click and 100/90/80; '+base);process.exit(0);
  } else if(process.argv.includes('--player-window')) {
    const page=await pageAt('/a'),id=(await state()).activeId;
    console.log('WebView2 PiP APIs',await page.evaluate('JSON.stringify({video:document.pictureInPictureEnabled,document:typeof window.documentPictureInPicture})'));
    await page.evaluate(`(async()=>{const video=document.createElement('video');video.muted=true;video.style.cssText='width:640px;height:360px';const canvas=document.createElement('canvas');canvas.width=640;canvas.height=360;const ctx=canvas.getContext('2d');window.__draw=setInterval(()=>{ctx.fillStyle='green';ctx.fillRect(0,0,640,360)},50);video.srcObject=canvas.captureStream(20);document.body.append(video);await video.play()})()`);
    await command({type:'tab.open',url:origin+'/b'});await pageAt('/b');
    const otherId=(await state()).activeId;
    await waitFor(async()=>(await readFile(resolve(profile,'debug.log'),'utf8')).includes(`pip.enter ${id}`),'PiP opens');
    assert.equal(await page.evaluate('document.pictureInPictureElement===document.querySelector("video")'),true,'native engine PiP uses original video');
    const run=async(mode)=>{
      const file=resolve(base,'external.html');await writeFile(file,'<title>External file</title>Opened from Windows');
      const proc=spawn('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',resolve('tools/check-player-window.ps1'),'-SolProcessId',String(app.pid),'-Exe',exe,'-DataDir',base,'-Mode',mode,'-Url',origin+'/external','-File',file],{windowsHide:true,env:{...process.env,WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS:`--remote-debugging-port=${port}`},stdio:['ignore','pipe','pipe']});
      let output='';proc.stdout.on('data',b=>output+=b);proc.stderr.on('data',b=>output+=b);
      const code=(await once(proc,'exit'))[0];assert.equal(code,0,output);console.log(output.trim());
    };
    for(const scale of [0,1,2]) {await command({type:'settings.set',id:'uiScale',index:scale});await delay(300);await run('resize');}
    await run('return');
    await waitFor(async()=>(await state()).activeId===id,'native back button selects source tab');
    await command({type:'tab.activate',id:otherId});
    await waitFor(()=>page.evaluate('!!document.pictureInPictureElement'),'PiP reopened');
    await run('close');
    await waitFor(async()=>(await state()).activeId===id,'native close returns source tab');
    await page.evaluate('document.querySelector("video").pause();clearInterval(__draw)');
    await command({type:'tab.activate',id});await delay(400);
    assert.equal(await page.evaluate('document.pictureInPictureElement'),null,'return exits native PiP');
    await command({type:'tab.activate',id:otherId});await delay(500);
    assert.equal(await page.evaluate('document.pictureInPictureElement'),null,'paused video stays in its tab');
    await command({type:'tab.activate',id});
    await page.evaluate(`document.querySelector('video').remove();const f=document.createElement('iframe');f.id='video-frame';f.allow='picture-in-picture';f.src='${origin}/video-child';f.style.cssText='width:320px;height:360px';document.body.append(f)`);
    await waitFor(()=>page.evaluate(`(()=>{const d=document.querySelector('iframe').contentDocument;return d?.location.pathname==='/video-child' && d.readyState==='complete' && !!d.body})()`),'embedded page');
    await page.evaluate(`(async()=>{const d=document.querySelector('iframe').contentDocument,c=d.createElement('canvas');c.width=160;c.height=280;d.defaultView.__draw=setInterval(()=>c.getContext('2d').fillRect(0,0,160,280),50);const v=d.createElement('video');v.muted=true;v.style.cssText='width:160px;height:280px';v.srcObject=c.captureStream(20);d.body.append(v);await v.play()})()`);
    await command({type:'tab.activate',id:otherId});
    await waitFor(()=>page.evaluate(`!!document.querySelector('iframe').contentDocument.pictureInPictureElement`),'embedded portrait native PiP');
    await command({type:'tab.activate',id});
    await waitFor(()=>page.evaluate(`!document.querySelector('iframe').contentDocument.pictureInPictureElement`),'embedded PiP returned');
    await page.evaluate(`const d=document.querySelector('iframe').contentDocument;d.querySelector('video').pause();clearInterval(d.defaultView.__draw);document.querySelector('iframe').remove()`);
    await run('activation');
    await pageAt('/external');
    assert.ok((await state()).tabs.some(t=>t.url.startsWith('file:///') && t.url.endsWith('/external.html')),'external file reaches existing browser');
    await stop();console.log('PASS native engine PiP, edge resize at 100/90/80, return and external activation; '+base);process.exit(0);
  } else if(process.argv.includes('--video-cursor-native')) {
    const page=await pageAt('/a');
    await page.evaluate(`(async()=>{document.body.innerHTML='<div id="player" style="width:100vw;height:100vh"><video muted style="width:100%;height:100%;cursor:crosshair"></video></div>';const c=document.createElement('canvas');c.width=640;c.height=360;window.__draw=setInterval(()=>c.getContext('2d').fillRect(0,0,640,360),50);document.querySelector('video').srcObject=c.captureStream(20);await document.querySelector('video').play();await document.querySelector('#player').requestFullscreen()})()`);
    await waitFor(()=>page.evaluate('!!document.fullscreenElement'),'real fullscreen player');
    const run=async(mode)=>{
      const proc=spawn('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',resolve('tools/check-player-window.ps1'),'-SolProcessId',String(app.pid),'-Exe',exe,'-DataDir',base,'-Mode',mode],{windowsHide:true,stdio:['ignore','pipe','pipe']});
      let output='';proc.stdout.on('data',b=>output+=b);proc.stderr.on('data',b=>output+=b);
      const code=(await once(proc,'exit'))[0];assert.equal(code,0,output);console.log(output.trim());
    };
    await run('cursor');
    await page.evaluate('document.exitFullscreen()');
    await waitFor(()=>page.evaluate('!document.fullscreenElement'),'normal video');
    for(const scale of [0,1,2]) {await command({type:'settings.set',id:'uiScale',index:scale});await delay(300);await run('cursor');}
    await run('cursor-sidebar');
    await page.evaluate('document.querySelector("video").pause()');await run('cursor-visible');
    await page.evaluate(`document.querySelector('video').play();const b=document.createElement('button');b.id='controls';b.style.cssText='position:absolute;left:0;top:0;width:55vw;height:80vh;cursor:pointer';b.textContent='Player controls';document.body.append(b)`);
    await run('cursor-visible');
    await page.evaluate(`document.querySelector('#controls').remove();window.__clicks=0;document.querySelector('video').addEventListener('click',()=>{if(++__clicks===2)document.querySelector('video').pause()})`);
    await run('cursor-click');assert.equal(await page.evaluate('__clicks'),2,'first click while hidden reaches player');assert.equal(await page.evaluate('document.querySelector("video").paused'),true,'first click pauses playback');
    await page.evaluate('clearInterval(__draw)');
    await stop();console.log('PASS real Windows cursor: fullscreen, normal 100/90/80, sidebar, pause, controls and first click; '+base);process.exit(0);
  } else if(process.argv.includes('--video-cursor')) {
    const page=await pageAt('/a');
    await page.call('Emulation.setFocusEmulationEnabled',{enabled:true});
    await page.evaluate(`(async()=>{document.body.innerHTML='<video muted style="position:absolute;left:20px;top:20px;width:320px;height:180px;cursor:crosshair"></video><button style="position:absolute;left:40px;top:40px">Pause</button>';const canvas=document.createElement('canvas');canvas.width=320;canvas.height=180;const ctx=canvas.getContext('2d');window.__draw=setInterval(()=>{ctx.fillStyle='green';ctx.fillRect(0,0,320,180)},50);window.__video=document.querySelector('video');__video.srcObject=canvas.captureStream(20);await __video.play();})()`);
    const move=(x,y)=>page.call('Input.dispatchMouseEvent',{type:'mouseMoved',x,y});
    const cursor=()=>page.evaluate('getComputedStyle(__video).cursor');
    await page.evaluate(`const policy=document.createElement('meta');policy.httpEquiv='Content-Security-Policy';policy.content="style-src 'none'";document.head.append(policy)`);
    await move(200,100);assert.equal(await cursor(),'crosshair');
    await waitFor(async()=>(await cursor())==='none','playing video hides pointer',6000);
    await move(201,100);assert.equal(await cursor(),'crosshair');
    await waitFor(async()=>(await cursor())==='none','idle hides again',6000);
    await page.evaluate('__video.pause()');assert.equal(await cursor(),'crosshair');
    await page.evaluate('__video.play()');await waitFor(async()=>(await cursor())==='none','resume with stationary pointer',6000);
    await move(202,100);assert.equal(await cursor(),'crosshair');
    await page.evaluate(`const overlay=document.createElement('div');overlay.id='player-layer';overlay.setAttribute('role','button');overlay.style.cssText='position:absolute;left:20px;top:20px;width:320px;height:180px;cursor:pointer';document.body.append(overlay)`);
    await move(203,100);
    await waitFor(async()=>await page.evaluate('getComputedStyle(document.querySelector("#player-layer")).cursor')==='none','streaming player clickable overlay hides',6000);
    await page.evaluate(`const next=document.querySelector('#player-layer').cloneNode();next.style.cursor='pointer';document.querySelector('#player-layer').replaceWith(next)`);
    await waitFor(async()=>await page.evaluate('getComputedStyle(document.querySelector("#player-layer")).cursor')==='none','replacement overlay stays hidden',3000);
    await move(204,100);assert.equal(await page.evaluate('getComputedStyle(document.querySelector("#player-layer")).cursor'),'pointer','site overlay cursor restored');
    await page.evaluate(`document.querySelector('#player-layer').remove()`);
    await move(60,50);await delay(3300);assert.equal(await cursor(),'crosshair','controls stay visible');
    await move(500,300);await delay(3300);assert.equal(await cursor(),'crosshair','outside player stays visible');
    await page.evaluate('clearInterval(__draw)');
    await stop();console.log('PASS native video cursor idle, movement, pause, controls and site custom cursor; '+base);process.exit(0);
  } else if(process.argv.includes('--ai-private')) {
    assert.equal((await state()).privateMode,true);
    const popup=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'private settings'));
    await popup.call('Emulation.setFocusEmulationEnabled',{enabled:true});
    await popup.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup==='settings')window.__privateAi=e.data;})`);
    await command({type:'bookmark.add',url:origin+'/a?secret=1',title:'Private favorite'});
    await command({type:'settings.open',x:900,y:60});
    await command({type:'settings.set',id:'aiAdaptation',index:1});
    const settings=await waitFor(()=>popup.evaluate('window.__privateAi?.aiPreferences?.adaptation?window.__privateAi:null'),'private adaptation state');
    assert.equal(settings.aiPreferences.isPrivate,true);assert.equal(settings.aiPreferences.context,'','private browsing never supplies adaptation context');
    await command({type:'settings.set',id:'aiReset',index:0});
    await stop();console.log('PASS private browsing excluded from Sol AI adaptation');process.exit(0);
  } else if(process.argv.includes('--markdown')) {
    await pageAt('/a');
    const text=`# Titre\n\n## Section\n\n### Sous-section\n\n#### Niveau 4\n\n##### Niveau 5\n\n###### Niveau 6\n\n**Gras**, *italique*, ~~barré~~ et \`code\`.\n\n> Citation\n\n- Élément\n  - Sous-élément\n\n1. Première étape\n2. Seconde étape\n\n- [x] Fait\n- [ ] À faire\n\n| Colonne | Valeur |\n| --- | --- |\n| Sol | Clair |\n\n\`\`\`js\nconst greeting = "Bonjour";\n\`\`\`\n\nFormule $x^2$\n\n$$\n\\frac{1}{2}\n$$\n\n[Page locale](${origin}/a)\n\n![Aperçu](${origin}/markdown-image.svg)\n\nRéférence[^1]\n\n[^1]: Note de bas de page.\n\n---\n\n<b>HTML nettoyé</b><script>window.__markdownInjected=true</script><img src="javascript:alert(1)" onerror="window.__markdownInjected=true">\n\n[Interdit](javascript:alert(1))`;
    await command({type:'notes.draft',id:'',title:'Markdown',input:text});
    await command({type:'notes.open',x:500,y:70});
    const popup=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'markdown note'));
    await popup.call('Emulation.setFocusEmulationEnabled',{enabled:true});
    const preview=()=>popup.evaluate(`Array.from(document.querySelectorAll('button')).find(b=>/^Aperçu$|^Preview$/.test(b.textContent)).click()`);
    await waitFor(()=>popup.evaluate(`!!document.querySelector('.notes-text')`),'markdown editor');await preview();
    await waitFor(()=>popup.evaluate(`!!document.querySelector('.sol-markdown .katex')`),'math rendering');
    const rendered=await popup.evaluate(`(()=>{const m=document.querySelector('.sol-markdown');return {headings:Array.from(m.querySelectorAll('h1,h2,h3,h4,h5,h6')).filter(h=>!h.closest('.footnotes')).length,strong:!!m.querySelector('strong'),em:!!m.querySelector('em'),del:!!m.querySelector('del'),table:!!m.querySelector('table'),quote:!!m.querySelector('blockquote'),code:!!m.querySelector('pre code .hljs-keyword'),checks:m.querySelectorAll('input[disabled]').length,footnote:!!m.querySelector('[data-footnote-ref]'),unsafe:m.querySelectorAll('script,iframe,[onerror],a[href^="javascript:"]').length,injected:!!window.__markdownInjected,images:m.querySelectorAll('img[src]').length};})()`);
    assert.equal(rendered.headings,6);for(const key of ['strong','em','del','table','quote','code','footnote'])assert.equal(rendered[key],true,key);
    assert.equal(rendered.checks,2);assert.equal(rendered.unsafe,0);assert.equal(rendered.injected,false);assert.equal(markdownImageRequests,0);assert.equal(rendered.images,0);
    await popup.evaluate(`Array.from(document.querySelectorAll('.sol-markdown button')).find(b=>/Afficher|Show/.test(b.textContent)).click()`);
    await waitFor(()=>popup.evaluate(`document.querySelector('.sol-markdown img')?.naturalWidth===160`),'explicit image loading');assert.equal(markdownImageRequests,1);
    for(const scale of [0,1,2]){await command({type:'settings.set',id:'uiScale',index:scale});await delay(200);assert.ok(await popup.evaluate(`document.documentElement.scrollWidth<=innerWidth+1`),'markdown fits every scale');}
    await writeFile(resolve(base,'markdown.png'),Buffer.from((await popup.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await command({type:'notes.manage'});
    const notePage=await connect(await waitFor(async()=>(await targets()).find(t=>t.url==='https://start.sol/index.html#notes'),'notes workspace'));
    await waitFor(()=>notePage.evaluate(`!!document.querySelector('.notes-writing')`),'note workspace editor');
    await notePage.evaluate(`Array.from(document.querySelectorAll('button')).find(b=>/^Aperçu$|^Preview$/.test(b.textContent)).click()`);
    await waitFor(()=>notePage.evaluate(`!!document.querySelector('.sol-markdown table')`),'workspace markdown');
    const before=(await state()).tabs.length;
    await notePage.evaluate(`Array.from(document.querySelectorAll('.sol-markdown a')).find(a=>a.textContent==='Page locale').click()`);
    await waitFor(async()=>(await state()).tabs.length===before+1,'safe markdown link opens a tab');
    assert.ok(!(await readFile(resolve(profile,'notes.json'),'utf8').catch(()=>'' )).includes('Markdown'),'preview does not save draft');
    await stop();console.log('PASS native Markdown, headings, nested lists, tasks, tables, code, math, footnotes, safe HTML/links/images, notes and 100/90/80; '+base);process.exit(0);
  } else if(process.argv.includes('--ai-actions')) {
    const id=(await state()).activeId, page=await pageAt('/a');
    const popup=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'AI popup'));
    await popup.call('Emulation.setFocusEmulationEnabled',{enabled:true});
    await popup.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup)window.__aiMessage=e.data;})`);
    const content=()=>popup.evaluate('window.__aiMessage');
    await command({type:'settings.open',x:900,y:60});
    await command({type:'ai.enable',index:1});
    await waitFor(async()=>{const d=await content();return d?.popup==='settings'&&!d.ai.busy&&d.ai.models.some(m=>m.cached)},'cached AI catalog',60000);
    const model=(await content()).ai.models.find(m=>m.cached&&m.name==='Sol AI');assert.ok(model);
    assert.ok((await content()).ai.models.every(m=>m.name==='Sol AI'),'one public AI module');
    await command({type:'bookmark.add',url:origin+'/a?token=private-query',title:'Local gardening fixture'});
    await command({type:'settings.open',x:900,y:60});
    await command({type:'settings.set',id:'aiTone',index:2});
    await command({type:'settings.set',id:'aiLength',index:1});
    await command({type:'settings.set',id:'aiAdaptation',index:1});
    await waitFor(async()=>(await content())?.aiPreferences?.adaptation,'local adaptation enabled');
    assert.match((await content()).aiPreferences.context,/Local gardening fixture/);
    assert.ok(!(await content()).aiPreferences.context.includes('private-query'),'navigation query excluded');
    assert.equal(JSON.parse(await readFile(resolve(profile,'settings.json'),'utf8')).SolAiTone,2,'preferences persisted');
    await command({type:'settings.set',id:'aiReset',index:0});
    await waitFor(async()=>(await content())?.aiPreferences?.context==='','adaptation reset');
    const usable=model.usable;
    await command({type:'ai.select',id:model.id});
    await command({type:'popup.close'});
    await page.evaluate(`document.body.innerHTML='<article><h1>A community garden</h1><p>The community garden opens on Saturday. Volunteers will plant ten apple trees. Everyone is welcome.</p></article><textarea id="draft">hello world</textarea><input id="secret" type="password" value="never-process">';document.documentElement.lang='en';`);
    assert.equal(await page.evaluate(`document.querySelector('#secret').focus();window.__solAI.capture()`),null,'sensitive field excluded');
    const snapshot=await page.evaluate(`(()=>{const f=document.querySelector('#draft');f.focus();f.setSelectionRange(0,5);return window.__solAI.capture()})()`);
    assert.equal(snapshot.text,'hello');assert.equal(snapshot.editable,true);
    assert.equal(await page.evaluate(`document.querySelector('#draft').value='changed';window.__solAI.apply(${JSON.stringify(snapshot.token)},'Bonjour')`),false,'stale text rejected');
    const valid=await page.evaluate(`(()=>{const f=document.querySelector('#draft');f.value='hello world';f.focus();f.setSelectionRange(0,5);return window.__solAI.capture()})()`);
    assert.equal(await page.evaluate(`window.__solAI.apply(${JSON.stringify(valid.token)},'Bonjour')`),true,'native editable replacement');
    assert.equal(await page.evaluate(`document.querySelector('#draft').value`),'Bonjour world','selected range only');
    await command({type:'ai.open',id});
    await waitFor(async()=>(await content())?.popup==='ai-action','summary popup');
    assert.equal((await content()).editable,false);assert.match((await content()).text,/apple trees/);
    for(const scale of [0,1,2]) {await command({type:'settings.set',id:'uiScale',index:scale});await delay(250);assert.ok(await popup.evaluate(`document.documentElement.scrollWidth<=innerWidth+1`),'AI actions fit all scales');}
    await command({type:'ai.run',query:'summary',title:'fr'});
    const response=await waitFor(async()=>{const d=await content();return d?.popup==='ai-action'&&!d.busy&&(d.result||d.error)?d:null},'real local summary',180000);
    if(response.error==='machine-capacity'||!usable&&response.error==='choose-model')console.log('PASS capacity gate (no generation claimed)');
    else {assert.equal(response.error,null);assert.match(response.result,/pommier|pommes/i);assert.match(response.result,/samedi/i);console.log('SUMMARY '+response.result);}
    await writeFile(resolve(base,'ai-actions.png'),Buffer.from((await popup.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await command({type:'popup.close'});
    await command({type:'notes.open',x:500,y:70});
    await command({type:'notes.draft',id:'',title:'Test',input:'A short draft'});
    await command({type:'ai.notes',index:0,width:0,height:7});
    await waitFor(async()=>(await content())?.popup==='ai-action'&&(await content()).note,'note action');
    assert.equal((await content()).text,'A short');assert.equal((await content()).editable,true);
    await command({type:'popup.close'});
    const point=await page.evaluate(`(()=>{const f=document.querySelector('#draft');f.focus();f.setSelectionRange(0,7);const r=f.getBoundingClientRect();return {x:r.x+10,y:r.y+10}})()`);
    for(const type of ['mousePressed','mouseReleased'])await page.call('Input.dispatchMouseEvent',{type,button:'right',clickCount:1,...point});
    await waitFor(async()=>(await content())?.popup==='menu','native AI context menu');
    const menu=(await content()).items;assert.ok(menu.includes('Sol AI…'));
    await command({type:'menu.pick',index:menu.indexOf('Sol AI…')});
    await waitFor(async()=>(await content())?.popup==='ai-action','context text tool');
    assert.equal((await content()).editable,true);assert.equal((await content()).text,'Bonjour');
    if(usable) {
      await command({type:'popup.close'});
      await page.evaluate(`const f=document.querySelector('#draft');f.value='bonjour je voudrai savoir si vous ete disponible demain';f.focus();f.setSelectionRange(0,f.value.length);`);
      for(const type of ['mousePressed','mouseReleased'])await page.call('Input.dispatchMouseEvent',{type,button:'right',clickCount:1,...point});
      await waitFor(async()=>(await content())?.popup==='menu','field context menu');
      await command({type:'menu.pick',index:(await content()).items.indexOf('Sol AI…')});
      await waitFor(async()=>(await content())?.popup==='ai-action','field AI');
      await command({type:'ai.run',query:'improve',title:'fr'});
      const correction=await waitFor(async()=>{const d=await content();return d?.popup==='ai-action'&&!d.busy&&(d.result||d.error)?d:null},'native field correction',180000);
      assert.equal(correction.error,null);assert.match(correction.result,/voudrais[\s\S]*êtes/i);
      assert.equal(await page.evaluate(`document.querySelector('#draft').value`),'bonjour je voudrai savoir si vous ete disponible demain','preview does not apply automatically');
      await command({type:'ai.apply'});
      await waitFor(()=>page.evaluate(`document.querySelector('#draft').value.includes('voudrais')`),'explicit field replacement');
      await command({type:'notes.open',x:500,y:70});
      await command({type:'notes.draft',id:'',title:'Draft',input:'nous avon recu votre demande'});
      await command({type:'ai.notes',index:0,width:0,height:0});
      await waitFor(async()=>(await content())?.popup==='ai-action'&&(await content()).note,'whole note AI');
      await command({type:'ai.run',query:'improve',title:'fr'});
      const note=await waitFor(async()=>{const d=await content();return d?.popup==='ai-action'&&!d.busy&&(d.result||d.error)?d:null},'native note correction',180000);
      assert.equal(note.error,null);assert.match(note.result,/avons reçu/i);
      await command({type:'ai.apply'});
      await waitFor(()=>popup.evaluate(`document.querySelector('.notes-text')?.value.includes('avons reçu')`),'note proposal applied to draft');
      assert.ok(!(await readFile(resolve(profile,'notes.json'),'utf8').catch(()=>'' )).includes('avons reçu'),'AI never saves notes automatically');
    }
    await stop();console.log('PASS native AI action extraction, sensitive exclusion, stale selection, range replacement, note selection, responsive popup; '+base);process.exit(0);
  } else if(process.argv.includes('--ai')) {
    await command({type:'settings.open',x:1000,y:60});
    const popup=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'AI settings'));
    await popup.call('Emulation.setFocusEmulationEnabled',{enabled:true});
    await popup.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup==='settings')window.__checkAi=e.data.ai;})`);
    await command({type:'settings.set',id:'theme',index:2});
    const ai=()=>popup.evaluate('window.__checkAi');
    await waitFor(async()=>(await ai())?.available,'compatible AI helper');
    assert.equal((await ai()).enabled,false,'AI off by default');
    assert.equal(await popup.evaluate(`document.querySelectorAll('.settings-nav button').length`),5,'AI belongs to the existing Sol section');
    await popup.evaluate(`Array.from(document.querySelectorAll('.settings-nav button')).find(b=>b.textContent==='Sol').click()`);
    await waitFor(()=>popup.evaluate(`!!document.querySelector('.ai-settings button[role=switch]')`),'AI section');
    await popup.evaluate(`document.querySelector('.ai-settings button[role=switch]').click()`);
    await waitFor(async()=>(await ai())?.busy,'catalog starts');
    await waitFor(async()=>!(await ai())?.busy,'real model catalog',60000);
    assert.equal((await ai()).error,null,'catalog loaded');
    assert.ok((await ai()).models.every(m=>m.device==='CPU' && m.license),'real compatible metadata');
    const model=(await ai()).models.find(m=>m.name==='Sol AI');
    if(!model) assert.ok((await ai()).machine.availableMb<1536,'insufficient available RAM hides install choices');
    if(model && !model.cached) {
      await popup.evaluate(`Array.from(document.querySelectorAll('.ai-model')).find(e=>e.textContent.includes('Sol AI')).querySelector('button').click()`);
      assert.equal(await popup.evaluate(`Array.from(document.querySelectorAll('.ai-confirm button')).find(b=>b.textContent==='Installer sur cet ordinateur').disabled`),true,'download requires explicit license choice');
      await popup.evaluate(`document.querySelector('.ai-confirm input').click()`);
      assert.equal(await popup.evaluate(`Array.from(document.querySelectorAll('.ai-confirm button')).find(b=>b.textContent==='Installer sur cet ordinateur').disabled`),false,'accepted license enables installation');
    }
    for(const index of [0,1,2]) {
      await command({type:'settings.set',id:'uiScale',index});await delay(350);
      assert.ok(await popup.evaluate(`document.documentElement.scrollWidth<=innerWidth+1`),'AI settings fit every scale');
      await writeFile(resolve(base,`ai-scale-${index}.png`),Buffer.from((await popup.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    }
    await popup.evaluate(`document.querySelector('.ai-settings button[role=switch]').click()`);
    await waitFor(async()=>!(await ai()).enabled,'AI disabled');
    await stop();await launch();
    await command({type:'settings.open',x:1000,y:60});
    const reopened=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'reopened AI settings'));
    await reopened.call('Emulation.setFocusEmulationEnabled',{enabled:true});
    await waitFor(()=>reopened.evaluate(`Array.from(document.querySelectorAll('.settings-nav button')).some(b=>b.textContent==='Sol')`),'Sol section after restart');
    await reopened.evaluate(`Array.from(document.querySelectorAll('.settings-nav button')).find(b=>b.textContent==='Sol').click()`);
    assert.equal(await reopened.evaluate(`document.querySelector('.ai-settings button[role=switch]').getAttribute('aria-checked')`),'false','disabled state persists');
    if(aiCache && model) {
      await reopened.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup==='settings')window.__checkAi=e.data.ai;})`);
      const current=()=>reopened.evaluate('window.__checkAi');
      await reopened.evaluate(`document.querySelector('.ai-settings button[role=switch]').click()`);
      await waitFor(async()=>(await current())?.busy,'catalog refresh starts');
      await waitFor(async()=>!(await current())?.busy && (await current())?.models.some(m=>m.id===model.id&&m.cached),'installed model recognised',60000);
      await command({type:'ai.select',id:model.id});
      if(!(await current()).models.find(m=>m.id===model.id).usable) {
        assert.equal((await current()).selected,'','memory reserve blocks selection');
        console.log('PASS memory reserve prevents loading on a busy machine; inference already measured separately');
        await stop();process.exit(0);
      }
      await waitFor(async()=>(await current()).selected===model.id,'model selected');
      await command({type:'ai.check',id:model.id});
      await waitFor(async()=>(await current()).busy,'inference starts');
      await command({type:'ai.cancel'});
      await waitFor(async()=>!(await current()).busy,'inference cancelled',10000);
      assert.equal((await current()).phase,'cancelled','cancellation releases process');
      await command({type:'ai.check',id:model.id});
      await waitFor(async()=>(await current()).busy,'failure isolation starts');
      const terminate=spawn('powershell.exe',['-NoProfile','-NonInteractive','-Command',`$child=Get-CimInstance Win32_Process -Filter "ParentProcessId=${app.pid} AND Name='Sol.AI.exe'";if(!$child){exit 2};$child|ForEach-Object{Stop-Process -Id $_.ProcessId -Force}`],{windowsHide:true,stdio:'ignore'});
      assert.equal((await once(terminate,'exit'))[0],0,'terminate only this test browser helper');
      await waitFor(async()=>!(await current()).busy && (await current()).error==='module-stopped','helper failure reported',15000);
      assert.ok((await state()).tabs.length>0,'Sol survives helper failure');
      await command({type:'ai.check',id:model.id});
      await waitFor(async()=>!(await current()).busy && (await current()).check,'real CPU inference',120000);
      assert.equal((await current()).error,null,'inference completed');
      assert.match((await current()).check.text,/4|four/i,'fixed check produces a result');
      console.log('Local check: '+JSON.stringify((await current()).check));
      await writeFile(resolve(base,'ai-checked.png'),Buffer.from((await reopened.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
      await command({type:'ai.remove',id:model.id});
      await waitFor(async()=>!(await current()).busy && !(await current()).models.find(m=>m.id===model.id).cached,'model removed',30000);
      assert.equal((await current()).selected,'','removed model cannot stay selected');
    }
    console.log('PASS Sol AI opt-in, real catalog, consent, settings persistence and 100/90/80 UI'+(aiCache?', CPU inference, cancellation and model removal':''));
    console.log('Evidence: '+base);
    await stop();
    process.exit(0);
  } else if(process.argv.includes('--site-reset')) {
    const first=(await state()).activeId;
    const page=await pageAt('/a');
    const popup=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'site menu'));
    await popup.call('Emulation.setFocusEmulationEnabled',{enabled:true});
    await page.call('Network.setCookie',{name:'http-only',value:'secret',url:origin,httpOnly:true});
    await page.evaluate(`document.cookie='account=main;path=/';document.cookie='deep=keep-until-reset;path=/deep';localStorage.account='main';sessionStorage.account='main';
      (async()=>{const db=await new Promise((resolve,reject)=>{const r=indexedDB.open('site-reset',1);r.onupgradeneeded=()=>r.result.createObjectStore('data');r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error)});db.close();await (await caches.open('site-reset')).put('/cached',new Response('cached'));await navigator.serviceWorker.register('/sw.js');})()`);
    const other=`http://localhost:${server.address().port}/b`;
    await command({type:'tab.open',url:other});
    const otherPage=await connect(await waitFor(async()=>(await targets()).find(t=>t.url===other),'other site'));
    await waitFor(()=>otherPage.evaluate(`document.readyState==='complete' && location.href===${JSON.stringify(other)}`),'other site ready');
    await otherPage.evaluate(`document.cookie='account=other;path=/';localStorage.account='other'`);
    await command({type:'tab.open',url:origin+'/isolated'});
    await pageAt('/isolated');
    const isolatedId=await waitFor(async()=>(await state()).tabs.find(t=>t.url===origin+'/isolated')?.id,'isolated tab state');
    const admin=JSON.parse(await readFile(resolve(profile,'tab-sessions.json'),'utf8')).find(s=>s.Name==='Admin').Id;
    const old=(await targets()).map(t=>t.id);await command({type:'session.set',id:isolatedId,query:admin});
    const isolated=await connect(await waitFor(async()=>(await targets()).find(t=>t.url===origin+'/isolated'&&!old.includes(t.id)),'other session'));
    await waitFor(()=>isolated.evaluate(`document.readyState==='complete' && location.href===${JSON.stringify(origin+'/isolated')}`),'other session ready');
    await isolated.evaluate(`document.cookie='account=isolated;path=/';localStorage.account='isolated'`);
    await command({type:'tab.activate',id:first});
    const menu=async()=>{
      await page.call('Emulation.setFocusEmulationEnabled',{enabled:true});
      for(const type of ['mousePressed','mouseReleased'])await page.call('Input.dispatchMouseEvent',{type,button:'right',clickCount:1,x:100,y:150});
      await waitFor(()=>popup.evaluate(`Array.from(document.querySelectorAll('.row')).some(b=>b.textContent.includes('Vider la session'))`),'right-click clear action');
      await popup.evaluate(`Array.from(document.querySelectorAll('.row')).find(b=>b.textContent.includes('Vider la session')).dispatchEvent(new MouseEvent('mousedown',{bubbles:true}))`);
      await waitFor(()=>popup.evaluate(`document.body.textContent.includes('Vider et recharger')`),'site confirmation');
    };
    await menu();await command({type:'popup.close'});
    assert.equal(await page.evaluate('localStorage.account'),'main','cancel preserves data');
    await command({type:'site.reset'});await delay(150);
    assert.equal(await page.evaluate('localStorage.account'),'main','closed confirmation cannot clear data');
    await menu();
    await delay(450);
    await writeFile(resolve(base,'site-reset.png'),Buffer.from((await popup.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    const beforeReload=await page.evaluate('performance.timeOrigin');
    await popup.evaluate(`Array.from(document.querySelectorAll('button')).find(b=>b.textContent==='Vider et recharger').click()`);
    await waitFor(()=>page.evaluate(`performance.timeOrigin>${beforeReload} && document.readyState==='complete' && localStorage.account===undefined && sessionStorage.account===undefined && document.cookie===''`),'current site cleared and reloaded');
    assert.deepEqual(await page.evaluate(`(async()=>({db:(await indexedDB.databases()).length,cache:(await caches.keys()).length,workers:(await navigator.serviceWorker.getRegistrations()).length}))()`),{db:0,cache:0,workers:0});
    assert.equal((await page.call('Network.getCookies',{urls:[origin+'/deep']})).cookies.length,0,'cookies at other paths cleared');
    const permissions=JSON.parse(await readFile(resolve(profile,'permissions.json'),'utf8'));
    assert.equal(permissions[origin+' Camera'],undefined,'site permissions forgotten');
    assert.equal(permissions[`http://localhost:${server.address().port} Camera`],true,'other site permissions kept');
    assert.equal(await otherPage.evaluate('document.cookie'),'account=other','other site cookie preserved');
    assert.equal(await otherPage.evaluate('localStorage.account'),'other','other site storage preserved');
    assert.equal(await isolated.evaluate('document.cookie'),'account=isolated','same site in another session preserved');
    assert.equal(await isolated.evaluate('localStorage.account'),'isolated','isolated storage preserved');
    await command({type:'session.set',id:first,query:admin});await delay(300);
    const before=(await targets()).map(t=>t.id);await command({type:'session.set',id:first,query:''});
    const returned=await connect(await waitFor(async()=>(await targets()).find(t=>t.url===origin+'/a'&&!before.includes(t.id)),'reset session reopened'));
    assert.equal(await returned.evaluate('document.cookie'),'','old session cookies do not return');
    await stop();console.log('PASS site reset confirmation, cancellation, cookies/all paths, local/session storage, IndexedDB, CacheStorage, service workers, session and site isolation');console.log('Evidence: '+base);process.exit(0);
  } else if(process.argv.includes('--site-sessions')) {
    const activeTab=async()=>{const s=await state();return s.tabs.find(t=>t.id===s.activeId)};
    const other=`http://localhost:${server.address().port}`;
    const first=(await state()).activeId;
    const catalog=JSON.parse(await readFile(resolve(profile,'tab-sessions.json'),'utf8'));
    const admin=catalog.find(s=>s.Name==='Admin').Id,user=catalog.find(s=>s.Name==='User').Id;
    const documentAt=async(url,exclude=[])=>{
      return await waitFor(async()=>{
        for(const target of (await targets()).filter(t=>t.url===url&&!exclude.includes(t.id))){
          const page=await connect(target);
          if(await page.evaluate(`location.href===${JSON.stringify(url)}&&document.readyState==='complete'&&!!window.__navBlurHost&&!!document.querySelector('h1')`))return page;
        }
      },'site document '+url);
    };
    const switched=async(id,session,url)=>{
      const old=(await targets()).map(t=>t.id);await command({type:'session.set',id,query:session});
      return documentAt(url,old);
    };
    let page=await documentAt(origin+'/a');
    page=await switched(first,admin,origin+'/a');await page.evaluate(`document.cookie='account=admin;path=/';localStorage.account='admin'`);
    await command({type:'tab.open',url:'https://start.sol/index.html#start'});
    await waitFor(async()=>{const s=await state();return s.tabs.find(t=>t.id===s.activeId)?.url==='https://start.sol/index.html#start'},'new start');
    assert.equal((await activeTab())?.sessionId,'');
    // Fermer l'accueil avant ses captures d'aperçus : ce contrôle porte sur les vues des onglets.
    await command({type:'tab.close',id:(await state()).activeId});
    await command({type:'tab.open',url:other+'/b'});page=await documentAt(other+'/b');let id=(await state()).activeId;
    assert.equal((await state()).tabs.find(t=>t.id===id).sessionId,'','unselected site defaults to current session');
    assert.equal(await page.evaluate('document.cookie'),'','new site does not inherit account');
    page=await switched(id,user,other+'/b');await page.evaluate(`document.cookie='account=user;path=/';localStorage.account='user'`);
    assert.equal(await page.evaluate('document.cookie'),'account=user');
    await command({type:'nav.go',id,input:origin+'/address-return'});page=await documentAt(origin+'/address-return');
    assert.equal((await state()).tabs.find(t=>t.id===id).sessionId,admin,'address navigation restores this site choice');
    assert.equal(await page.evaluate('localStorage.account'),'admin');
    await page.evaluate("sessionStorage.authState='kept'");
    const authUrl=other+'/__/auth/handler?client_id=fixture&redirect_uri='+encodeURIComponent(origin+'/oauth-return');
    await page.evaluate(`location.href=${JSON.stringify(authUrl)}`);
    page=await documentAt(authUrl);
    assert.equal((await activeTab()).sessionId,admin,'authentication keeps initiating profile');
    const callbackUrl=origin+'/address-return?state=fixture&code=fixture';
    await page.evaluate(`location.href=${JSON.stringify(callbackUrl)}`);
    page=await documentAt(callbackUrl);
    assert.equal(await page.evaluate('sessionStorage.authState'),'kept','redirect retains sessionStorage');
    await page.call('Emulation.setFocusEmulationEnabled',{enabled:true});
    await delay(200);
    await page.evaluate(`document.querySelector('main').innerHTML='<a href="${other}/clicked#section">Other site</a>'`);
    const point=await page.evaluate(`(()=>{const r=document.querySelector('a').getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2}})()`);
    await page.call('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,...point});
    // Le clic ferme cette vue pour changer de profil : son canal CDP peut disparaître avant l'accusé de réception.
    await page.call('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,...point}).catch(e=>{if(e.message!=='CDP timeout: Input.dispatchMouseEvent')throw e});
    page=await documentAt(other+'/clicked#section');assert.equal((await state()).tabs.find(t=>t.id===id).sessionId,user,'page link with fragment restores destination session');
    assert.equal(await page.evaluate('localStorage.account'),'user');
    assert.equal(await page.evaluate('document.cookie'),'account=user','session cookies survive switching away and back');
    assert.equal(siteRequests.find(r=>r.host===new URL(other).host&&r.url==='/clicked')?.cookie,'account=user','first link request uses destination session');
    await page.evaluate(`void window.open('${origin}/new-window','_blank')`);
    let child=await documentAt(origin+'/new-window');let childId=(await state()).tabs.find(t=>t.url===origin+'/new-window').id;
    assert.equal((await state()).tabs.find(t=>t.id===childId).sessionId,user,'popup retains initiating session');
    assert.equal(await child.evaluate('!!window.opener'),true,'popup retains window.opener');
    await page.evaluate("window.addEventListener('message',e=>{if(e.data==='auth-complete')window.popupResult=true})");
    await child.evaluate("window.opener.postMessage('auth-complete','*')");
    await waitFor(()=>page.evaluate('window.popupResult===true'),'popup communicates with opener');
    assert.equal(await child.evaluate('document.cookie'),'','popup cannot inherit another profile cookies');
    await child.evaluate('window.close()');
    await waitFor(async()=>!(await state()).tabs.some(t=>t.id===childId),'script closes its popup');
    await command({type:'tab.activate',id:first});await command({type:'tab.open',url:other+'/remembered'});
    page=await documentAt(other+'/remembered');assert.equal((await activeTab()).sessionId,user,'new tabs use remembered site');
    await command({type:'tab.open',url:origin+'/site-redirect'});
    child=await documentAt(other+'/signed-in');
    childId=(await waitFor(async()=> (await state()).tabs.find(t=>t.url===other+'/signed-in'),'redirect tab')).id;
    assert.equal((await state()).tabs.find(t=>t.id===childId).sessionId,admin,'authentication redirects retain initiating session');
    await command({type:'nav.go',id:childId,input:origin+'/back-to-site'});child=await documentAt(origin+'/back-to-site');
    await child.evaluate(`document.querySelector('main').insertAdjacentHTML('beforeend','<iframe src="${other}/embedded"></iframe>')`);
    await waitFor(()=>siteRequests.some(r=>r.host===new URL(other).host&&r.url==='/embedded'),'embedded frame request');
    await delay(350);assert.equal((await state()).tabs.find(t=>t.id===childId).sessionId,admin,'embedded sites do not switch the parent session');
    await child.evaluate(`document.querySelector('main').innerHTML='<form method="post" action="${other}/form-result"><input name="value" value="test"></form>';document.querySelector('form').requestSubmit()`);
    await documentAt(other+'/form-result');assert.equal((await state()).tabs.find(t=>t.id===childId).sessionId,admin,'form posts retain their initiating session');
    await command({type:'tab.close',id:childId});
    await command({type:'tab.activate',id:first});
    page=await documentAt(origin+'/a');
    await page.evaluate("sessionStorage.pending='preserve';document.querySelector('main').innerHTML='<input value=initial>';document.querySelector('input').value='unsaved'");
    await command({type:'tab.open',url:origin+'/idle'});
    await delay(23000);
    assert.equal((await state()).tabs.find(t=>t.id===first).asleep,false,'stateful tab is retained');
    await command({type:'tab.activate',id:first});
    assert.equal(await page.evaluate('sessionStorage.pending'),'preserve','wake retains storage');
    assert.equal(await page.evaluate("document.querySelector('input').value"),'unsaved','wake retains form');
    await page.evaluate("document.querySelector('input').click();document.querySelector('input').value='interactive'");
    assert.equal(await page.evaluate("document.querySelector('input').value"),'interactive','resumed page responds');
    await delay(2300);await stop();await launch();
    await command({type:'tab.open',url:other+'/restart'});page=await documentAt(other+'/restart');id=(await state()).activeId;
    assert.equal((await state()).tabs.find(t=>t.id===id).sessionId,user,'site choice persists after restart');
    page=await switched(id,'',other+'/restart');
    await command({type:'tab.open',url:other+'/default-again'});await documentAt(other+'/default-again');
    assert.equal((await activeTab()).sessionId,'','explicit current session removes site override');
    const requested=(host,url)=>siteRequests.find(r=>r.host===new URL(host).host&&r.url===url);
    assert.equal(requested(origin,'/address-return').cookie,'account=admin','first destination request sends only its chosen account');
    assert.equal(requested(other,'/clicked').cookie,'account=user','link destination request sends only its chosen account');
    assert.equal(requested(origin,'/new-window').cookie,undefined,'popup does not leak the destination profile cookies');
    const guest=catalog.find(s=>s.Name==='Invité').Id,rapid=(await state()).activeId;
    await command({type:'session.set',id:rapid,query:guest});await delay(450);
    await switched(rapid,'',other+'/default-again');
    await switched(rapid,guest,other+'/default-again');
    assert.equal((await activeTab()).sessionId,guest,'session remains usable after closing during initialization');
    await stop();console.log('PASS per-site session defaults, remembered choices, links/new windows, cookies, redirects, restart and reset');process.exit(0);
  } else if(process.argv.includes('--usability')) {
    const popup=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'settings popup'));
    const settings=async()=>{await command({type:'settings.open',x:900,y:52});await waitFor(()=>popup.evaluate(`!!document.querySelector('.settings-popup')`),'settings shown');await popup.evaluate(`document.querySelector('.settings-nav button').click();document.querySelector('#root').scrollTop=0`);await delay(250)};
    const resize=async(width,height)=>{
      const script=`Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public class SizeCheck{[DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int z,uint f);}';$p=Get-Process -Id ${app.pid};if(-not [SizeCheck]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,-9000,-9000,${width},${height},0x14)){exit 2}`;
      const proc=spawn('powershell.exe',['-NoProfile','-NonInteractive','-Command',script],{windowsHide:true,stdio:'ignore'});
      assert.equal((await once(proc,'exit'))[0],0);await delay(400);
    };
    for(const scale of [0,1,2]){
      await command({type:'settings.set',id:'uiScale',index:scale});await resize(640,440);await settings();
      const before=await popup.evaluate(`document.querySelector('#root').scrollTop`);
      await popup.call('Input.dispatchMouseEvent',{type:'mouseWheel',x:180,y:180,deltaX:0,deltaY:300});await delay(250);
      assert.ok(await popup.evaluate(`document.querySelector('#root').scrollTop>${before}+20`),'settings mouse wheel');
      await popup.evaluate(`document.querySelector('#root').scrollTop=1e6`);
      assert.ok(await popup.evaluate(`(()=>{const r=document.querySelector('#root');return r.scrollHeight-r.clientHeight-r.scrollTop<2})()`),'last settings reachable');
      const short=await popup.evaluate('innerHeight');await resize(1100,950);
      await waitFor(async()=>(await popup.evaluate('innerHeight'))>short+150,'menu regrows after window resize');
      await popup.evaluate(`document.querySelector('#root').scrollTop=0`);
      for(const section of ['Général','Apparence','Sécurité','Données','Sol']){
        await popup.evaluate(`Array.from(document.querySelectorAll('.settings-nav button')).find(b=>b.textContent===${JSON.stringify(section)}).click()`);await delay(120);
        assert.ok(await popup.evaluate(`document.querySelector('.settings-nav').scrollWidth<=document.querySelector('.settings-nav').clientWidth+1`),'settings sections fit');
      }
      await command({type:'popup.close'});
    }
    await resize(400,400);
    assert.ok(await ui.evaluate(`document.documentElement.scrollWidth<=innerWidth+1`),'small toolbar fits');
    const closeBox=await ui.evaluate(`document.querySelector('.light.close').getBoundingClientRect().toJSON()`);
    await ui.call('Input.dispatchMouseEvent',{type:'mouseMoved',x:300,y:10});
    await ui.call('Input.dispatchMouseEvent',{type:'mouseMoved',x:closeBox.x+closeBox.width/2,y:closeBox.y+closeBox.height/2});await delay(650);
    await waitFor(async()=>/ui.tooltip shown/.test(await readFile(resolve(profile,'debug.log'),'utf8')),'native tooltip opened');
    await command({type:'settings.set',id:'fullscreen',index:0});
    await waitFor(async()=>JSON.parse(await readFile(resolve(profile,'settings.json'),'utf8')).FullScreen===false,'fullscreen preference saved');
    await command({type:'window.maximize'});await waitFor(async()=>!(await state()).maximized,'normal restore');
    await command({type:'window.maximize'});await waitFor(async()=>(await state()).maximized,'normal maximize');
    await stop();await launch();await command({type:'window.maximize'});
    await waitFor(async()=>(await state()).maximized,'fullscreen preference used after restart');
    await command({type:'settings.set',id:'fullscreen',index:1});
    await waitFor(async()=>JSON.parse(await readFile(resolve(profile,'settings.json'),'utf8')).FullScreen===true,'fullscreen reenabled');
    await stop();console.log('PASS settings mouse wheel, small/resized menus, native tooltips, 100/90/80%, fullscreen setting and restart');process.exit(0);
  } else if(process.argv.includes('--sessions')) {
    const popup=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#popup')),'session popup'));
    if(process.argv.includes('--private-check'))assert.equal((await state()).privateMode,true);
    const first=(await state()).activeId;
    const catalog=JSON.parse(await readFile(resolve(profile,'tab-sessions.json'),'utf8'));
    const admin=catalog.find(s=>s.Name==='Admin').Id,user=catalog.find(s=>s.Name==='User').Id,guest=catalog.find(s=>s.Name==='Invité').Id;
    const switchSession=async(id,session,suffix)=>{
      const old=(await targets()).map(t=>t.id);await command({type:'session.set',id,query:session});
      const target=await waitFor(async()=>(await targets()).find(t=>t.url===origin+suffix&&!old.includes(t.id)),'new session view');
      const connection=await connect(target);await waitFor(()=>connection.evaluate(`location.href===${JSON.stringify(origin+suffix)}&&document.readyState==='complete'&&!!document.querySelector('h1')`),'session document');return connection;
    };
    let page=await pageAt('/a');await page.evaluate(`document.cookie='account=main;path=/';localStorage.account='main'`);
    assert.ok((await page.call('Network.getCookies',{urls:[origin]})).cookies.some(c=>c.name==='account'&&c.value==='main'),'main cookie accepted before switch');
    page=await switchSession(first,admin,'/a');
    assert.equal(await page.evaluate('document.cookie'),'','admin cookies isolated from main');
    assert.equal(await page.evaluate('localStorage.account'),undefined,'admin storage isolated from main');
    await page.evaluate(`document.cookie='account=admin;path=/';localStorage.account='admin'`);
    await command({type:'tab.open',url:origin+'/admin-child'});await waitFor(async()=>(await state()).tabs.some(t=>t.url===origin+'/admin-child'),'child tab');const child=(await state()).activeId;
    let childPage=await pageAt('/admin-child');assert.equal((await state()).tabs.find(t=>t.id===child).sessionId,admin,'new tab uses this site choice');
    assert.equal(await childPage.evaluate('localStorage.account'),'admin');
    await childPage.evaluate(`void window.open('/popup-child','_blank')`);
    await waitFor(async()=>(await state()).tabs.some(t=>t.url===origin+'/popup-child'),'popup opens in Sol');
    const popupTab=(await state()).tabs.find(t=>t.url===origin+'/popup-child');assert.equal(popupTab.sessionId,admin,'popup session inherited');
    await command({type:'tab.activate',id:child});await command({type:'tab.close',id:popupTab.id});
    await command({type:'tab.open',url:origin+'/user-page'});await waitFor(async()=>(await state()).tabs.some(t=>t.url===origin+'/user-page'),'user tab');const second=(await state()).activeId;
    await pageAt('/user-page');let userPage=await switchSession(second,user,'/user-page');
    await waitFor(()=>userPage.evaluate(`document.readyState==='complete'`),'user loaded');
    assert.equal(await userPage.evaluate('document.cookie'),'','user cookie isolation');assert.equal(await userPage.evaluate('localStorage.account'),undefined);
    await userPage.evaluate(`document.cookie='account=user;path=/';localStorage.account='user'`);
    // The same origin must ask independently in each account.
    for(const [id,connection,session] of [[second,userPage,user],[child,childPage,admin]]) {
      await command({type:'tab.activate',id});await waitFor(async()=>(await state()).activeId===id,'permission tab active');
      await connection.evaluate(`window.__geoDenied=false;navigator.geolocation.getCurrentPosition(()=>{},e=>window.__geoDenied=e.code===1)`);
      await waitFor(()=>popup.evaluate(`document.body.innerText.includes('connaître votre position')`),'session permission prompt');
      await command({type:'permission.answer',index:0});await waitFor(()=>connection.evaluate('window.__geoDenied'),'session permission denial');
      const permissions=await waitFor(async()=>{try{return JSON.parse(await readFile(resolve(profile,'session-permissions',session+'.json'),'utf8'))}catch(e){if(e.code==='ENOENT')return null;throw e}},'session permission saved');
      assert.equal(permissions[`${origin} Geolocation`],false);
    }
    // Compare independent native extension storage, using Sol's existing zoom extension.
    const workers=[];
    for(const target of (await targets()).filter(t=>t.type==='service_worker'&&t.url.includes('joongffhgagcehjmlaeopoemilgmiabe'))) {
      const connection=await connect(target),urls=await connection.evaluate('chrome.tabs.query({}).then(t=>t.map(t=>t.url))');
      workers.push({connection,urls});
    }
    const adminWorker=workers.find(w=>w.urls.includes(origin+'/admin-child'))?.connection,userWorker=workers.find(w=>w.urls.includes(origin+'/user-page'))?.connection;
    assert.ok(adminWorker&&userWorker,'separate extension contexts exist');
    await adminWorker.evaluate(`chrome.storage.local.set({sessionCheck:'admin'})`);
    assert.equal(await userWorker.evaluate(`chrome.storage.local.get('sessionCheck').then(x=>x.sessionCheck)`),undefined,'extension storage isolation');
    await command({type:'device.compare',id:child,query:'iPhone SE|iPad mini'});
    const mirrored=await waitFor(async()=>{const list=(await targets()).filter(t=>t.url===origin+'/admin-child');return list.length>=2&&list},'session mirror');
    for(const target of mirrored){const mirror=await connect(target);await waitFor(()=>mirror.evaluate(`localStorage.account==='admin'`),'mirror inherits account');}
    await command({type:'device.set',id:child,query:''});
    await command({type:'tab.activate',id:child});childPage=await pageAt('/admin-child');assert.equal(await childPage.evaluate('document.cookie'),'account=admin');
    await command({type:'split.open',id:second});await delay(200);
    assert.equal(await userPage.evaluate('localStorage.account'),'user','split sessions remain separate');
    await command({type:'split.close'});
    await childPage.evaluate(`(async()=>{const canvas=document.createElement('canvas');canvas.width=160;canvas.height=280;const ctx=canvas.getContext('2d');function draw(){ctx.fillStyle='green';ctx.fillRect(0,0,160,280);window.__videoDraw=requestAnimationFrame(draw)}draw();const video=document.createElement('video');video.muted=true;video.srcObject=canvas.captureStream(20);document.body.append(video);await video.play()})()`);
    await command({type:'tab.activate',id:second});
    await waitFor(async()=>(await readFile(resolve(profile,'debug.log'),'utf8')).includes(`pip.enter ${child}`),'session PiP');
    assert.equal(await childPage.evaluate('localStorage.account'),'admin','PiP keeps source session');
    await command({type:'tab.activate',id:child});await waitFor(async()=>(await readFile(resolve(profile,'debug.log'),'utf8')).includes(`pip.exit ${child}`),'session PiP returns');
    await childPage.evaluate(`document.querySelector('video').pause();cancelAnimationFrame(window.__videoDraw)`);
    await command({type:'tab.activate',id:second});await command({type:'tab.close',id:child});await command({type:'tab.reopen'});
    await waitFor(async()=>(await state()).tabs.some(t=>t.id!==child&&t.url===origin+'/admin-child'),'tab reopened');
    const reopened=(await state()).tabs.find(t=>t.url===origin+'/admin-child').id;await waitFor(async()=>(await state()).tabs.find(t=>t.id===reopened)?.sessionId===admin,'closed tab session preserved');
    childPage=await pageAt('/admin-child');assert.equal(await childPage.evaluate('document.cookie'),'account=admin');
    await command({type:'nav.go',id:reopened,input:origin+'/document'});
    await waitFor(()=>sessionRequests.some(r=>r.url==='/document'),'PDF request');assert.equal(sessionRequests.find(r=>r.url==='/document').cookie,'account=admin','PDF uses session');
    await command({type:'nav.go',id:reopened,input:origin+'/download'});
    await waitFor(()=>sessionRequests.some(r=>r.url==='/download'),'download request');assert.equal(sessionRequests.find(r=>r.url==='/download').cookie,'account=admin','download uses session');
    await waitFor(async()=>(await readdir(resolve(base,'downloads'))).length>0,'download completes');
    await waitFor(async()=>(await state()).downloads.active===0,'download finished before session switch');
    await command({type:'nav.go',id:reopened,input:origin+'/admin-child'});childPage=await pageAt('/admin-child');
    childPage=await switchSession(reopened,'','/admin-child');
    await waitFor(()=>childPage.evaluate(`document.readyState==='complete'`),'main recreated');assert.equal(await childPage.evaluate('localStorage.account'),'main','main profile storage retained');assert.equal(await childPage.evaluate('document.cookie'),'account=main','change recreates view with main data');
    childPage=await switchSession(reopened,admin,'/admin-child');
    await waitFor(()=>childPage.evaluate(`document.readyState==='complete'`),'admin restored');assert.equal(await childPage.evaluate('localStorage.account'),'admin');
    await command({type:'tab.activate',id:second});await waitFor(async()=>(await state()).tabs.find(t=>t.id===reopened)?.asleep,'session memory discard',25000);
    assert.equal((await state()).tabs.find(t=>t.id===reopened).sessionId,admin,'discard keeps session identity');
    await command({type:'tab.activate',id:reopened});childPage=await pageAt('/admin-child');
    assert.equal(await childPage.evaluate('document.cookie'),'account=admin','wake preserves session cookies');
    await command({type:'tab.activate',id:second});userPage=await pageAt('/user-page');
    await command({type:'settings.open',x:900,y:58});await waitFor(()=>popup.evaluate(`!!document.querySelector('.settings-nav')`),'settings opened for session data clear');
    await command({type:'site-data.clear'});await waitFor(()=>userPage.evaluate(`document.cookie===''`),'session cookies cleared');
    await command({type:'nav.reload',id:second});
    await waitFor(()=>userPage.evaluate(`document.cookie===''&&localStorage.account===undefined`),'active session cleared');
    assert.equal(await childPage.evaluate('localStorage.account'),'admin','other session data preserved');
    await command({type:'popup.close'});
    await command({type:'tab.activate',id:reopened});
    await popup.evaluate(`window.__sessionOpenCount=0;chrome.webview.addEventListener('message',e=>{if(e.data.popup==='sessions')window.__sessionOpenCount++})`);
    await hotkey('session.menu');await waitFor(()=>popup.evaluate(`!!document.querySelector('.sessions-popup')`),'session shortcut');
    const opened=await popup.evaluate('window.__sessionOpenCount');await hotkey('session.menu');await delay(400);
    assert.equal(await popup.evaluate('window.__sessionOpenCount'),opened,'second click closes instead of reopening');
    await hotkey('session.menu');await waitFor(()=>popup.evaluate(`window.__sessionOpenCount===${opened+1}`),'closed session menu can reopen');
    for(const theme of [1,2])for(const scale of [0,1,2]){
      await command({type:'popup.close'});await command({type:'settings.set',id:'theme',index:theme});await command({type:'settings.set',id:'uiScale',index:scale});await hotkey('session.menu');
      await waitFor(()=>popup.evaluate(`!!document.querySelector('.sessions-popup')`),'session UI');
      assert.ok(await popup.evaluate(`document.documentElement.scrollWidth<=innerWidth`),'session menu fits');
    }
    await delay(350);await popup.call('Page.captureScreenshot',{format:'png'}).then(r=>writeFile(resolve(base,'sessions.png'),Buffer.from(r.data,'base64')));
    await command({type:'session.delete',id:admin});assert.ok(JSON.parse(await readFile(resolve(profile,'tab-sessions.json'),'utf8')).some(s=>s.Id===admin),'used session cannot be deleted');
    childPage=await switchSession(reopened,guest,'/admin-child');await childPage.evaluate(`localStorage.account='guest';document.cookie='account=guest;path=/'`);
    childPage=await switchSession(reopened,admin,'/admin-child');assert.equal(await childPage.evaluate('localStorage.account'),'admin');
    await command({type:'session.delete',id:guest});await waitFor(async()=>!JSON.parse(await readFile(resolve(profile,'tab-sessions.json'),'utf8')).some(s=>s.Id===guest),'unused profile deleted');
    await command({type:'popup.close'});await command({type:'session.create',id:reopened,title:'Test account'});
    await waitFor(async()=>(await state()).tabs.find(t=>t.id===reopened).sessionName==='Test account','custom session selected');
    const custom=(await state()).tabs.find(t=>t.id===reopened).sessionId;
    await command({type:'session.rename',id:custom,title:'Compte perso'});await waitFor(async()=>(await state()).tabs.find(t=>t.id===reopened).sessionName==='Compte perso','session renamed');
    await command({type:'notes.manage'});await waitFor(async()=>{const s=await state();return s.tabs.find(t=>t.id===s.activeId)?.url==='https://start.sol/index.html#notes'},'notes opened');
    await waitFor(()=>ui.evaluate(`document.querySelector('input[aria-label="Adresse ou recherche"]')?.value==='sol://notes'`),'notes address alias');
    await ui.evaluate(`document.querySelector('input[aria-label="Adresse ou recherche"]').focus()`);
    assert.equal(await ui.evaluate(`document.querySelector('input[aria-label="Adresse ou recherche"]').value`),'sol://notes','editing notes address preserves alias');
    await ui.evaluate(`document.querySelector('input[aria-label="Adresse ou recherche"]').closest('form').requestSubmit()`);
    const sidebar=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#sidebar')),'notes sidebar'));
    assert.ok(await sidebar.evaluate(`!!document.querySelector('input[value="sol://notes"]')`),'notes sidebar alias');
    await command({type:'tab.activate',id:reopened});
    if(!process.argv.includes('--private-check')){
      await stop();await launch();await waitFor(async()=>(await state()).tabs.some(t=>t.sessionId===custom),'session restored');
      assert.ok((await state()).tabs.some(t=>t.sessionId===admin),'sleeping tabs retain session');
      const engine=resolve(profile,'webview2/EBWebView'),deleted='WV2Profile_sol-'+guest;
      const registered=JSON.parse(await readFile(resolve(engine,'Local State'),'utf8')).profile.info_cache;
      assert.ok(!Object.hasOwn(registered,deleted),'deleted session unregistered by engine');
      const remaining=await readdir(resolve(engine,deleted)).catch(e=>{if(e.code==='ENOENT')return [];throw e});
      // WebView2 peut recréer ses journaux d'extensions après suppression : vérifier les données de navigation.
      assert.ok(!remaining.some(name=>['Network','Cookies','Local Storage','IndexedDB','Web Data','Login Data'].includes(name)),'deleted cookies, storage and passwords removed by engine');
    }
    await stop();console.log('PASS session isolation, inheritance, popups, split, mirror, permissions, extension storage, PiP, reopen, PDF/download, switch, custom names, deletion, scales, notes address and persistence');process.exit(0);
  }
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
    const notesCapture=await connect(await waitFor(async()=>(await targets()).find(t=>t.url.endsWith('#capture')),'capture controls'));
    await waitFor(()=>notesCapture.evaluate('!!document.querySelector(".capture-review img")?.naturalWidth'),'notes capture preview',40000);
    await notesCapture.evaluate('document.querySelector(".capture-bar .capture-action").click()');
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
  if (process.argv.includes('--extensions') || process.argv.includes('--captures')) {
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
    // L'aperçu précède tout enregistrement ; « Enregistrer… » écrit le fichier.
    const saveCapture=async label=>{
      await waitFor(()=>captureUi.evaluate('!!document.querySelector(".capture-review img")?.naturalWidth'),`${label} preview`,40000);
      await captureUi.evaluate('document.querySelector(".capture-bar .capture-action").click()');
      await waitFor(()=>captureUi.evaluate('window.__captureResult?.kind === "done"'),`${label} saved`,40000);
      return readFile(resolve(base,'captures/capture.png'));
    };
    await captureUi.evaluate('window.__captureResult=null;document.querySelector(".capture-action").click()');
    await waitFor(()=>captureUi.evaluate('!!document.querySelector(".capture-review img")?.naturalWidth'),'screenshot preview',40000);
    assert.equal(await readFile(resolve(base,'captures/capture.png')).catch(()=>null),null,'nothing is written before the preview is accepted');
    const capture = await saveCapture('page');
    const shown = await (await pageAt('/a')).evaluate('[visualViewport.width*devicePixelRatio,visualViewport.height*devicePixelRatio]');
    assert.ok(Math.abs(capture.readUInt32BE(16)-shown[0])<=1.5 && Math.abs(capture.readUInt32BE(20)-shown[1])<=1.5,`screenshot has the pixels shown on screen (${capture.readUInt32BE(16)}x${capture.readUInt32BE(20)} for ${shown})`);
    await readFile(resolve(base,'captures/capture.pdf'));
    console.log('PASS screenshot at screen size, previewed before saving, as PNG and PDF');
    await command({type:'capture.menu'});
    await waitFor(()=>captureUi.evaluate('!!document.querySelector(".capture-action")'),'capture options before cancel');
    await captureUi.evaluate('window.__captureResult=null;document.querySelector(".capture-action").click()');
    await waitFor(()=>captureUi.evaluate('!!document.querySelector(".capture-review img")?.naturalWidth'),'preview to cancel',40000);
    await rm(resolve(base,'captures/capture.png'));
    await captureUi.evaluate('document.querySelector(".capture-bar button").click()');
    await waitFor(()=>captureUi.evaluate('window.__captureResult?.kind === "done"'),'preview cancelled');
    assert.equal(await readFile(resolve(base,'captures/capture.png')).catch(()=>null),null,'a cancelled preview saves nothing');
    console.log('PASS cancelled preview writes no file');
    await command({type:'device.set',id,query:'iPhone 18 Pro Max'});
    await waitFor(async()=>!!(await state()).tabs.find(t=>t.id===id).device,'simulated device');
    await delay(2500);
    await command({type:'capture.run',query:'visible',index:0});
    const framed = await saveCapture('simulated device');
    // La page seule ferait 440 points x 3 = 1320 pixels de large ; avec sa coque, la capture a les pixels de la fenêtre.
    assert.ok(framed.readUInt32BE(16) < 1100 && framed.readUInt32BE(20) <= 800,`simulated device captured with its frame (${framed.readUInt32BE(16)}x${framed.readUInt32BE(20)})`);
    await writeFile(resolve(base,'device-capture.png'),framed);
    await command({type:'device.set',id,query:''});
    await delay(1500);
    console.log('PASS simulated device captured with its frame');
    await command({type:'capture.menu'});
    await waitFor(()=>captureUi.evaluate('!!document.querySelector(".capture-action")'),'capture options reopened');
    await captureUi.evaluate('document.querySelector(".capture-browser").click()');
    assert.match(await captureUi.evaluate('document.body.innerText'),/Fenêtre entière|Entire window/);
    await captureUi.evaluate('window.__captureResult=null;document.querySelector(".capture-action").click()');
    const fullBrowser=await saveCapture('whole browser');
    assert.ok(fullBrowser.readUInt32BE(16)>1000 && fullBrowser.readUInt32BE(16)<=1100 && fullBrowser.readUInt32BE(20)>700 && fullBrowser.readUInt32BE(20)<=800,'whole browser capture has the window pixels');
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
    const selectedArea=await saveCapture('whole browser selected area');
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
    if (!process.argv.includes('--extensions')) { await stop(); console.log(`Evidence: ${base}`); process.exit(0); }
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
  if(process.argv.includes('--site-sessions')&&ui){console.error('SITE STATE',JSON.stringify((await state()).tabs.map(({id,url,sessionId,loading,asleep})=>({id,url,sessionId,loading,asleep}))));console.error('SITE REQUESTS',JSON.stringify(siteRequests.filter(r=>r.url==='/clicked')))}
  const manager = (await targets()).find(t=>t.url.endsWith('#popup'));
  if(manager && process.argv.includes('--ai')) console.error('AI STATE',await (await connect(manager)).evaluate('window.__checkAi'));
  if (manager) console.error('MANAGER',await (await connect(manager)).evaluate('JSON.stringify(window.__extensions,(key,value)=>key === "icon" ? undefined : value)'));
  if (ui) console.error(await ui.evaluate('JSON.stringify({ready:document.readyState,listening:window.__checkListening,lastEvent:window.__checkLastMessage?.event,text:document.body?.innerText.slice(0,300)})').catch(() => 'UI unavailable'));
  console.error(await readFile(resolve(profile, 'debug.log'), 'utf8').catch(() => 'No diagnostic log'));
  throw error;
} finally {
  for (const ws of connections) ws.close();
  if (app && app.exitCode === null) app.kill();
  server.close();
}
