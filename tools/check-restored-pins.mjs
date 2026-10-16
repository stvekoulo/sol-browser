import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
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
   await host.wait(()=>requests.includes('/0')&&requests.includes('/1'),'restored pins and shortcut prepare after startup',5000);
   assert(!requests.includes('/3'),'ordinary restored tabs remain unloaded');
   const first=await host.connect(host.origin+'/0'),shortcut=await host.connect(host.origin+'/1');
   await host.wait(()=>Promise.all([first,shortcut].map(page=>page.evaluate('document.title==="Restored page"&&document.readyState==="complete"').catch(()=>false))).then(states=>states.every(Boolean)),'pinned pages ready');
   const count=requests.length,state=await ui.evaluate('window.__sidebarState');
   const documents=await Promise.all([first.evaluate('performance.timeOrigin'),shortcut.evaluate('performance.timeOrigin')]);
   await ui.evaluate(`chrome.webview.postMessage(JSON.stringify({type:'tab.activate',id:'${state.tabs[0].id}'}))`);
   await host.wait(()=>ui.evaluate(`window.__sidebarState.activeId===${JSON.stringify(state.tabs[0].id)}`),'pin activated');
   await ui.evaluate(`chrome.webview.postMessage(JSON.stringify({type:'shortcut.open',index:0}))`);
   await host.wait(()=>ui.evaluate(`window.__sidebarState.activeId===${JSON.stringify(state.tabs[1].id)}`),'restored shortcut activated');
   assert.deepEqual(await Promise.all([first.evaluate('performance.timeOrigin'),shortcut.evaluate('performance.timeOrigin')]),documents,'activation keeps the prepared documents');
   assert.equal(requests.length,count,'first access reuses the loaded page instead of starting a new connection');
   await host.close(ui);
 }
 console.log(JSON.stringify({passed:'pins and restored shortcut load after startup, first access reuses them, ordinary tabs stay asleep, behavior survives restart',evidence:host.base}));
}finally{await host.dispose();}
