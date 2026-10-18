import assert from 'node:assert/strict';
import {access,readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost('src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
await writeFile(resolve(host.profile,'session.json'),JSON.stringify({Urls:['https://start.sol/index.html#notes'],ActiveIndex:0}));
await writeFile(resolve(host.profile,'notes.json'),JSON.stringify([{Id:'test',Title:'Note de test',Text:'Un texte de test',Url:'',Updated:'2026-10-18T12:00:00Z'}]));
let ui;
try {
  ui=await host.launch(850,1200);
  const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
  const notes=await host.connect('https://start.sol/index.html#notes');
  await host.wait(()=>notes.evaluate('!!document.querySelector(".note-card")'),'note');
  await command({type:'chat.toggle'});const chat=await host.connect('#chat');
  const seed=()=>chat.evaluate(`chrome.webview.dispatchEvent(new MessageEvent('message',{data:{chat:true,ready:true,account:'test',busy:false,error:null,messages:[{from:'assistant',text:'Une réponse de test avec un lien très long https://example.invalid/'+ 'long'.repeat(40)}]}}))`);
  const enabled=async index=>{
    await command({type:'settings.set',id:'fanEnabled',index});
    await host.wait(async()=>JSON.parse(await readFile(resolve(host.profile,'settings.json'),'utf8')).FanEnabled===!!index,'preference');
    await seed();
  };
  const move=async(page,selector,button)=>{
    const p=await page.evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(selector)});e.scrollIntoView({block:'center'});const r=e.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
    await page.call('Input.dispatchMouseEvent',{type:'mouseMoved',...p});
    if(button)for(const type of ['mousePressed','mouseReleased'])await page.call('Input.dispatchMouseEvent',{type,...p,button,clickCount:1});
  };
  await host.wait(async()=>{await seed();return chat.evaluate('!!document.querySelector(".nod")');},'assistant action wrapper');
  assert.equal(await chat.evaluate('document.querySelectorAll(".nod-act").length'),0,'no unused mounted chat buttons');
  await command({type:'settings.set',id:'fanTrigger',index:0});
  await move(chat,'.nod');await host.wait(()=>chat.evaluate('document.querySelectorAll(".nod-act:not(:disabled)").length===2'),'hover opens inline chat actions');
  await enabled(0);
  for(const page of [notes,chat]){await move(page,'.nod');assert.equal(await page.evaluate('document.querySelectorAll(".nod-act:not(:disabled)").length'),0);}
  await command({type:'settings.set',id:'fanTrigger',index:2});await enabled(1);
  for(const page of [notes,chat]){
    await move(page,'.nod','right');
    await host.wait(()=>page.evaluate('document.querySelectorAll(".nod-act:not(:disabled)").length>0'),'right click fan');
    await page.call('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape'});
    await host.wait(()=>page.evaluate('document.querySelectorAll(".nod-act").length===0'),'Escape unmounts');
  }
  await chat.call('Emulation.setDeviceMetricsOverride',{width:280,height:650,deviceScaleFactor:1,mobile:false});
  await move(chat,'.nod','right');
  await host.wait(()=>chat.evaluate('document.querySelectorAll(".nod-act").length===2'),'narrow chat');
  assert(await chat.evaluate('document.documentElement.scrollWidth<=innerWidth'),'no chat overflow');
  assert(await chat.evaluate('[...document.querySelectorAll(".nod-act")].every(b=>{const r=b.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth})'),'inline commands fit');
  await chat.call('Emulation.setEmulatedMedia',{features:[{name:'prefers-reduced-motion',value:'reduce'}]});
  await enabled(0);await host.wait(()=>chat.evaluate('document.querySelectorAll(".nod-act").length===0'),'reduced motion closes inline commands');
  await host.close(ui);
  console.log('PASS notes and chat: global disable, right click, Escape, 280px, reduced motion; '+host.base);
} finally {await host.dispose();await assert.rejects(access(host.profile));}
