import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const executable=process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe';
const host=await sidebarHost(executable,{media:true});
const command=(ui,object)=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(object))})`);
const state=page=>page.evaluate('Promise.all(["camera","microphone"].map(name=>navigator.permissions.query({name}).then(p=>p.state)))');
const ready=page=>host.wait(()=>page.evaluate('document.readyState==="complete"').catch(()=>false),'media page ready');
async function request(ui,page,popup,kind,allow=true){
  await popup.evaluate('window.__permissionRequests=[];if(!window.__permissionWatch){window.__permissionWatch=true;chrome.webview.addEventListener("message",e=>{if(e.data?.popup==="permission")window.__permissionRequests.push(e.data)})}');
  await page.evaluate(`window.__mediaOutcome=null;navigator.mediaDevices.getUserMedia(${JSON.stringify(kind==='both'?{audio:true,video:true}:kind==='audio'?{audio:true}:{video:true})}).then(s=>{s.getTracks().forEach(t=>t.stop());window.__mediaOutcome='ok'},e=>window.__mediaOutcome=e.name);void 0`);
  let answered=0;
  await host.wait(async()=>{
    const count=await popup.evaluate('window.__permissionRequests.length');
    if(count>answered){answered++;await command(ui,{type:'permission.answer',index:allow?1:0});await delay(50);}
    return await page.evaluate('window.__mediaOutcome');
  },'media permission resolved');
  assert.equal(await page.evaluate('window.__mediaOutcome'),allow?'ok':'NotAllowedError');
  return answered;
}
try{
  let ui=await host.launch(),page=await host.connect(host.origin+'/2'),popup=await host.connect('#popup');
  await ready(page);
  assert.equal(await request(ui,page,popup,'audio'),1);
  assert.equal(await request(ui,page,popup,'video'),1);
  const saved=JSON.parse(await readFile(resolve(host.profile,'permissions.json'),'utf8'));
  assert.equal(saved[host.origin+' Microphone'],true);assert.equal(saved[host.origin+' Camera'],true);
  await command(ui,{type:'permissions.forget',id:host.origin});
  await host.wait(async()=>JSON.stringify(await state(page))==='["prompt","prompt"]','clear native choices before legacy migration');
  await host.close(ui);
  await writeFile(resolve(host.profile,'permissions.json'),JSON.stringify({...saved,[host.origin+' 999']:true}));
  ui=await host.launch();page=await host.connect(host.origin+'/2');popup=await host.connect('#popup');
  await ready(page);
  assert.deepEqual(await state(page),['granted','granted'],'media permissions must be known to websites immediately after restart');
  assert.equal(await request(ui,page,popup,'both'),0,'no repeated permission prompt after restart');
  await command(ui,{type:'permissions.forget',id:host.origin});
  await host.wait(async()=>JSON.stringify(await state(page))==='["prompt","prompt"]','permission revocation reaches the engine');
  assert.equal(await request(ui,page,popup,'both'),2,'camera and microphone questions are queued without silently rejecting one');
  const primary=(await ui.evaluate('window.__sidebarState')).activeId;
  await command(ui,{type:'tab.open',url:host.origin+'/permission-isolated'});
  await host.wait(async()=>(await ui.evaluate('window.__sidebarState')).activeId!==primary,'second tab');
  const isolated=(await ui.evaluate('window.__sidebarState')).activeId;
  await command(ui,{type:'session.create',title:'Media isolated',id:isolated});
  await host.wait(async()=>(await ui.evaluate('window.__sidebarState')).tabs.find(t=>t.id===isolated)?.sessionId,'named session selected');
  await command(ui,{type:'nav.go',id:isolated,input:host.origin+'/named-media'});
  page=await host.connect(host.origin+'/named-media');await ready(page);
  assert.deepEqual(await state(page),['prompt','prompt'],'primary consent must not leak into another session');
  assert.equal(await request(ui,page,popup,'audio',false),1);
  assert.equal(await request(ui,page,popup,'video'),1);
  await host.close(ui);ui=await host.launch();page=await host.connect(host.origin+'/named-media');await ready(page);
  assert.deepEqual(await state(page),['granted','denied'],'both allow and deny persist in the named session');
  await command(ui,{type:'tab.activate',id:(await ui.evaluate('window.__sidebarState')).tabs.find(t=>t.url===host.origin+'/2').id});
  page=await host.connect(host.origin+'/2');await ready(page);
  assert.deepEqual(await state(page),['granted','granted'],'named session decisions must not change primary consent');
  console.log(JSON.stringify({passed:'camera/microphone allow and deny survive restart; fake devices; native revocation; queued simultaneous requests; named-session isolation',evidence:host.base}));
}finally{await host.dispose();}
