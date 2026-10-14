import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
const mode=process.argv[3]??'all';
await writeFile(resolve(host.profile,'notes.json'),JSON.stringify([{Id:'focus',Title:'Une note entière',Text:'Un texte conservé pendant le changement de vue.',Url:'',Updated:new Date().toISOString()}]));
let sidebar,page,popup;
const cmd=(target,value)=>target.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(value))})`);
const pointer=async(target,selector,type='mouseMoved')=>{
  const point=await target.evaluate(`(()=>{const r=document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
  await target.call('Input.dispatchMouseEvent',{type,...point,button:type==='mouseMoved'?'none':'left',buttons:type==='mousePressed'?1:0,clickCount:type==='mouseMoved'?0:1});
};
const click=async(target,selector)=>{await pointer(target,selector,'mousePressed');await pointer(target,selector,'mouseReleased');};
const shot=async(target,name)=>{const image=await target.call('Page.captureScreenshot',{format:'png'});await writeFile(resolve(host.base,`${name}.png`),Buffer.from(image.data,'base64'));};
try{
  sidebar=await host.launch();
  if(mode!=='menu'){
    await cmd(sidebar,{type:'notes.manage'});page=await host.connect('https://start.sol/index.html#notes');
    await host.wait(()=>page.evaluate('!!document.querySelector(".notes-preview")').catch(()=>false),'note loaded');
    await click(page,'.notes-preview');await host.wait(()=>page.evaluate('!!document.querySelector(".notes-document")'),'editor loaded');
    const focus='.sol-note-actions button[aria-label="Mode concentration"]';
    for(const width of [1366,960,390]){
      await page.call('Emulation.setDeviceMetricsOverride',{width,height:768,deviceScaleFactor:1,mobile:false});
      await click(page,focus);await host.wait(()=>page.evaluate('document.querySelector(".notes-workspace").classList.contains("note-focus")'),'focus mode');
      await delay(250);
      const size=await page.evaluate('(()=>{const workspace=document.querySelector(".notes-workspace").getBoundingClientRect(),editor=document.querySelector(".notes-document").getBoundingClientRect(),title=document.querySelector(".notes-title").getBoundingClientRect();return {workspace:workspace.width,editor:editor.width,left:editor.left,title:title.width,overflow:document.documentElement.scrollWidth>innerWidth+1}})()');
      await shot(page,`focus-${width}`);
      assert(size.editor>=size.workspace-2,`focus occupies full width at ${width}: ${JSON.stringify(size)}`);
      assert(size.title>Math.min(250,width/2),'title keeps usable width');assert(!size.overflow,'focus fits viewport');
      assert.equal(await page.evaluate('document.querySelector(".notes-title").value'),'Une note entière');
      await click(page,focus);await host.wait(()=>page.evaluate('!document.querySelector(".notes-workspace").classList.contains("note-focus")'),'focus exit');
      if(width>800)assert(await page.evaluate('getComputedStyle(document.querySelector(".notes-collection")).display!=="none"'),'collection restored');
    }
  }
  if(mode!=='focus'){
    const group=await sidebar.evaluate('window.__sidebarState.groups[0].id');
    for(const index of [1,2]){
      await cmd(sidebar,{type:'settings.set',id:'theme',index});await cmd(sidebar,{type:'group.menu',id:group,x:180,y:60});
      popup??=await host.connect('#popup');await host.wait(()=>popup.evaluate('!!document.querySelector(".sol-menu-item")').catch(()=>false),'menu opened');
      await delay(300);await pointer(popup,'.sol-menu-item');
      assert.equal(await popup.evaluate('getComputedStyle(document.activeElement).outlineStyle'),'none','hover has no blue outline');
      await popup.call('Input.dispatchKeyEvent',{type:'keyDown',key:'ArrowDown',code:'ArrowDown',windowsVirtualKeyCode:40});
      await popup.call('Input.dispatchKeyEvent',{type:'keyUp',key:'ArrowDown',code:'ArrowDown',windowsVirtualKeyCode:40});
      assert(await popup.evaluate('document.activeElement.matches(".sol-menu-item:focus-visible")'),'keyboard selection visible');
      assert.equal(await popup.evaluate('getComputedStyle(document.activeElement).outlineStyle'),'none','keyboard has no blue outline');
      assert.notEqual(await popup.evaluate('getComputedStyle(document.activeElement).boxShadow'),'none','keyboard focus keeps neutral marker');
      await shot(popup,`menu-${index}`);await cmd(sidebar,{type:'popup.close'});
    }
  }
  console.log(JSON.stringify({passed:mode,evidence:host.base}));
}finally{if(sidebar)await host.close(sidebar).catch(()=>{});await host.dispose();}
