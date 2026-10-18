// Fake GitLab cookies on local pages; no real account or authentication tokens are read.
import assert from 'node:assert/strict';
import {readFile,readdir} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const executable=process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe';
const requests=[];
const serve=(req,res)=>{
  requests.push({url:req.url,cookie:req.headers.cookie??''});
  const url=new URL(req.url,'http://fixture');
  if(url.pathname==='/login')res.setHeader('Set-Cookie',[
    `_gitlab_session=${url.searchParams.get('account')}; Path=/; HttpOnly; SameSite=Lax`,
    'other_session=temporary-only; Path=/; HttpOnly; SameSite=Lax',
    'remember_user_token=persistent-fixture; Path=/; HttpOnly; SameSite=Lax; Max-Age=3600',
  ]);
  if(url.pathname==='/logout')res.setHeader('Set-Cookie','_gitlab_session=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0');
  res.writeHead(200,{'Content-Type':'text/html'});res.end('<!doctype html><title>Session fixture</title><p>Local cookie fixture</p>');return true;
};
const host=await sidebarHost(executable,{serve});let ui,page;
const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const state=()=>ui.evaluate('window.__sidebarState');
const activate=async id=>{await command({type:'tab.activate',id});await host.wait(async()=>(await state()).activeId===id,'tab activation');};
const navigate=async path=>{
  await command({type:'nav.go',id:(await state()).activeId,input:host.origin+path});
  page=await host.connect(host.origin+path);
  await host.wait(()=>page.evaluate('document.readyState==="complete"').catch(()=>false),'fixture navigation');
};
const cookies=()=>page.call('Network.getCookies',{urls:[host.origin,'https://secure.fixture/']}).then(r=>r.cookies);
const current=async(name)=> (await cookies()).find(c=>c.name===name&&c.domain===new URL(host.origin).hostname);
async function encryptedSnapshots(){
  const folder=resolve(host.profile,'gitlab-sessions');
  const files=await readdir(folder);assert(files.some(f=>f.endsWith('.bin')),'encrypted GitLab snapshot created before exit');
  for(const file of files){const data=await readFile(resolve(folder,file));assert(!data.includes(Buffer.from('main-fixture'))&&!data.includes(Buffer.from('isolated-fixture'))&&!data.includes(Buffer.from('_gitlab_session')),'cookie names and values never stored in plaintext');}
}
try{
  ui=await host.launch(720);await host.wait(()=>ui.evaluate('window.__sidebarState.tabs.every(t=>!t.loading)'),'initial tabs loaded');await navigate('/login?account=main-fixture');
  await host.wait(async()=>!!await current('_gitlab_session'),'login cookie set');
  assert.equal((await current('_gitlab_session')).session,true,'fixture is a real session cookie');
  await page.call('Network.setCookie',{url:'https://secure.fixture/',name:'_gitlab_session',value:'secure-fixture',secure:true,httpOnly:true,sameSite:'Strict'});
  const mainTab=(await state()).activeId;
  await command({type:'tab.open',url:host.origin+'/isolated'});
  const isolatedTab=await host.wait(async()=>{const id=(await state()).activeId;return id!==mainTab&&id},'new isolated tab');
  await command({type:'session.create',id:isolatedTab,title:'Isolated fixture'});
  const session=await host.wait(async()=>(await state()).tabs.find(t=>t.id===isolatedTab)?.sessionId,'isolated session created');
  await navigate('/login?account=isolated-fixture');
  await activate(mainTab);await navigate('/restored');
  await host.close(ui);ui=null;await encryptedSnapshots();await delay(700);
  const before=requests.length;ui=await host.launch(720);page=await host.connect(host.origin+'/restored');
  await host.wait(()=>page.evaluate('document.readyState==="complete"'),'restored page ready');
  assert(requests.slice(before).find(r=>r.url==='/restored')?.cookie.includes('_gitlab_session=main-fixture'),'GitLab session is present on the first request after a process restart');
  assert(!(await current('other_session')),'unrelated session cookies keep their original lifetime');
  assert.equal((await current('remember_user_token')).session,false,'persistent cookies remain owned by the engine');
  const secure=(await cookies()).find(c=>c.domain==='secure.fixture');
  assert(secure?.session&&secure.secure&&secure.httpOnly&&secure.sameSite==='Strict','Secure, HttpOnly, SameSite and session lifetime preserved');
  await activate(isolatedTab);page=await host.connect(host.origin+'/login?account=isolated-fixture');
  await host.wait(async()=> (await current('_gitlab_session'))?.value==='isolated-fixture','named session restored independently');
  await navigate('/logout');assert(!(await current('_gitlab_session')),'logout removes the current session');
  await navigate('/logged-out');await host.close(ui);ui=null;await delay(700);
  const afterLogout=requests.length;ui=await host.launch(720);page=await host.connect(host.origin+'/logged-out');
  await host.wait(()=>page.evaluate('document.readyState==="complete"'),'logged-out page ready');
  assert(!requests.slice(afterLogout).find(r=>r.url==='/logged-out')?.cookie.includes('_gitlab_session='),'logout is not resurrected on restart');
  await activate(mainTab);page=await host.connect(host.origin+'/restored');
  await host.wait(async()=> (await current('_gitlab_session'))?.value==='main-fixture','other account remains signed in');
  await host.wait(()=>page.evaluate('location.pathname==="/restored"&&document.readyState==="complete"&&innerHeight>160').catch(()=>false),'restored page ready for context menu');
  for(const type of ['mousePressed','mouseReleased'])await page.call('Input.dispatchMouseEvent',{type,x:100,y:150,button:'right',clickCount:1});
  const popup=await host.connect('#popup');
  await host.wait(()=>popup.evaluate('[...document.querySelectorAll(".sol-menu-item")].some(b=>b.textContent.includes("Vider la session"))'),'site-reset context action');
  await popup.evaluate('[...document.querySelectorAll(".sol-menu-item")].find(b=>b.textContent.includes("Vider la session")).dispatchEvent(new MouseEvent("mousedown",{bubbles:true,button:0}))');
  await host.wait(()=>popup.evaluate('[...document.querySelectorAll("button")].some(b=>b.textContent==="Vider et recharger")'),'site-reset confirmation');
  await popup.evaluate('[...document.querySelectorAll("button")].find(b=>b.textContent==="Vider et recharger").click()');
  await host.wait(async()=>!await current('_gitlab_session'),'site-reset clears GitLab cookie');
  await host.close(ui);ui=null;await delay(700);
  ui=await host.launch(720);page=await host.connect(host.origin+'/restored');
  await host.wait(()=>page.evaluate('document.readyState==="complete"'),'reset site restored');
  assert(!(await current('_gitlab_session')),'site reset cannot resurrect GitLab sign-in on restart');
  await host.close(ui);ui=null;
  console.log(JSON.stringify({passed:'GitLab cookie survives actual restart before first request; encrypted storage; named-session isolation; secure flags; logout and site reset; unrelated cookie lifetime',evidence:host.base}));
}finally{if(ui)await Promise.race([host.close(ui).catch(()=>{}),delay(4000)]);await host.dispose();}

const privateHost=await sidebarHost(executable,{serve,privateMode:true});let privateUi;
try{
  privateUi=await privateHost.launch(720);
  const privateId=await privateUi.evaluate('window.__sidebarState.activeId');
  await privateUi.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify({type:'nav.go',id:privateId,input:privateHost.origin+'/login?account=private-fixture'}))})`);
  const privatePage=await privateHost.connect(privateHost.origin+'/login?account=private-fixture');
  await privateHost.wait(()=>privatePage.evaluate('document.readyState==="complete"'),'private fixture loaded');
  await privateHost.close(privateUi);privateUi=null;
  assert.equal((await readdir(resolve(privateHost.profile,'gitlab-sessions')).catch(()=>[])).length,0,'private browsing never writes a login snapshot');
  console.log('Private browsing does not persist GitLab session snapshots');
}finally{if(privateUi)await Promise.race([privateHost.close(privateUi).catch(()=>{}),delay(4000)]);await privateHost.dispose();}
