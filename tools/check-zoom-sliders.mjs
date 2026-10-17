import assert from 'node:assert/strict';
import {readFile,writeFile,access} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';
const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
let sidebar,popup,page;
const cmd=c=>sidebar.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const settings=async()=>{
  const path=resolve(host.profile,'settings.json');
  const raw=await readFile(path,'utf8').catch(async error=>{
    if(error.code!=='ENOENT')throw error;
    await delay(30);return readFile(path,'utf8');
  });
  return JSON.parse(raw);
};
const slider=label=>`[role="slider"][aria-label=${JSON.stringify(label)}]`;
const percent=label=>popup.evaluate(`Number(document.querySelector(${JSON.stringify(slider(label))})?.getAttribute('aria-valuenow'))`);
const key=async(label,key,virtual)=>{
  await popup.evaluate(`document.querySelector(${JSON.stringify(slider(label))}).focus()`);
  await popup.call('Input.dispatchKeyEvent',{type:'keyDown',key,code:key,windowsVirtualKeyCode:virtual});
  await popup.call('Input.dispatchKeyEvent',{type:'keyUp',key,code:key,windowsVirtualKeyCode:virtual});
};
const section=async name=>{
  await popup.evaluate(`[...document.querySelectorAll('.settings-nav [role=tab]')].find(b=>b.textContent.includes(${JSON.stringify(name)})).click()`);
  await host.wait(()=>popup.evaluate(`!![...document.querySelectorAll('.settings-nav [role=tab]')].find(b=>b.textContent.includes(${JSON.stringify(name)})&&b.getAttribute('aria-selected')==='true')`),'settings section');
};
const open=async()=>{
  await cmd({type:'settings.open',x:980,y:65});popup=await host.connect('#popup');
  await host.wait(()=>popup.evaluate('!!document.querySelector(".settings-popup")').catch(()=>false),'settings ready');
};
const zoom=async expected=>{
  await host.wait(()=>settings().then(s=>Math.abs(s.PageZoom-expected/100)<.0001),'saved page zoom');
  await host.wait(()=>page.evaluate(`Math.abs(devicePixelRatio-${expected/100})<.001`),'real page zoom');
  await host.wait(()=>percent('Zoom des pages').then(value=>value===expected),'rendered page zoom');
};
const point=(label,ratio)=>popup.evaluate(`(()=>{const r=document.querySelector(${JSON.stringify(slider(label))}).parentElement.getBoundingClientRect();return {x:r.left+r.width*${ratio},y:r.top+r.height/2}})()`);
const pointer=(type,p,buttons)=>popup.call('Input.dispatchMouseEvent',{type,...p,button:'left',buttons,clickCount:1});
const aligned=label=>host.wait(()=>popup.evaluate(`(()=>{const handle=document.querySelector(${JSON.stringify(slider(label))}),track=handle.parentElement,r=track.getBoundingClientRect(),h=handle.getBoundingClientRect(),scale=r.width/track.offsetWidth,p=(Number(handle.getAttribute('aria-valuenow'))-Number(handle.getAttribute('aria-valuemin')))/(Number(handle.getAttribute('aria-valuemax'))-Number(handle.getAttribute('aria-valuemin')));return Math.abs(h.left+h.width/2-(r.left+10*scale+(r.width-20*scale)*p))<2})()`),'slider thumb aligns with its value',3000);
try{
  sidebar=await host.launch();page=await host.connect(host.origin+'/2');await open();
  assert.equal(await percent('Zoom des pages'),100,'settings expose the beUI page slider');
  await aligned('Zoom des pages');
  await key('Zoom des pages','ArrowRight',39);await zoom(105);
  await key('Zoom des pages','ArrowLeft',37);await zoom(100);
  await key('Zoom des pages','Home',36);await zoom(50);
  for(let expected=55;expected<=200;expected+=5){await cmd({type:'zoom.in'});await zoom(expected);}
  await key('Zoom des pages','ArrowRight',39);await zoom(200);
  for(let expected=195;expected>=50;expected-=5){await cmd({type:'zoom.out'});await zoom(expected);}
  await key('Zoom des pages','ArrowLeft',37);await zoom(50);
  await key('Zoom des pages','End',35);await zoom(200);
  await cmd({type:'zoom.set',index:105});await zoom(105);
  await cmd({type:'zoom.set',index:52});await zoom(105);
  await pointer('mousePressed',await point('Zoom des pages',.3),1);await zoom(95);
  await pointer('mouseMoved',await point('Zoom des pages',.4),1);await zoom(110);
  await pointer('mouseReleased',await point('Zoom des pages',.4),0);
  await cmd({type:'zoom.set',index:105});await zoom(105);
  await section('Apparence');
  await pointer('mousePressed',await point('Taille de l’interface',.25),1);
  await host.wait(()=>settings().then(s=>s.UiScale===.85),'interface pointer starts at 85');
  await host.wait(()=>percent('Taille de l’interface').then(value=>value===85),'interface pointer value rendered');
  await pointer('mouseMoved',await point('Taille de l’interface',.5),1);
  await host.wait(()=>settings().then(s=>s.UiScale===.9),'interface drag reaches 90 after rescaling');
  await pointer('mouseReleased',await point('Taille de l’interface',.5),0);
  await key('Taille de l’interface','End',35);await host.wait(()=>settings().then(s=>s.UiScale===1),'interface drag resets');
  await host.wait(()=>percent('Taille de l’interface').then(value=>value===100),'interface reset rendered');
  for(const [expected,factor] of [[95,.905],[90,.89],[85,.875],[80,.86]]){
    await key('Taille de l’interface','ArrowLeft',37);
    await host.wait(()=>settings().then(s=>s.UiScale===expected/100),'saved interface size');
    await host.wait(()=>sidebar.evaluate(`Number(document.body.style.zoom)===${factor}`),'native and web interface sizes agree');
    await host.wait(()=>percent('Taille de l’interface').then(value=>value===expected),'interface keyboard value rendered');
    assert.equal(await percent('Taille de l’interface'),expected);
    await aligned('Taille de l’interface');
    assert.equal((await settings()).PageZoom,1.05,'interface size preserves page zoom');
    assert.equal(await popup.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),true,'slider fits at every scale');
  }
  await key('Taille de l’interface','ArrowLeft',37);assert.equal(await percent('Taille de l’interface'),80);
  await key('Taille de l’interface','End',35);await host.wait(()=>settings().then(s=>s.UiScale===1),'maximum interface size');
  for(const [index,expected] of [[1,.9],[2,.8],[3,.95],[4,.85],[0,1]]){
    await cmd({type:'settings.set',id:'uiScale',index});await host.wait(()=>settings().then(s=>s.UiScale===expected),'existing and new scale commands');
    await host.wait(()=>percent('Taille de l’interface').then(value=>value===Math.round(expected*100)),'interface scale rendered');
  }
  await key('Taille de l’interface','ArrowLeft',37);await host.wait(()=>settings().then(s=>s.UiScale===.95),'interface 95 before restart');
  for(const width of [320,280]){
    await popup.call('Emulation.setDeviceMetricsOverride',{width,height:650,deviceScaleFactor:1,mobile:false});
    assert.equal(await popup.evaluate(`(()=>{const h=document.querySelector(${JSON.stringify(slider('Taille de l’interface'))}),r=h.parentElement.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth+1&&document.documentElement.scrollWidth<=innerWidth+1})()`),true,'slider fits narrow settings');
  }
  await popup.call('Emulation.clearDeviceMetricsOverride');
  await popup.call('Page.captureScreenshot',{format:'png'}).then(r=>writeFile(resolve(host.base,'interface-slider.png'),Buffer.from(r.data,'base64')));
  await host.close(sidebar);sidebar=null;sidebar=await host.launch();page=await host.connect(host.origin+'/2');await open();
  await zoom(105);await section('Apparence');assert.equal(await percent('Taille de l’interface'),95,'interface size survives restart');
  await section('Général');await popup.call('Page.captureScreenshot',{format:'png'}).then(r=>writeFile(resolve(host.base,'page-zoom-slider.png'),Buffer.from(r.data,'base64')));
  console.log(JSON.stringify({passed:'all page zoom steps 50–200 by 5; beUI keyboard sliders; interface 80/85/90/95/100; native/web scale agreement; independent zoom and persistence',evidence:host.base}));
}finally{
  if(sidebar)await host.close(sidebar).catch(()=>{});
  await host.dispose();assert.equal(await access(resolve(host.base,'profiles')).then(()=>true,()=>false),false,'test profiles and caches removed');
}
