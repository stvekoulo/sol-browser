import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{
  serve(req,res){
    if(req.url!=='/2')return false;
    res.writeHead(200,{'Content-Type':'text/html','Cache-Control':'no-store'});
    res.end('<title>Recovery</title><input value="draft"><script>window.instance=crypto.randomUUID();window.ticks=0;setInterval(()=>ticks++,50)</script>');return true;
  }
});
let ui;
const cmd=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
try{
  ui=await host.launch();
  const page=await host.connect(host.origin+'/2');
  const id=await ui.evaluate(`window.__sidebarState.tabs.find(t=>t.url===${JSON.stringify(host.origin+'/2')}).id`);
  const before=await page.evaluate('instance');
  await page.evaluate('setTimeout(()=>{while(true){}},100)');
  await delay(1000);
  await cmd({type:'nav.reload',id});
  await host.wait(async()=>{
    const matches=(await host.targets()).filter(t=>t.url===host.origin+'/2');
    if(!matches.length)return false;
    try{
      const recovered=await host.connect(host.origin+'/2',matches[0].id);
      return (await recovered.evaluate('instance'))!==before;
    }catch{return false;}
  },'explicit reload recovers unresponsive renderer',25000);
  const restored=await host.connect(host.origin+'/2');
  const ticks=await restored.evaluate('ticks');await delay(250);
  assert((await restored.evaluate('ticks'))>ticks,'reloaded tab is interactive');
  await writeFile(resolve(host.base,'tab-recovery.json'),JSON.stringify({unresponsive:true,reloaded:true}));
  await host.close(ui);console.log(`PASS unresponsive tab recovery: ${host.base}`);
}finally{await host.dispose();}
