import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';
const payload=Buffer.alloc(2*1024*1024,7);
const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{downloads:true,serve(req,res){
  if(!req.url.startsWith('/stack/'))return false;
  res.writeHead(200,{'Content-Type':'application/octet-stream','Content-Length':payload.length,'Content-Disposition':`attachment; filename="${req.url.split('/').at(-1)}"`});
  let offset=0;const timer=setInterval(()=>{if(offset===payload.length){clearInterval(timer);res.end();return;}res.write(payload.subarray(offset,offset+16384));offset+=16384;},100);res.on('close',()=>clearInterval(timer));return true;
}});
let sidebar,dock;
const cmd=c=>sidebar.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const latest=()=>dock.evaluate('window.__downloads?.items??[]');
try{
  sidebar=await host.launch(380);const source=(await sidebar.evaluate('window.__sidebarState')).tabs.find(t=>t.url===host.origin+'/2');
  for(let i=0;i<4;i++){
    await cmd({type:'tab.activate',id:source.id});const page=await host.connect(source.url);
    await page.evaluate(`void window.open(${JSON.stringify(host.origin+'/stack/file-'+i+'.bin')})`);
    if(!dock){dock=await host.connect('#dock');await host.wait(()=>dock.evaluate('!!document.querySelector(".sol-download-stack")').catch(()=>false),'stack ready');await dock.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup==='downloads')window.__downloads=e.data});chrome.webview.postMessage('{"type":"dock.ready"}')`);}
    await host.wait(()=>latest().then(items=>items.some(d=>d.name===`file-${i}.bin`)),'queued download '+i);
  }
  await dock.evaluate('document.querySelector(".sol-download-stack-footer").click()');
  await host.wait(()=>dock.evaluate('document.querySelectorAll(".download-card").length===4'),'expanded stack includes all downloads');
  assert.equal(await dock.evaluate('document.querySelectorAll("button button").length'),0,'actions are independent controls');
  await delay(400);
  assert(await dock.evaluate('innerHeight<340'),'native stack stays inside short window');
  await dock.evaluate('document.querySelector(".sol-download-stack-footer").scrollIntoView({block:"end"})');
  assert(await dock.evaluate('document.querySelector(".sol-download-stack-footer").getBoundingClientRect().bottom<=innerHeight+1'),'footer reachable by scrolling');
  const shot=await dock.call('Page.captureScreenshot',{format:'png'});await writeFile(resolve(host.base,'download-stack-expanded.png'),Buffer.from(shot.data,'base64'));
  for(const d of await latest())if(d.state==='InProgress')await cmd({type:'download.cancel',id:d.id});
  console.log(JSON.stringify({passed:'four native downloads; all cards on expansion; independent actions; native bounds and reachable footer in short window',evidence:host.base}));
}catch(error){if(dock)console.error('STACK',await dock.evaluate('({text:document.body.innerText,height:innerHeight,scroll:document.querySelector("#root").scrollHeight,client:document.querySelector("#root").clientHeight})').catch(()=>null));throw error;}
finally{if(sidebar)await host.close(sidebar).catch(()=>{});await host.dispose();}
