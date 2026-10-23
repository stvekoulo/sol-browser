import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';
import {extensionFixture} from './extension-check-fixtures.mjs';

const executable=process.argv[2]??'bin/session-startup-fixed-build/Sol.exe';
const host=await sidebarHost(executable,{media:true});
const extension=await extensionFixture(host,{name:'Notification startup regression',popup:true});
extension.manifest.permissions.push('notifications');
extension.manifest.background={service_worker:'permission-worker.js'};
await writeFile(resolve(extension.folder,'permission-worker.js'),`chrome.runtime.onMessage.addListener((message,sender,respond)=>{if(message==='permission-check'){chrome.permissions.contains({permissions:['notifications']},respond);return true;}});`);
await writeFile(resolve(extension.folder,'content.js'),`document.addEventListener('DOMContentLoaded',()=>{document.documentElement.dataset.solExtension=chrome.runtime.id;chrome.runtime.sendMessage('permission-check',allowed=>document.documentElement.dataset.notificationGrant=String(allowed));});`);
await writeFile(resolve(extension.folder,'manifest.json'),JSON.stringify(extension.manifest));
await writeFile(resolve(host.profile,'extension-state.json'),JSON.stringify({[extension.id]:true}));
const permissionFile=resolve(host.profile,'permissions.json');
await writeFile(permissionFile,JSON.stringify({[host.origin+' Microphone']:true,[host.origin+' Camera']:false}));
let ui;
const command=object=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(object))})`);
const mediaState=page=>page.evaluate('Promise.all(["microphone","camera"].map(name=>navigator.permissions.query({name}).then(value=>value.state)))');
const pageReady=async url=>{
 const page=await host.connect(url);await host.wait(()=>page.evaluate('document.readyState==="complete"').catch(()=>false),'page ready');return page;
};
async function extensionStillGranted(page){
 await host.wait(()=>page.evaluate('document.documentElement.dataset.notificationGrant==="true"').catch(()=>false),'Extension notification grant remains present');
}
try{
 ui=await host.launch();let page=await pageReady(host.origin+'/2');
 await host.wait(()=>page.evaluate('!!document.documentElement.dataset.solExtension'),'extension installed');
 assert.deepEqual(await mediaState(page),['granted','denied']);await extensionStillGranted(page);
 await host.close(ui);
 // Revocation while closed must still override a native grant at the next startup.
 await writeFile(permissionFile,JSON.stringify({[host.origin+' Microphone']:false,[host.origin+' Camera']:true}));
 ui=await host.launch();page=await pageReady(host.origin+'/2');
 assert.deepEqual(await mediaState(page),['denied','granted'],'Web allow and deny must still synchronize');
 await extensionStillGranted(page);
 await command({type:'tab.open',url:host.origin+'/new-tab-after-restart'});
 page=await pageReady(host.origin+'/new-tab-after-restart');
 assert.deepEqual(await mediaState(page),['denied','granted'],'New tabs work after restart');
 await command({type:'nav.reload',id:(await ui.evaluate('window.__sidebarState')).activeId});
 await host.wait(()=>page.evaluate('document.readyState==="complete"').catch(()=>false),'reload works');
 await command({type:'permissions.clear'});
 await host.wait(async()=>JSON.stringify(await mediaState(page))==='["prompt","prompt"]','Forget web permissions still reaches native state');
 assert.deepEqual(JSON.parse(await readFile(permissionFile,'utf8')),{});
 await extensionStillGranted(page);
 await host.close(ui);ui=await host.launch();page=await pageReady(host.origin+'/new-tab-after-restart');
 assert.deepEqual(await mediaState(page),['prompt','prompt'],'Revocation survives a second restart');
 await extensionStillGranted(page);
 const log=await readFile(resolve(host.profile,'debug.log'),'utf8');
 assert(!log.includes('session.start.failed'),'No startup failure on any tab');
 await writeFile(resolve(host.base,'session-startup-verification.json'),JSON.stringify({checks:[
  'notification extension survives restart','new tab and reload work','web grant and deny preserved',
  'web revocation applied at startup','clear web permissions succeeds','extension grant preserved when clearing website permissions',
  'second restart remains usable','no session startup errors'],passed:true},null,2));
 console.log(JSON.stringify({passed:true,evidence:host.base}));await host.close(ui);
}finally{await host.dispose();}
