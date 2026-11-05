import assert from 'node:assert/strict';
import {readFile, writeFile} from 'node:fs/promises';
import {resolve, dirname} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

// Instrument only the dedicated test build, never an installed browser.
const executable=resolve(process.argv[2]??'bin/session-persistence-build/Sol.exe');
assert([resolve('bin/session-persistence-build'),resolve('bin/sol-session-fix-1.2.7')].includes(dirname(executable)),'Use only a dedicated test build');
const index=resolve(dirname(executable),'ui/index.html');
const original=await readFile(index,'utf8');
const hook=`<script>
if(location.hostname==='ui.navigateur'&&!location.hash){
 const send=chrome.webview.postMessage.bind(chrome.webview);
 window.releaseSessionStartup=()=>{window.sessionStartupReleased=true;send(JSON.stringify({type:'ui.ready'}));};
 chrome.webview.postMessage=value=>{
  let command;try{command=JSON.parse(value);}catch{}
  if(command?.type==='ui.ready'&&!window.sessionStartupReleased)return;
  send(value);
 };
}
</script>`;
let host,ui;
try{
 await writeFile(index,original.replace('<head>','<head>'+hook));
 host=await sidebarHost(executable);
 const sessionFile=resolve(host.profile,'session.json');
 const settingsFile=resolve(host.profile,'settings.json');
 const initial=await readFile(sessionFile,'utf8');
 const settings=JSON.parse(await readFile(settingsFile,'utf8'));
 const shortcuts=value=>value.Shortcuts.map(s=>({...s,NameLocked:s.NameLocked??false}));
 // launch waits for sidebar state, so release startup ourselves through CDP.
 const launching=host.launch().catch(error=>error);
 ui=await host.connect('#sidebar');
 await host.wait(()=>ui.evaluate('!!window.releaseSessionStartup').catch(()=>false),'startup instrumented');
 await ui.evaluate(`chrome.webview.postMessage(JSON.stringify({type:'tabs.selection.clear'}))`);
 await delay(2800);
 assert.equal(await readFile(sessionFile,'utf8'),initial,'A refresh before ui.ready must not overwrite the saved workspace');
 await ui.evaluate('window.releaseSessionStartup()');
 ui=await launching;
 if(ui instanceof Error)throw ui;
 let state=await ui.evaluate('window.__sidebarState');
 assert.equal(state.tabs.length,12,'All saved tabs restored');
 assert.equal(state.tabs.filter(tab=>tab.pinned).length,2);
 assert.equal(state.groups.length,2);
 assert.equal(state.shortcuts.length,3);
 await host.close(ui);
 assert.deepEqual(shortcuts(JSON.parse(await readFile(settingsFile,'utf8'))),shortcuts(settings),'Closing preserves shortcuts');
 // Closing before ui.ready must leave the previous workspace untouched too.
 const before=await readFile(sessionFile,'utf8');
 const earlyLaunch=host.launch().catch(()=>null);
 ui=await host.connect('#sidebar');
 await host.wait(()=>ui.evaluate('!!window.releaseSessionStartup').catch(()=>false),'second startup instrumented');
 await host.close(ui);
 await earlyLaunch;
 assert.equal(await readFile(sessionFile,'utf8'),before,'Early close must preserve the previous workspace');
 const restart=host.launch();
 ui=await host.connect('#sidebar');
 await host.wait(()=>ui.evaluate('!!window.releaseSessionStartup').catch(()=>false),'third startup instrumented');
 await ui.evaluate('window.releaseSessionStartup()');
 ui=await restart;
 state=await ui.evaluate('window.__sidebarState');
 assert.equal(state.tabs.length,12);
 assert.equal(state.groups.length,2);
 assert.equal(state.tabs.filter(tab=>tab.pinned).length,2);
 assert.equal(state.shortcuts.length,3);
 await host.close(ui);
 console.log(JSON.stringify({passed:true,checks:['slow startup','early close','restart','tabs','groups','pins','shortcuts']}));
}catch(error){
 if(host){
  console.error('Targets:',JSON.stringify(await host.targets()));
  console.error((await readFile(resolve(host.profile,'debug.log'),'utf8').catch(()=>'' )).split('\n').slice(-24).join('\n'));
 }
 throw error;
}finally{
 try{await host?.dispose();}finally{await writeFile(index,original);}
}
