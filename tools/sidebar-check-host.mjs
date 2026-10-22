import { createServer } from 'node:http';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { copyFile, mkdir, rm, writeFile } from 'node:fs/promises';
import { relative, resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

export async function sidebarHost(executable,{serve=null,downloads=false,captures=false,privateMode=false,media=false,performance=false,saverTest=false,visible=false}={}) {
  const profileName=`sidebar-check-${Date.now()}`,base = resolve(`bin/${profileName}`), profile = resolve(base, 'profiles',profileName);
  await mkdir(profile, {recursive: true});
  if(privateMode)await writeFile(resolve(profile,'temporary'),'');
  const server = createServer((req, res) => {
    if(serve?.(req,res))return;
    res.writeHead(200, {'Content-Type': 'text/html'});
    res.end(`<html><head><title>${req.url==='/2'?'Projet Alpha':`Page ${req.url}`}</title></head><body style="background:#edf2e7;font:24px system-ui"><h1>Sol · Sidebar</h1><p>${req.url}</p></body></html>`);
  }).listen(0, '127.0.0.1');
  await once(server, 'listening');
  const origin = `http://127.0.0.1:${server.address().port}`;
  const portServer = createServer().listen(0, '127.0.0.1'); await once(portServer, 'listening');
  const port = portServer.address().port; await new Promise(r => portServer.close(r));
  const icon = 'data:image/svg+xml;base64,' + Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64"><rect x="4" y="4" width="56" height="56" rx="12" fill="#87a878"/></svg>').toString('base64');
  await writeFile(resolve(profile, 'settings.json'), JSON.stringify({Onboarded:true, FullScreen:false, Theme:'light', SidebarWidth:220, HomePage:`${origin}/home`, OnlineSuggestions:false, LiveColor:false, StartWallpaper:false,
    Shortcuts:['A','B','C'].map(name => ({Name:name,Url:`https://${name.toLowerCase()}.invalid/`,Icon:icon}))}));
  await writeFile(resolve(profile, 'session.json'), JSON.stringify({Urls: Array.from({length:12}, (_,i) => `${origin}/${i}`),
    Titles: ['Épinglé A', 'Épinglé B', 'Projet Alpha', 'Documentation', 'Groupe Beta', 'Recherche', ...Array.from({length:6}, (_,i)=>`Onglet ${i+1}`)],
    Icons: Array(12).fill(icon), PinnedCount:2, ActiveIndex:2,
    Groups:[{Name:'Travail',Color:'#8fa8e8',Icon:'',Collapsed:false},{Name:'Lecture',Color:'#e3cf7a',Icon:'📖',Collapsed:false}], GroupOf:[null,null,0,0,1,1]}));
  const env = {...process.env, NAVIGATEUR_DATA_DIR:base, NAVIGATEUR_DEBUG:'1', WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS:`--remote-debugging-port=${port}${performance?'':' --disable-background-timer-throttling --disable-backgrounding-occluded-windows'}`};
  if(saverTest)env.NAVIGATEUR_SAVER_TEST='1';
  if(media)env.WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS+=' --use-fake-device-for-media-stream';
  if(downloads){env.NAVIGATEUR_TEST_DOWNLOAD_DIR=resolve(base,'downloads');await mkdir(env.NAVIGATEUR_TEST_DOWNLOAD_DIR,{recursive:true});}
  if(captures){env.NAVIGATEUR_CAPTURE_DIR=resolve(base,'captures');await mkdir(env.NAVIGATEUR_CAPTURE_DIR,{recursive:true});}
  delete env.WEBVIEW2_USER_DATA_FOLDER;
  let app, failure='';
  const connections = [];
  const wait = async (get, label, timeout=30000) => {
    const end = Date.now()+timeout;
    while (Date.now()<end) { if(app?.exitCode!==null && app?.exitCode!==undefined) throw new Error(`Sol exited: ${app.exitCode} ${failure}`); const value = await get(); if (value) return value; await delay(80); }
    throw new Error(`Timed out: ${label}`);
  };
  const targets = () => fetch(`http://127.0.0.1:${port}/json/list`).then(r=>r.json()).catch(()=>[]);
  const connect = async (hash,targetId=null) => {
    if(hash==='#sidebar')hash='';
    const url=hash.includes('://')?hash:`https://ui.navigateur/index.html${hash}`;
    const target = await wait(async()=> (await targets()).find(t=>t.url === url&&(!targetId||t.id===targetId)), `target ${hash}`);
    const ws = new WebSocket(target.webSocketDebuggerUrl); await once(ws,'open'); connections.push(ws);
    let seq=0; const pending=new Map();
    ws.addEventListener('message', e=> { const m=JSON.parse(e.data); const p=pending.get(m.id); if(p) {pending.delete(m.id);clearTimeout(p.timer);if(m.error)p.reject(new Error(m.error.message));else p.resolve(m.result);} });
    const call = (method,params={})=>new Promise((resolve,reject)=>{const id=++seq;const timer=setTimeout(()=>{pending.delete(id);reject(new Error(`CDP timeout ${method}`));},10000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}));});
    const evaluate = async expression=> {const result=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true,userGesture:true});if(result.exceptionDetails)throw new Error(JSON.stringify(result.exceptionDetails));return result.result.value;};
    return {call,evaluate};
  };
  const launch = async(height=950,width=1000)=> {
    failure='';
    app = spawn(resolve(executable),[`--profile=${profileName}`,`--bounds=${visible?'100,80':'-9000,-9000'},${width},${height}`,...(privateMode?['--private']:[])],{windowsHide:true,env,stdio:['ignore','pipe','pipe']});
    app.stderr.on('data',data=>failure+=data);app.stdout.on('data',data=>failure+=data);
    const sidebar=await connect('#sidebar');
    await wait(()=>sidebar.evaluate('!!document.querySelector(".sidebar-card")').catch(()=>false),'sidebar ready');
    await sidebar.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.tabs)window.__sidebarState=e.data});chrome.webview.postMessage(JSON.stringify({type:'ui.ready'}))`);
    await wait(()=>sidebar.evaluate('window.__sidebarState?.tabs.length'), 'sidebar state');
    return sidebar;
  };
  const close = async sidebar=> {
    if(app && app.exitCode===null) { const exited=once(app,'exit');await sidebar.evaluate(`chrome.webview.postMessage('{"type":"window.close"}')`);await exited; }
    connections.splice(0).forEach(ws=>ws.close());
  };
  const dispose=async()=>{
    if(app&&app.exitCode===null){const exited=once(app,'exit');app.kill();await exited;}
    connections.splice(0).forEach(ws=>ws.close());
    await new Promise(r=>server.close(r));
    assert.match(relative(resolve('bin'),base),/^sidebar-check-\d+$/);
    await copyFile(resolve(profile,'debug.log'),resolve(base,'native-debug.log')).catch(error=>{if(error.code!=='ENOENT')throw error;});
    await rm(resolve(base,'profiles'),{recursive:true,force:true,maxRetries:30,retryDelay:200});
    if(downloads)await rm(resolve(base,'downloads'),{recursive:true,force:true,maxRetries:10,retryDelay:200});
  };
  const crash=async()=>{
    if(app&&app.exitCode===null){const exited=once(app,'exit');app.kill();await exited;}
    connections.splice(0).forEach(ws=>ws.close());
  };
  return {base,profile,origin,launch,close,connect,wait,targets,dispose,crash,get pid(){return app?.pid;}};
}
