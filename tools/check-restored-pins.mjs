import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const requests=[];
const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{
  serve(req,res){
    if(!['/0','/1','/3'].includes(req.url))return false;
    requests.push(req.url);
    setTimeout(()=>{res.writeHead(200,{'Content-Type':'text/html'});res.end('<title>Restored page</title><p>Ready</p>');},500);
    return true;
  }
});
const settingsFile=resolve(host.profile,'settings.json'),settings=JSON.parse(await readFile(settingsFile,'utf8'));
settings.Shortcuts[0].Url=host.origin+'/1';await writeFile(settingsFile,JSON.stringify(settings));
const sessionFile=resolve(host.profile,'session.json'),session=JSON.parse(await readFile(sessionFile,'utf8'));
session.ShortcutOf=[null,0];await writeFile(sessionFile,JSON.stringify(session));
try{
 for(let run=0;run<2;run++){
   requests.length=0;
   const ui=await host.launch();
   await delay(2000);
   assert.deepEqual(requests,[],'pins, shortcuts and ordinary background tabs must not load at startup');
   const state=await ui.evaluate('window.__sidebarState');
   await ui.evaluate(`chrome.webview.postMessage(JSON.stringify({type:'tab.activate',id:'${state.tabs[0].id}'}))`);
   await host.wait(()=>requests.includes('/0'),'pin loads on first click');
   assert(!requests.includes('/1'),'opening one pin must not load the shortcut');
   const first=await host.connect(host.origin+'/0');
   await host.wait(()=>first.evaluate('document.title==="Restored page"&&document.readyState==="complete"').catch(()=>false),'pin ready');
   await ui.evaluate(`chrome.webview.postMessage(JSON.stringify({type:'shortcut.open',index:0}))`);
   await host.wait(()=>requests.includes('/1'),'shortcut loads on first click');
   const shortcut=await host.connect(host.origin+'/1');
   await host.wait(()=>shortcut.evaluate('document.title==="Restored page"&&document.readyState==="complete"').catch(()=>false),'shortcut ready');
   assert(!requests.includes('/3'),'ordinary background tabs remain asleep');
   const before=requests.length;
   await ui.evaluate(`chrome.webview.postMessage(JSON.stringify({type:'tab.activate',id:'${state.tabs[0].id}'}))`);
   await host.wait(()=>ui.evaluate(`window.__sidebarState.activeId===${JSON.stringify(state.tabs[0].id)}`),'pin revisited');
   assert.equal(requests.length,before,'returning to the loaded pin does not reload it');
   await ui.evaluate(`chrome.webview.postMessage(JSON.stringify({type:'tab.activate',id:'${state.activeId}'}))`);
   await host.wait(()=>ui.evaluate(`window.__sidebarState.activeId===${JSON.stringify(state.activeId)}`),'original tab selected before restart');
   await host.close(ui);
 }
 console.log(JSON.stringify({passed:'no background pin or shortcut preload at startup; first click loads only the selected tab; return reuses it; same behavior after restart',evidence:host.base}));
}finally{await host.dispose();}
