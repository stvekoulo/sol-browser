import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';
const requests=[];
const host=await sidebarHost(process.argv.slice(2).find(a=>!a.startsWith('--'))??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{captures:true,serve(req,res){
  const path=req.url;
  if(!['/source','/preview1','/preview2'].includes(path))return false;
  requests.push({path,cookie:req.headers.cookie??''});
  res.writeHead(200,{'Content-Type':'text/html; charset=utf-8','X-Frame-Options':'DENY','Content-Security-Policy':"frame-ancestors 'none'",...(path==='/source'?{'Set-Cookie':'preview_session=fixture; HttpOnly; SameSite=Lax; Path=/'}:{})});
  res.end(`<html><head><title>${path}</title></head><body style="height:2000px;font:18px system-ui"><h1>${path}</h1><input id="draft" aria-label="Draft"><a id="next" href="/preview2" target="_blank">Next preview page</a><a id="target" href="/preview1">Preview this link</a></body></html>`);return true;
}});
let ui;
const cmd=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const state=()=>ui.evaluate('window.__sidebarState');
async function waitGone(target){await host.wait(async()=>!(await host.targets()).some(t=>t.id===target.id),'preview target closed')}
try{
  ui=await host.launch(850,1200);
  const id=(await state()).activeId;
  await cmd({type:'nav.go',id,input:host.origin+'/source'});
  const source=await host.connect(host.origin+'/source');
  await host.wait(()=>source.evaluate('location.pathname==="/source"&&!!document.querySelector("#draft")').catch(()=>false),'source document');
  await host.wait(async()=>(await state()).tabs.find(t=>t.id===id)?.url===host.origin+'/source','source URL');
  await source.evaluate('document.querySelector("#draft").value="Keep my work";scrollTo(0,500)');
  const scroll=await source.evaluate('scrollY'),count=(await state()).tabs.length;
  await cmd({type:'preview.open',id,input:host.origin+'/preview1'});
  const first=await host.connect(host.origin+'/preview1');
  await host.wait(()=>first.evaluate('location.pathname==="/preview1"&&!!document.querySelector("#next")&&document.readyState==="complete"').catch(()=>false),'preview loaded despite frame headers');
  assert.equal((await state()).tabs.length,count,'Preview is not a tab');
  assert.equal(await source.evaluate('document.querySelector("#draft").value'),'Keep my work');
  assert.equal(await source.evaluate('scrollY'),scroll);
  assert(requests.find(r=>r.path==='/preview1').cookie.includes('preview_session=fixture'),'Same source session');
  const nextPoint=await first.evaluate('(()=>{const r=document.querySelector("#next").getBoundingClientRect();return{x:r.left+5,y:r.top+5}})()');
  for(const type of ['mouseMoved','mousePressed','mouseReleased'])await first.call('Input.dispatchMouseEvent',{type,...nextPoint,...(type==='mouseMoved'?{}:{button:'left',clickCount:1})});
  const second=await host.connect(host.origin+'/preview2');
  await host.wait(()=>second.evaluate('location.pathname==="/preview2"&&!!document.querySelector("#draft")&&document.readyState==="complete"').catch(()=>false),'navigate in preview');
  assert.equal((await state()).tabs.length,count,'target blank stays in preview');
  await second.evaluate('document.querySelector("#draft").focus()');await second.call('Input.insertText',{text:'Interactive preview'});
  assert.equal(await second.evaluate('document.querySelector("#draft").value'),'Interactive preview');
  await cmd({type:'preview.back'});await host.wait(()=>first.evaluate('location.pathname==="/preview1"'),'back');
  await cmd({type:'preview.forward'});await host.wait(()=>first.evaluate('location.pathname==="/preview2"'),'forward');
  await writeFile(resolve(host.base,'link-preview-content.png'),Buffer.from((await second.call('Page.captureScreenshot')).data,'base64'));
  if(process.argv.includes('--visual')){
    await cmd({type:'capture.menu'});
    const capture=await host.connect('#capture');
    await host.wait(()=>capture.evaluate('document.readyState==="complete"'),'capture controls initialized');
    await cmd({type:'capture.run',query:'visible',index:1});
    await host.wait(()=>capture.evaluate('!!document.querySelector(".capture-review img")?.naturalWidth'),'native window preview',40000);
    const image=await capture.evaluate('document.querySelector(".capture-review img").src');
    await writeFile(resolve(host.base,'link-preview-window.png'),Buffer.from(image.split(',')[1],'base64'));
    await cmd({type:'capture.close'});
  }
  await cmd({type:'preview.tab'});
  await host.wait(async()=>{const s=await state();return s.tabs.length===count+1&&s.tabs.find(t=>t.id===s.activeId)?.url===host.origin+'/preview2'},'promote current URL into tab');
  await cmd({type:'tab.activate',id});
  await cmd({type:'preview.open',id,input:host.origin+'/preview1'});await host.connect(host.origin+'/preview1');
  await cmd({type:'preview.split'});await host.wait(async()=>{const s=await state();return s.split.primaryId===id&&s.tabs.some(t=>t.id===s.split.secondaryId&&t.url===host.origin+'/preview1')},'open beside source');
  await cmd({type:'split.close'});await cmd({type:'tab.activate',id});
  // Alt+click is an explicit real gesture; no normal click behavior is replaced.
  await source.evaluate('scrollTo(0,0)');
  const beforeAlt=new Set((await host.targets()).map(t=>t.id));
  const point=await source.evaluate('(()=>{const r=document.querySelector("#target").getBoundingClientRect();return{x:r.left+5,y:r.top+5}})()');
  for(const type of ['mouseMoved','mousePressed','mouseReleased'])await source.call('Input.dispatchMouseEvent',{type,...point,modifiers:1,...(type==='mouseMoved'?{}:{button:'left',clickCount:1})});
  const target=await host.wait(async()=>(await host.targets()).find(t=>t.url===host.origin+'/preview1'&&!beforeAlt.has(t.id)),'Alt click creates preview');
  const escape=await host.connect(target.url,target.id);
  await host.wait(()=>escape.evaluate('document.readyState==="complete"'),'preview ready for Escape');
  await escape.call('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});await waitGone(target);
  const beforeInvalid=(await host.targets()).length;
  await cmd({type:'preview.open',id,input:'javascript:alert(1)'});assert.equal((await host.targets()).length,beforeInvalid);
  assert.equal(await source.evaluate('document.querySelector("#draft").value'),'Keep my work');
  await writeFile(resolve(host.base,'link-preview-verification.json'),JSON.stringify({checks:['no tab for preview','real navigable WebView','X-Frame-Options DENY works','session cookie retained','source form and scroll preserved','target blank stays inside preview','typing','back forward','promote current URL','split view','Alt click','Escape','invalid protocol blocked']},null,2));
  await host.close(ui);console.log('Link preview checks passed: '+host.base);
}finally{await host.dispose()}
