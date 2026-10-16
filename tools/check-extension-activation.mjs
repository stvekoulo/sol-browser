import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {runInNewContext} from 'node:vm';
import {sidebarHost} from './sidebar-check-host.mjs';
import {extensionFixture} from './extension-check-fixtures.mjs';

const source=await readFile('src/Browser.Core/ExtensionShim.cs','utf8');
const script=source.match(/public const string Script = """([\s\S]*?)""";/)[1];
const extensionOrigin='chrome-extension://'+'a'.repeat(32)+'/';
const tabs=[
  {id:1,url:'https://site.test/',lastAccessed:10,active:true},
  {id:2,url:extensionOrigin+'popup.html',lastAccessed:20,active:true},
  {id:3,url:extensionOrigin+'panel.html',lastAccessed:30,active:true},
];
const chrome={runtime:{getManifest:()=>({action:{default_popup:'popup.html'},side_panel:{default_path:'panel.html'}}),
  getURL:path=>extensionOrigin+path,getContexts:async()=>tabs.slice(1).map(t=>({documentUrl:t.url,tabId:t.id}))},
  tabs:{query:async info=>tabs.filter(t=>!info.url||t.url===info.url)}};
runInNewContext(script,{chrome});
assert.deepEqual(Array.from(await chrome.tabs.query({active:true,currentWindow:true}),t=>t.id),[1]);
await new Promise(done=>chrome.tabs.query({active:true},result=>{assert.equal(result[0].id,1);done();}));
assert.equal((await chrome.tabs.query({url:'https://site.test/'})).length,1);
console.log('PASS popup and panel do not replace the active website; promise and callback APIs');

const executable=process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe';
const host=await sidebarHost(executable);
const action=await extensionFixture(host);
const panel=await extensionFixture(host,{name:'Panel fixture',panel:true});
const popupFixture=await extensionFixture(host,{name:'Popup fixture',popup:true});
const rejected=await extensionFixture(host,{name:'Rejected fixture'});
await writeFile(resolve(rejected.folder,'manifest.json'),JSON.stringify({...rejected.manifest,manifest_version:999}));
await writeFile(resolve(host.profile,'extension-state.json'),JSON.stringify({[action.id]:true,[panel.id]:true,[popupFixture.id]:true,[rejected.id]:true}));
try{
  const ui=await host.launch();
  const command=object=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(object))})`);
  await command({type:'extensions.menu',x:600,y:100});
  const popup=await host.connect('#popup');
  await host.wait(()=>popup.evaluate('document.body.textContent.includes("Action fixture")'),'action-only extension appears in toolbar menu');
  await command({type:'extension.show',id:action.id,x:600,y:100});
  await host.wait(()=>popup.evaluate('document.body.textContent.includes("Microsoft Edge")'),'Edge fallback menu');
  const text=await popup.evaluate('document.body.textContent');
  assert.match(text,/Ouvrir cette page dans Microsoft Edge/);
  assert.match(text,/Installer l’extension dans Edge/);
  assert.match(text,/Réglages de l’extension dans Sol/);
  assert(!text.includes('ne sait pas encore faire'));
  console.log('PASS native Sol fallback menu exposes page handoff, installation and separate settings');
  for(const [fixture,isPanel] of [[panel,true],[popupFixture,false]]){
    await command({type:'extension.show',id:fixture.id,x:600,y:100});
    const view=await host.connect(`chrome-extension://${fixture.id}/view.html`);
    await host.wait(()=>view.evaluate('document.body?.dataset.active').catch(()=>false),'extension knows its active website');
    assert((await view.evaluate('document.body.dataset.active')).startsWith(host.origin));
    if(isPanel){const size=await view.evaluate('[innerWidth,innerHeight]');assert(size[0]>=350&&size[1]>=500,'panel has a usable viewport');}
  }
  console.log('PASS native extension popup and panel render and target the website');
  await command({type:'extension.show',id:rejected.id,x:600,y:100});
  await host.wait(()=>popup.evaluate('document.body.textContent.includes("Ouvrir cette page dans Microsoft Edge")'),'rejected extension falls back without blocking navigation');
  console.log('PASS one rejected extension does not block the browser or other extensions');
  console.log('No actual Edge launch or account sign-in performed. Evidence:',host.base);
}finally{await host.dispose();}
