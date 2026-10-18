import assert from 'node:assert/strict';
import {readFile,writeFile,unlink} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';
import {extensionFixture} from './extension-check-fixtures.mjs';

const executable=process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe';
const requests=[];
const host=await sidebarHost(executable,{serve:(req,res)=>{
  requests.push({url:req.url,cookie:req.headers.cookie});
  const url=new URL(req.url,'http://fixture');
  if(url.pathname==='/set')res.setHeader('Set-Cookie',`account=${url.searchParams.get('account')}; Path=/; HttpOnly; Max-Age=3600`);
  if(url.pathname==='/redirect'){res.writeHead(302,{Location:'/after-redirect'});res.end();return true;}
  res.writeHead(200,{'Content-Type':'text/html'});
  res.end('<!doctype html><title>Session fixture</title><p>Fixture</p>');return true;
}});
const extension=await extensionFixture(host,{name:'Shared fixture',panel:true});
await writeFile(resolve(host.profile,'extension-state.json'),JSON.stringify({[extension.id]:true}));
let ui;
const command=object=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(object))})`);
const state=()=>ui.evaluate('window.__sidebarState');
const tabState=async id=>(await state()).tabs.find(t=>t.id===id);
const activate=async id=>{await command({type:'tab.activate',id});await host.wait(async()=>(await state()).activeId===id,'tab activated');};
const nav=async path=>{
  const id=(await state()).activeId,url=host.origin+path;
  await command({type:'nav.go',id,input:url});
  const page=await host.connect(url);
  await host.wait(()=>page.evaluate(`location.href===${JSON.stringify(url)}&&document.readyState==="complete"`).catch(()=>false),'page ready');return page;
};
const open=async url=>{
  const old=(await state()).activeId;await command({type:'tab.open',...(url?{url}: {})});
  return host.wait(async()=>{const id=(await state()).activeId;return id!==old&&id;},'new tab');
};
const create=async(title,id)=>{
  const previous=(await tabState(id))?.sessionId;
  await command({type:'session.create',title,id});
  return host.wait(async()=>{const current=(await tabState(id))?.sessionId;return current&&current!==previous&&current;},'new session');
};
const cookie=async page=>(await page.call('Network.getCookies',{urls:[host.origin]})).cookies.find(c=>c.name==='account')?.value;
const extView=async()=>{
  await command({type:'extension.show',id:extension.id,x:600,y:100});
  const view=await host.connect(`chrome-extension://${extension.id}/view.html`);
  await host.wait(()=>view.evaluate('document.body?.dataset.active').catch(()=>false),'extension ready');return view;
};
try{
  ui=await host.launch();
  console.log('Evidence:',host.base);
  const a=(await state()).activeId,sessionA=await create('Account A',a);
  let page=await nav('/set?account=A');assert.equal(await cookie(page),'A');
  const inherited=await open();assert.equal((await tabState(inherited)).sessionId,sessionA,'new tab inherits active session');
  page=await nav('/inherited');assert.equal(await cookie(page),'A','first navigation keeps inherited cookies');
  const b=await open(host.origin+'/new-b');assert.equal((await tabState(b)).sessionId,sessionA);
  const sessionB=await create('Account B',b);page=await nav('/set?account=B');
  if(await cookie(page)!=='B')console.log('B diagnostics',JSON.stringify({state:await state(),requests,cookies:await page.call('Network.getCookies',{urls:[host.origin]})}));
  assert.equal(await cookie(page),'B');
  let view=await extView();await view.evaluate('chrome.storage.local.set({account:"B"})');
  await activate(a);assert.equal((await tabState(a)).sessionId,sessionA,'returning to A keeps A');
  page=await nav('/account-a');assert.equal(await cookie(page),'A');
  view=await extView();assert.equal(await view.evaluate('chrome.storage.local.get("account").then(s=>s.account)'),undefined,'extension storage isolated');
  await view.evaluate('chrome.storage.local.set({account:"A"})');
  await activate(b);view=await extView();assert.equal(await view.evaluate('chrome.storage.local.get("account").then(s=>s.account)'),'B');
  // Un lien ouvrant une fenêtre depuis A doit garder A, même si B est l'onglet actif.
  await page.evaluate(`window.open(${JSON.stringify(host.origin+'/from-a')},'_blank');true`);
  const linked=await host.wait(async()=>{const id=(await state()).activeId;return id!==b&&id!==a&&id;},'window.open inherits its opener');
  assert.equal((await tabState(linked)).sessionId,sessionA);await command({type:'tab.close',id:linked});await activate(b);
  // Dupliquer un onglet en arrière-plan, puis le rouvrir après fermeture.
  await command({type:'tab.menu',id:a,x:400,y:100});const menu=await host.connect('#popup');
  await host.wait(()=>menu.evaluate('document.body.textContent.includes("Dupliquer")'),'duplicate action');
  await menu.evaluate('[...document.querySelectorAll(".sol-menu-item")].find(e=>e.textContent.includes("Dupliquer")).dispatchEvent(new MouseEvent("mousedown",{bubbles:true,button:0}))');
  const duplicate=await host.wait(async()=>{const id=(await state()).activeId;return id!==b&&id!==a&&id;},'duplicated tab');
  assert.equal((await tabState(duplicate)).sessionId,sessionA);await command({type:'tab.close',id:duplicate});await activate(b);
  await command({type:'tab.reopen'});const reopened=await host.wait(async()=>{const id=(await state()).activeId;return id!==b&&id;},'closed tab reopened');
  assert.equal((await tabState(reopened)).sessionId,sessionA);await command({type:'tab.close',id:reopened});await activate(b);
  page=await nav('/history-one');await nav('/history-two');await command({type:'nav.back',id:b});
  await host.wait(()=>page.evaluate('location.pathname==="/history-one"&&document.readyState==="complete"').catch(()=>false),'history back');
  assert.equal((await tabState(b)).sessionId,sessionB);await command({type:'nav.forward',id:b});
  await host.wait(()=>page.evaluate('location.pathname==="/history-two"&&document.readyState==="complete"').catch(()=>false),'history forward');
  await command({type:'nav.go',id:b,input:host.origin+'/redirect'});
  await host.wait(()=>page.evaluate('location.pathname==="/after-redirect"&&document.readyState==="complete"').catch(()=>false),'redirect');
  assert.equal((await tabState(b)).sessionId,sessionB);
  await page.evaluate(`location.href=${JSON.stringify(host.origin.replace('127.0.0.1','localhost')+'/cross-site')}`);
  await host.wait(()=>page.evaluate('location.hostname==="localhost"&&document.readyState==="complete"').catch(()=>false),'cross-site navigation');
  assert.equal((await tabState(b)).sessionId,sessionB);page=await nav('/returned-b');assert.equal(await cookie(page),'B');
  await activate(inherited);await command({type:'session.set',id:inherited,query:''});
  await host.wait(async()=>(await tabState(inherited)).sessionId==='','explicit primary session');
  page=await nav('/primary');assert.equal(await cookie(page),undefined,'primary cookies isolated');
  assert.equal((await tabState(a)).sessionId,sessionA);assert.equal((await tabState(b)).sessionId,sessionB);
  await activate(a);await command({type:'extension.toggle',id:extension.id});
  await host.wait(async()=>JSON.parse(await readFile(resolve(host.profile,'extension-state.json'),'utf8'))[extension.id]===false,'disabled globally');
  const c=await open(host.origin+'/new-c'),sessionC=await create('Account C',c);page=await nav('/account-c');
  assert.equal(await page.evaluate('document.documentElement.dataset.solExtension'),undefined,'disabled extension never runs in a new session');
  await command({type:'extensions.open'});let manager=await host.connect('#popup');
  await host.wait(()=>manager.evaluate('document.body.textContent.includes("Shared fixture")'),'disabled extension available in new session');
  assert(await manager.evaluate('document.body.textContent.includes("Désactivée")'));
  // Simuler la première ouverture de cette version depuis C, où l'extension n'a jamais été installée.
  await host.close(ui);
  for(const name of ['extension-state.json','extension-state.json.bak'])await unlink(resolve(host.profile,name)).catch(e=>{if(e.code!=='ENOENT')throw e;});
  ui=await host.launch();
  await host.wait(async()=>JSON.parse(await readFile(resolve(host.profile,'extension-state.json'),'utf8').catch(()=>'{}'))[extension.id]===false,'legacy extensions migrated from unopened sessions');
  await command({type:'extensions.open'});manager=await host.connect('#popup');
  await host.wait(()=>manager.evaluate('document.body.textContent.includes("Shared fixture")'),'legacy extension visible without reinstall');
  await command({type:'extension.toggle',id:extension.id});view=await extView();
  assert((await view.evaluate('document.body.dataset.active')).includes('/account-c'));
  page=await nav('/reenabled-c');assert.equal(await page.evaluate('document.documentElement.dataset.solExtension'),extension.id);
  const d=await open(host.origin+'/deleted-session'),sessionD=await create('Deleted fixture',d);await nav('/session-d');
  await command({type:'tab.close',id:d});await activate(a);await command({type:'session.delete',id:sessionD});
  await host.wait(async()=>!JSON.parse(await readFile(resolve(host.profile,'tab-sessions.json'),'utf8')).some(s=>s.Id===sessionD),'unused session deleted');
  const beforeReopen=(await state()).tabs.map(t=>t.id);
  await ui.evaluate('window.__beforeReopen=window.__sidebarState');await command({type:'tab.reopen'});
  await host.wait(()=>ui.evaluate('window.__beforeReopen!==window.__sidebarState'),'reopen processed after deletion');
  assert.deepEqual((await state()).tabs.map(t=>t.id),beforeReopen,'deleted session cannot be reopened');
  await activate(a);view=await extView();assert.equal(await view.evaluate('chrome.storage.local.get("account").then(s=>s.account)'),'A');
  await command({type:'extension.remove',id:extension.id});
  await host.wait(async()=>JSON.parse(await readFile(resolve(host.profile,'extension-state.json'),'utf8'))[extension.id]===null,'removed globally');
  const expected=(await state()).tabs.map(t=>[t.url,t.sessionId]);
  await host.close(ui);ui=await host.launch();
  assert.deepEqual((await state()).tabs.map(t=>[t.url,t.sessionId]),expected,'each restored tab keeps its session');
  const restoredB=(await state()).tabs.find(t=>t.sessionId===sessionB).id;
  assert((await state()).tabs.some(t=>t.sessionId===sessionC));
  await activate(restoredB);page=await nav('/after-restart-b');assert.equal(await cookie(page),'B');
  await command({type:'extensions.open'});const restoredManager=await host.connect('#popup');
  await host.wait(()=>restoredManager.evaluate('document.body.textContent.includes("Aucune extension ajoutée")'),'removal survives restart');
  console.log('PASS new tabs, navigation, per-tab switching, cookies, default session and process restart');
  console.log('PASS extension available in A/B/C, isolated storage, global disable/enable/removal, legacy migration and restart');
  console.log('Evidence:',host.base);
}finally{await host.dispose();}
