import assert from 'node:assert/strict';
import {access,readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const fixtures={
  '/2':'',
  '/3':'<input id="draft" value="initial">',
  '/4':'<input id="check" type="checkbox">',
  '/5':'<select id="choice"><option selected>A</option><option>B</option></select>',
  '/6':'<div contenteditable id="editor">Brouillon</div>',
  '/7':'<iframe src="/frame"></iframe>',
  '/8':'',
  '/9':'',
};
const instances=new Map();
const edits={
  '/3':`document.querySelector('#draft').value='Ne pas perdre'`,
  '/4':`document.querySelector('#check').checked=true`,
  '/5':`document.querySelector('#choice').selectedIndex=1`,
  '/8':`sessionStorage.setItem('draft','Gardé en mémoire')`,
};
const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{
  performance:true,saverTest:true,serve(req,res){
    if(!(req.url in fixtures))return false;
    const instance=crypto.randomUUID();instances.set(req.url,instance);
    res.writeHead(200,{'Content-Type':'text/html; charset=utf-8','Cache-Control':'no-store'});
    res.end(`<!doctype html><title>Memory ${req.url}</title><body>${fixtures[req.url]}<script>window.__instance=${JSON.stringify(instance)};window.__ticks=0;setInterval(()=>window.__ticks++,100);${edits[req.url]??''}</script>`);return true;
  }
});
const settings=JSON.parse(await readFile(resolve(host.profile,'settings.json'),'utf8'));
settings.AutoPip=false;await writeFile(resolve(host.profile,'settings.json'),JSON.stringify(settings));
let ui;
const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const state=()=>ui.evaluate('window.__sidebarState');
const tabs=new Map();
const activate=async(path,inspect=true)=>{
  const tab=(await state()).tabs.find(t=>t.url===host.origin+path);assert(tab);
  await command({type:'tab.activate',id:tab.id});
  await host.wait(()=>state().then(s=>s.tabs.some(t=>t.id===tab.id&&!t.loading&&t.title===`Memory ${path}`)),`loaded ${path}`);
  const page=inspect?await host.connect(tab.url):null;
  tabs.set(path,{...tab,page,instance:instances.get(path)});return page;
};
try{
  ui=await host.launch(760);
  // Exercise normal sleep eligibility before inspecting the page with DevTools.
  for(const path of Object.keys(fixtures))await activate(path,false);
  await host.wait(()=>state().then(s=>s.tabs.find(t=>t.id===tabs.get('/2').id)?.asleep),'plain idle tab discarded',40000);
  // Several saver passes must retain protected documents without repeated discard attempts.
  await delay(8000);
  for(const path of ['/3','/4','/5','/6','/7','/8']){
    const tab=tabs.get(path);assert.equal((await state()).tabs.find(t=>t.id===tab.id).asleep,false,`protected state ${path}`);
    tab.page=await host.connect(tab.url);
    assert.equal(await tab.page.evaluate('window.__instance'),tab.instance,`same document ${path}`);
  }
  const idle=await tabs.get('/3').page.evaluate('window.__ticks');
  const restingLog=await readFile(resolve(host.profile,'debug.log'),'utf8');
  const id=tabs.get('/3').id;
  if(restingLog.includes(`memory.low ${id}`)){
    assert(restingLog.split('\n').filter(line=>line.endsWith(`memory.low ${id}`)).length<=2,'native refusal uses low memory without retrying each saver pass');
  }else{
    assert(restingLog.includes(`memory.suspend ${id} True`),'native suspension succeeded');
    assert(idle<120,'draft timers stopped during the uninspected idle interval');
  }
  assert.equal(await tabs.get('/3').page.evaluate(`document.querySelector('#draft').value`),'Ne pas perdre');
  assert.equal(await tabs.get('/4').page.evaluate(`document.querySelector('#check').checked`),true);
  assert.equal(await tabs.get('/5').page.evaluate(`document.querySelector('#choice').value`),'B');
  assert.equal(await tabs.get('/8').page.evaluate(`sessionStorage.getItem('draft')`),'Gardé en mémoire');
  await activate('/3');
  await host.wait(()=>tabs.get('/3').page.evaluate(`window.__ticks>${idle}`),'draft resumes immediately');
  for(let cycle=0;cycle<3;cycle++){
    await activate('/9');
    await delay(6000);
    const resumed=await activate('/3');
    const ticks=await resumed.evaluate('window.__ticks');
    await host.wait(()=>resumed.evaluate(`window.__ticks>${ticks}`),'repeated sleep resumes timers');
    assert.equal(await resumed.evaluate(`document.querySelector('#draft').value`),'Ne pas perdre','resume preserves draft');
  }
  const oldEditor=instances.get('/6');
  await command({type:'nav.reload',id:tabs.get('/6').id});
  await host.wait(()=>instances.get('/6')!==oldEditor,'reload wakes a background suspended tab');
  await command({type:'settings.set',id:'memory',index:0});
  const previous=tabs.get('/2').instance;
  const restored=await activate('/2');assert.notEqual(await restored.evaluate('window.__instance'),previous,'discarded document reloads on demand');
  const background=tabs.get('/4').page;const awake=await background.evaluate('window.__ticks');
  await host.wait(()=>background.evaluate(`window.__ticks>${awake}+2`),'turning saver off resumes hidden pages');
  const log=await readFile(resolve(host.profile,'debug.log'),'utf8');
  assert(log.includes(`memory.discard ${tabs.get('/2').id}`));
  await writeFile(resolve(host.base,'memory-saver.json'),JSON.stringify({protectedPaths:['/3','/4','/5','/6','/7','/8'],discarded:true,resume:true},null,2));
  await host.close(ui);console.log(`Memory saver passed: ${host.base}`);
}catch(error){
  const log=await readFile(resolve(host.profile,'debug.log'),'utf8').catch(()=>'');
  await writeFile(resolve(host.base,'memory-debug.log'),log);
  console.error(log.split('\n').filter(line=>line.includes('memory.')).slice(-20).join('\n'));
  throw error;
}finally{
  await host.dispose();await assert.rejects(access(resolve(host.base,'profiles')),'test profiles removed');
}
