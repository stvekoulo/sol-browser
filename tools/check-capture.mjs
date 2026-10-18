import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{
 captures:true,serve(req,res){
  if(req.url!=='/2')return false;
  res.writeHead(200,{'Content-Type':'text/html; charset=utf-8'});
  res.end('<!doctype html><meta name="viewport" content="width=device-width,initial-scale=1"><title>Capture fixture</title><body style="margin:0;min-width:1300px;height:4600px;background:linear-gradient(#f9d455,#419797)"><input id="draft" style="position:fixed;top:20px;left:20px" value="Texte à conserver"><button id="live" style="position:fixed;top:60px;left:20px" onclick="this.dataset.clicks=Number(this.dataset.clicks||0)+1">Tester la page</button><script>window.__ticks=0;setInterval(()=>{window.__ticks++},50)</script>');return true;
 }
});
let ui,page,capture;
const cmd=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const wait=expression=>host.wait(()=>capture.evaluate(expression).catch(()=>false),expression,40000);
const stats=()=>page.evaluate('({width:innerWidth,height:innerHeight,dpr:devicePixelRatio,x:scrollX,y:scrollY,touch:navigator.maxTouchPoints,agent:navigator.userAgent,draft:document.querySelector("#draft").value})');
const png=async()=>Buffer.from((await page.call('Page.captureScreenshot',{format:'png',captureBeyondViewport:false})).data,'base64');
const size=image=>[image.readUInt32BE(16),image.readUInt32BE(20)];
const unchanged=async(before,render)=>{
 assert.deepEqual(await stats(),before,'capture restores viewport, zoom, scroll, input and emulation');
 assert.deepEqual(size(await png()),render,'live rendering has the original pixel dimensions');
 const ticks=await page.evaluate('window.__ticks');await delay(100);assert(await page.evaluate(`window.__ticks>${ticks}`),'page keeps running');
};
const open=async(mode,browser=false)=>{
 await cmd({type:'capture.menu'});capture??=await host.connect('#capture');await wait('!!document.querySelector(".capture-modes")');
 if(await capture.evaluate('document.querySelector(".capture-browser").checked')!==browser)await capture.evaluate('document.querySelector(".capture-browser").click()');
 await wait(`document.querySelector('.capture-browser').checked===${browser}`);
 const index={region:0,visible:1,full:2}[mode];
 await capture.evaluate(`document.querySelectorAll('.capture-modes button')[${index}].click()`);
 await wait(`document.querySelectorAll('.capture-modes button')[${index}].getAttribute('aria-pressed')==='true'`);
 await capture.evaluate('document.querySelector(".capture-action").click()');
};
const review=async()=>{
 await wait('!!document.querySelector(".capture-review img")?.naturalWidth');await delay(150);
 assert(await capture.evaluate('(()=>{const r=document.querySelector(".capture-bar").getBoundingClientRect();return r.top>=0&&r.bottom<=innerHeight+1&&r.left>=0&&r.right<=innerWidth+1})()'),'review actions remain visible');
 return capture.evaluate('({width:document.querySelector(".capture-review img").naturalWidth,height:document.querySelector(".capture-review img").naturalHeight})');
};
const escape=async(target)=>{
 await target.call('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});
 await target.call('Input.dispatchKeyEvent',{type:'keyUp',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});
};
const closed=()=>wait('document.hidden&&!document.querySelector(".capture-review")&&!document.querySelector(".capture-selection")');
try{
 ui=await host.launch(760);const tab=await ui.evaluate('window.__sidebarState.activeId');page=await host.connect(`${host.origin}/2`);
 assert(!(await host.targets()).some(t=>t.url.endsWith('#capture')),'capture renderer is lazy');
 await cmd({type:'capture.menu'});capture=await host.connect('#capture');await wait('!!document.querySelector(".capture-modes")');
 await cmd({type:'capture.close'});
 await host.wait(()=>page.evaluate('!!document.querySelector("#draft")').catch(()=>false),'fixture loaded');
 await capture.evaluate('window.__events=[];chrome.webview.addEventListener("message",e=>{if(e.data.event?.startsWith("capture-"))window.__events.push({event:e.data.event,kind:e.data.kind})})');
 for(const zoom of [false,true]){
  if(zoom){await cmd({type:'zoom.out'});await host.wait(()=>page.evaluate('devicePixelRatio<1'),'page zoom applied');}
  await page.evaluate('scrollTo({left:100,top:600,behavior:"instant"})');const before=await stats(),render=size(await png());
  await open('full');const image=await review();assert(image.width>render[0]&&image.height>4000,'full-page image includes offscreen content');
  await unchanged(before,render);
  if(zoom){await escape(capture);await closed();}
  else{
   const shot=await capture.call('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await writeFile(resolve(host.base,'capture-full-review.png'),Buffer.from(shot.data,'base64'));
   await capture.evaluate('document.querySelector(".capture-review .capture-action").click()');await closed();
   const saved=await readFile(resolve(host.base,'captures','capture.png'));assert.deepEqual(size(saved),[image.width,image.height]);
   assert.equal((await readFile(resolve(host.base,'captures','capture.pdf'))).subarray(0,4).toString(),'%PDF','full-page PDF saved');
  }
  await unchanged(before,render);
 }
 await cmd({type:'zoom.reset'});await host.wait(()=>page.evaluate('devicePixelRatio===1'),'page zoom reset');
 for(const browser of [false,true]){
  const before=await stats(),render=size(await png());await open('visible',browser);await review();await escape(capture);await closed();await unchanged(before,render);
 }
 await open('region');await host.wait(()=>page.evaluate('[...document.querySelectorAll("div")].some(el=>el.style.cursor==="crosshair"&&el.style.zIndex==="2147483647")'),'page region selector');
 await escape(page);await closed();
 await open('region');await host.wait(()=>page.evaluate('[...document.querySelectorAll("div")].some(el=>el.style.cursor==="crosshair"&&el.style.zIndex==="2147483647")'),'region selector reopened');
 const regionBefore=await stats(),regionRender=size(await png());
 await page.call('Input.dispatchMouseEvent',{type:'mousePressed',x:80,y:100,button:'left',buttons:1,clickCount:1});
 await page.call('Input.dispatchMouseEvent',{type:'mouseMoved',x:240,y:200,buttons:1});
 await page.call('Input.dispatchMouseEvent',{type:'mouseReleased',x:240,y:200,button:'left',buttons:0,clickCount:1});
 const region=await review();assert(region.width>10&&region.width<regionRender[0]);await escape(capture);await closed();await unchanged(regionBefore,regionRender);
 for(const [mode,browser] of [['full',0],['region',1]]){
  const index=await capture.evaluate('window.__events.length');
  await ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify({type:'capture.run',query:mode,index:browser}))});chrome.webview.postMessage('{"type":"capture.close"}')`);
  await wait(`window.__events.slice(${index}).some(e=>e.kind==='done')`);await delay(200);
  assert(await capture.evaluate(`!window.__events.slice(${index}).some(e=>e.event==='capture-review'||e.event==='capture-preview')`),'cancel during preparation cannot reopen an overlay');
 }
 await cmd({type:'device.set',id:tab,query:'iPhone 18 Pro Max'});
 await host.wait(()=>page.evaluate('navigator.maxTouchPoints>0&&!!document.querySelector("#draft")').catch(()=>false),'mobile simulator ready');await delay(300);
 const mobile=await stats(),mobileRender=size(await png());await open('full');await review();await escape(capture);await closed();await unchanged(mobile,mobileRender);
 await cmd({type:'device.set',id:tab,query:''});await host.wait(()=>page.evaluate('navigator.maxTouchPoints===0'),'desktop restored');
 await host.close(ui);ui=undefined;capture=undefined;
 ui=await host.launch(380,420);page=await host.connect(`${host.origin}/2`);
 await open('visible');await review();const small=await capture.call('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await writeFile(resolve(host.base,'capture-small-review.png'),Buffer.from(small.data,'base64'));await escape(capture);await closed();
 console.log(JSON.stringify({passed:'full/visible/region/window screenshots; save PNG/PDF; viewport, scroll and form restoration; zoom; cancellation before/during review; mobile touch/DPR; narrow-window controls',evidence:host.base}));
}catch(error){console.error('CAPTURE',await capture?.evaluate('({events:window.__events,text:document.body.innerText})').catch(()=>null));throw error;}
finally{if(ui)await host.close(ui).catch(()=>{});await host.dispose()}
