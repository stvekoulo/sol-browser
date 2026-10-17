import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {resolve,basename} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';

const payload=Buffer.alloc(32768,7);
const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{
  downloads:true,serve(req,res){
    if(!req.url.startsWith('/download/'))return false;
    res.writeHead(200,{'Content-Type':'application/octet-stream','Content-Length':payload.length,
      'Content-Disposition':`attachment; filename="${req.url.split('/').at(-1)}"`});
    res.end(payload);return true;
  }
});
const log=()=>readFile(resolve(host.profile,'debug.log'),'utf8');
const shown=async()=>(await log()).split('dock.shown').length-1;
try{
  const ui=await host.launch(),page=await host.connect(host.origin+'/2');
  for(const name of ['file-one.bin','file-two.bin']){
    const before=await shown();
    await page.evaluate(`void window.open(${JSON.stringify(host.origin+'/download/'+name)})`);
    const dock=await host.connect('#dock');
    await host.wait(()=>dock.evaluate(`document.body.textContent.includes(${JSON.stringify(name)})`).catch(()=>false),'current download card');
    await host.wait(async()=>await shown()>before,'native download frame appears for '+name,5000);
    await host.wait(async()=>{const text=await log();return text.lastIndexOf('dock.hidden')>text.lastIndexOf('dock.shown');},'finished download frame hides',12000);
    assert.equal((await readFile(resolve(host.base,'downloads',name))).length,payload.length);
  }
  const before=await shown();
  const command=object=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(object))})`);
  await command({type:'settings.set',id:'floatingDownloads',index:0});
  await page.evaluate(`void window.open(${JSON.stringify(host.origin+'/download/file-off.bin')})`);
  await host.wait(()=>readFile(resolve(host.base,'downloads/file-off.bin')).then(data=>data.length===payload.length).catch(()=>false),'download completes with floating cards disabled');
  assert.equal(await shown(),before,'disabled floating cards stay hidden');
  await command({type:'settings.set',id:'floatingDownloads',index:1});
  await host.wait(async()=>await shown()>before,'reenabling floating cards restores the native frame');
  console.log(JSON.stringify({passed:'each sequential download reopens its native bottom-right frame after the previous card disappears',profile:basename(host.profile),evidence:host.base}));
}finally{await host.dispose();}
