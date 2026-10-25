// Synthetic cookies only: reproduce an exit that does not reach Window.Closing.
import assert from 'node:assert/strict';
import {readdir,readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';
const requests=[];
const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{serve(req,res){
  requests.push({path:req.url,cookie:req.headers.cookie??''});
  if(req.url==='/login')res.setHeader('Set-Cookie','_gitlab_session=interrupted-fixture; HttpOnly; SameSite=Lax; Path=/');
  if(req.url==='/rotate')res.setHeader('Set-Cookie','_gitlab_session=rotated-fixture; HttpOnly; SameSite=Lax; Path=/');
  if(req.url==='/logout')res.setHeader('Set-Cookie','_gitlab_session=; HttpOnly; SameSite=Lax; Path=/; Max-Age=0');
  res.writeHead(200,{'Content-Type':'text/html'});res.end('<!doctype html><title>Interrupted session</title><p>Fixture</p>');return true;
}});
let ui;
const send=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const snapshots=async()=>{const folder=resolve(host.profile,'gitlab-sessions');return Promise.all((await readdir(folder).catch(()=>[])).filter(f=>f.endsWith('.bin')).map(f=>readFile(resolve(folder,f))))};
async function navigate(path){const id=await ui.evaluate('window.__sidebarState.activeId');await send({type:'nav.go',id,input:host.origin+path});const page=await host.connect(host.origin+path);await host.wait(()=>page.evaluate(`location.pathname===${JSON.stringify(path)}&&document.readyState==='complete'`).catch(()=>false),'fixture document');return page}
try {
  ui=await host.launch();await navigate('/login');
  await host.wait(async()=>(await snapshots()).length>0,'encrypted session checkpoint while window is open',6000);
  for(const bytes of await snapshots())assert(!bytes.includes(Buffer.from('interrupted-fixture')),'Session snapshot is encrypted');
  const previous=(await snapshots())[0];await navigate('/rotate');
  await host.wait(async()=>!(await snapshots())[0]?.equals(previous),'cookie rotation replaces checkpoint');
  // Persist the restored tab URL, then terminate this test instance without graceful closing.
  await navigate('/restored');await host.close(ui);await delay(700);
  ui=await host.launch();await host.connect(host.origin+'/restored');
  await host.crash();ui=null;await delay(700);
  const before=requests.length;ui=await host.launch();
  await host.wait(()=>requests.slice(before).some(r=>r.path==='/restored'),'request after interruption');
  assert(requests.slice(before).find(r=>r.path==='/restored').cookie.includes('_gitlab_session=rotated-fixture'),'Interrupted close retains the latest rotated login before first request');
  await navigate('/logout');await host.wait(async()=>(await snapshots()).length===0,'logout immediately removes checkpoint',6000);
  await host.crash();ui=null;await delay(700);
  ui=await host.launch();const page=await navigate('/after-logout');
  assert(!(await page.call('Network.getCookies',{urls:[host.origin]})).cookies.some(c=>c.name==='_gitlab_session'),'Interrupted logout does not resurrect session');
  await writeFile(resolve(host.base,'interrupted-session-verification.json'),JSON.stringify({checks:['encrypted checkpoint before exit','rotated cookie replaces previous snapshot','restore before first request after interrupted exit','logout invalidates checkpoint before exit','no logout resurrection after interruption']},null,2));
  await host.close(ui);ui=null;console.log('Interrupted session checks passed: '+host.base);
} finally {await host.dispose()}
