import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
const settingsFile=resolve(host.profile,'settings.json'),settings=JSON.parse(await readFile(settingsFile,'utf8'));
settings.HomePage='https://start.sol/index.html#start';await writeFile(settingsFile,JSON.stringify(settings));
let sidebar,page,top;
const cmd=(ui,c)=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const press=async(ui,key,virtual,modifiers=0)=>{
  await ui.call('Input.dispatchKeyEvent',{type:'keyDown',key,code:key,windowsVirtualKeyCode:virtual,modifiers,...(key==='Enter'?{text:'\r',unmodifiedText:'\r'}:{})});
  await ui.call('Input.dispatchKeyEvent',{type:'keyUp',key,code:key,windowsVirtualKeyCode:virtual,modifiers});
};
const click=async(ui,selector)=>{
  const point=await ui.evaluate(`(()=>{const r=document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
  for(const type of ['mousePressed','mouseReleased'])await ui.call('Input.dispatchMouseEvent',{type,...point,button:'left',buttons:type==='mousePressed'?1:0,clickCount:1});
};
try{
  sidebar=await host.launch();top=await host.connect('');const popup=await host.connect('#popup');
  await top.evaluate('window.__topState=null;window.__topErrors=[];chrome.webview.addEventListener("message",e=>{if(e.data.tabs)window.__topState=e.data});window.addEventListener("error",e=>window.__topErrors.push(e.message))');
  for(const [where,value,pick] of [['sidebar','/notes','enter'],['top',':notes','enter'],['sidebar','/notes','keyboard'],['top','/notes','click']]){
    const before=await sidebar.evaluate('window.__sidebarState.tabs.map(t=>t.id)');await cmd(sidebar,{type:'tab.open'});
    const id=await host.wait(()=>sidebar.evaluate(`!${JSON.stringify(before)}.includes(window.__sidebarState.activeId)&&window.__sidebarState.activeId`),'fresh tab');
    await host.wait(()=>top.evaluate(`document.querySelector('.address-pill')?.dataset.tabId==='${id}'`),'address ready');
    const home=await host.connect(settings.HomePage);await host.wait(()=>home.evaluate('!!document.querySelector(".start-page")').catch(()=>false),'real Sol home');
    page=home;
    await home.evaluate('document.body.style.backgroundImage="linear-gradient(red,blue)";localStorage.setItem("sol.notes.editor","source");window.__notes=null;chrome.webview.addEventListener("message",e=>{if(e.data.popup==="notes")window.__notes=e.data})');
    const ui=where==='sidebar'?sidebar:top,selector=where==='sidebar'?'.sidebar-url-form input':'.address-form input';
    await click(ui,selector);await press(ui,'a',65,2);await ui.call('Input.insertText',{text:value});
    await host.wait(()=>popup.evaluate('document.querySelector("[role=option]")?.textContent.includes("Notes")').catch(()=>false),'Notes suggestion');
    if(pick==='keyboard'){
      await press(ui,'ArrowDown',40);
      await host.wait(()=>ui.evaluate(`document.querySelector(${JSON.stringify(selector)}).value==='sol://notes'`),'Notes suggestion has a clean address');
    }
    if(pick==='click')await click(popup,'[role=option]');else await press(ui,'Enter',13);
    await host.wait(()=>sidebar.evaluate(`window.__sidebarState.tabs.find(t=>t.id==='${id}')?.url==='https://start.sol/index.html#notes'`),'Notes URL');
    await host.wait(()=>home.evaluate('!!document.querySelector(".notes-workspace")&&!document.querySelector(".start-page")'),'Notes replaces home in same document',5000);
    assert(await home.evaluate('document.body.classList.contains("notes-body")&&!document.body.classList.contains("start-body")'),'Notes page styling');
    assert.equal(await home.evaluate('document.body.style.backgroundImage'),'','Notes does not keep home wallpaper');
    if(pick==='enter'&&where==='sidebar'){
      await click(home,'.notes-welcome button');await host.wait(()=>home.evaluate('!!document.querySelector(".notes-writing")'),'source editor');
      await click(home,'.notes-writing');await home.call('Input.insertText',{text:'Un texte conservé dans le même onglet.'});
      await host.wait(()=>home.evaluate('window.__notes?.items.some(n=>n.text==="Un texte conservé dans le même onglet.")'),'note autosaved');
    }
    const shot=await home.call('Page.captureScreenshot',{format:'png'});await writeFile(resolve(host.base,`notes-${where}-${pick}.png`),Buffer.from(shot.data,'base64'));
    await cmd(sidebar,{type:'nav.back',id});await host.wait(()=>home.evaluate('!!document.querySelector(".start-page")&&!document.querySelector(".notes-workspace")'),'Back restores home');
    await host.wait(()=>sidebar.evaluate(`window.__sidebarState.tabs.find(t=>t.id==='${id}')?.canGoForward`),'Forward becomes available');
    assert(await home.evaluate('document.body.classList.contains("start-body")&&!document.body.classList.contains("notes-body")'),'home page styling restored');
    await host.wait(()=>home.evaluate('document.body.style.zoom===""'),'home has its original page scale');
    await cmd(sidebar,{type:'nav.forward',id});await host.wait(()=>home.evaluate('!!document.querySelector(".notes-workspace")'),'Forward restores notes');
    await host.wait(()=>home.evaluate('document.querySelector(".notes-writing")?.value==="Un texte conservé dans le même onglet."'),'note content survives history navigation');
    await cmd(sidebar,{type:'tab.close',id});
    await host.wait(()=>sidebar.evaluate(`window.__sidebarState.tabs.every(t=>t.id!=='${id}')`),'tab fully closed');
  }
  const remoteTab=await sidebar.evaluate('window.__sidebarState.tabs.find(t=>t.id===window.__sidebarState.activeId)');
  const remote=await host.connect(remoteTab.url);
  await remote.evaluate('window.__internalReplies=[];chrome.webview.addEventListener("message",e=>{if(e.data.popup==="notes"||e.data.event==="start")window.__internalReplies.push(e.data)});chrome.webview.postMessage(JSON.stringify({type:"notes.ready"}));chrome.webview.postMessage(JSON.stringify({type:"start.ready"}))');
  await delay(250);assert.equal(await remote.evaluate('window.__internalReplies.length'),0,'remote sites cannot request internal page data');
  console.log(JSON.stringify({passed:'native Notes aliases from Sol home, sidebar/top, Enter/keyboard/click suggestions, Back/Forward, saved content, page styles and remote-origin isolation',evidence:host.base}));
}catch(error){console.error('NAVIGATION',await sidebar?.evaluate('window.__sidebarState.tabs.find(t=>t.id===window.__sidebarState.activeId)').catch(()=>null));console.error('TOP',await top?.evaluate('({address:[...document.querySelectorAll(".address-pill")].map(el=>el.dataset.tabId),state:window.__topState?.activeId,errors:window.__topErrors,root:document.querySelector("#root").innerHTML.slice(0,80)})').catch(()=>null));console.error('PAGE',await page?.evaluate('({url:location.href,body:document.body.className,root:document.querySelector("#root").innerHTML.slice(0,180)})').catch(()=>null));throw error;}
finally{if(sidebar)await host.close(sidebar).catch(()=>{});await host.dispose();}
