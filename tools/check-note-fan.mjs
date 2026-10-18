import assert from 'node:assert/strict';
import {access,readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost('src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
const notes=['Neimil','Object & Archive','Vero'].map((Title,index)=>({Id:'note-'+index,Title,Text:Title+'\nhttps://example.invalid/'+index,Url:'',Updated:'2026-10-18T12:00:00Z'}));
await writeFile(resolve(host.profile,'notes.json'),JSON.stringify(notes));
let ui,page;
try {
  ui=await host.launch(850,1400);
  await ui.evaluate(`chrome.webview.postMessage('{"type":"tab.open","url":"https://start.sol/index.html#notes"}')`);
  page=await host.connect('https://start.sol/index.html#notes');
  await host.wait(()=>page.evaluate('document.querySelectorAll(".note-card").length===3'),'three notes');
  await page.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup==='notes')window.__noteState=e.data});chrome.webview.postMessage('{"type":"notes.ready"}')`);
  await host.wait(()=>page.evaluate('!!window.__noteState'),'native notes state');
  await page.evaluate(`chrome.webview.dispatchEvent(new MessageEvent('message',{data:{...__noteState,aiEnabled:true}}))`);
  for(const [width,layout] of [[1150,'cards'],[360,'cards'],[700,'list']]){
    await page.call('Emulation.setDeviceMetricsOverride',{width,height:800,deviceScaleFactor:1,mobile:false});
    await page.evaluate(`document.querySelector('[aria-label="${layout==='cards'?'Vue cartes':'Vue liste'}"]').click()`);
    await host.wait(()=>page.evaluate(`!!document.querySelector('.note-${layout}')`),'layout');
    const point=await page.evaluate(`(()=>{const e=document.querySelectorAll('.notes-preview')[1];e.scrollIntoView({block:'center'});const r=e.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
    await page.call('Input.dispatchMouseEvent',{type:'mouseMoved',...point});
    await host.wait(()=>page.evaluate('document.querySelectorAll(".nod-act").length===4'),'four actions');
    assert(await page.evaluate('document.querySelector(".nod-act").getAnimations().length>0'),'original spring uses browser animations');
    await host.wait(()=>page.evaluate('[...document.querySelectorAll(".nod-act")].every(b=>b.getAnimations().every(a=>a.playState!=="running"&&!a.pending))'),'animation settles');
    const dimensions=await page.evaluate(`(()=>{const s=document.querySelector('.notes-browser-list').getBoundingClientRect();return [...document.querySelectorAll('.nod-act')].map(b=>{const r=b.getBoundingClientRect();return {left:r.left,right:r.right,top:r.top,bottom:r.bottom,inside:r.left>=s.left&&r.right<=s.right&&r.top>=s.top&&r.bottom<=s.bottom}})})()`);
    assert(dimensions.every(r=>r.inside),'all note actions fit the scroll viewport');
    assert(await page.evaluate(`(()=>{const active=document.querySelector('.nod[data-mounted]');const others=[...document.querySelectorAll('.note-card')].filter(e=>!active.contains(e)).map(e=>e.getBoundingClientRect());return [...document.querySelectorAll('.nod-act')].every(b=>{const r=b.getBoundingClientRect();return others.every(o=>r.right<=o.left||r.left>=o.right||r.bottom<=o.top||r.top>=o.bottom)})})()`),'fan never covers a neighboring note');
    await writeFile(resolve(host.base,`notes-fan-${width}-${layout}.png`),Buffer.from((await page.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await page.call('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape'});
    await host.wait(()=>page.evaluate('document.querySelectorAll(".nod-act").length===0'),'closing spring completes');
  }
  await host.close(ui);console.log('PASS four animated note actions, uncut card/list layouts at 1150/700/360, closing spring and Escape; '+host.base);
}catch(error){console.error(JSON.stringify(await page?.evaluate(`({text:document.body.innerText.slice(0,100),padding:getComputedStyle(document.querySelector('.notes-browser-list')).padding,buttons:[...document.querySelectorAll('.nod-act')].map(b=>({label:b.title,rect:b.getBoundingClientRect().toJSON()})),viewport:document.querySelector('.notes-browser-list')?.getBoundingClientRect().toJSON()})`).catch(()=>null),null,2));await writeFile(resolve(host.base,'notes-fan-failure.png'),Buffer.from((await page.call('Page.captureScreenshot',{format:'png'})).data,'base64'));console.error(host.base);throw error;}
finally{await host.dispose();await assert.rejects(access(host.profile));}
