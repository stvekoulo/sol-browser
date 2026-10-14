import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';
import {menuPaths} from '../ui/src/PopupIcons.ts';

const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
let sidebar,popup;
const cmd=(ui,c)=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const settings=async()=>JSON.parse(await readFile(resolve(host.profile,'settings.json'),'utf8'));
const click=async(selector)=>{
  const point=await popup.evaluate(`(()=>{const el=document.querySelector(${JSON.stringify(selector)});el.scrollIntoView({block:'nearest'});const r=el.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
  await popup.call('Input.dispatchMouseEvent',{type:'mousePressed',...point,button:'left',buttons:1,clickCount:1});
  await popup.call('Input.dispatchMouseEvent',{type:'mouseReleased',...point,button:'left',buttons:0,clickCount:1});
};
const press=async(name,virtual)=>{
  await popup.call('Input.dispatchKeyEvent',{type:'keyDown',key:name,code:name,windowsVirtualKeyCode:virtual,...(name==='Enter'?{text:'\r',unmodifiedText:'\r'}:{})});
  await popup.call('Input.dispatchKeyEvent',{type:'keyUp',key:name,code:name,windowsVirtualKeyCode:virtual});
};
const section=async(name)=>{
  await popup.evaluate(`[...document.querySelectorAll('.settings-nav [role=tab]')].find(b=>b.textContent.includes(${JSON.stringify(name)})).click()`);
  await host.wait(()=>popup.evaluate(`!![...document.querySelectorAll('.settings-nav [role=tab]')].find(b=>b.textContent.includes(${JSON.stringify(name)})&&b.getAttribute('aria-selected')==='true')`),name);
};
const openSettings=async()=>{
  await cmd(sidebar,{type:'popup.close'});await cmd(sidebar,{type:'settings.open',x:980,y:65});
  popup??=await host.connect('#popup');
  await host.wait(()=>popup.evaluate('!!document.querySelector(".settings-popup")').catch(()=>false),'settings rendered');
  await delay(220);
};
const shot=async(name)=>{await delay(250);const r=await popup.call('Page.captureScreenshot',{format:'png'});await writeFile(resolve(host.base,`${name}.png`),Buffer.from(r.data,'base64'));};
const bounds=async(id)=>{
  await delay(350);
  const b=await popup.evaluate(`(()=>{const el=document.querySelector('[data-sol-select="${id}"] [role="listbox"]'),r=el.getBoundingClientRect();return {top:r.top,bottom:r.bottom,left:r.left,right:r.right,height:r.height,width:innerWidth,viewport:innerHeight,hidden:el.getAttribute('aria-hidden'),opacity:getComputedStyle(el).opacity}})()`);
  assert.equal(b.hidden,'false');assert(Number(b.opacity)>.99);assert(b.height>30);
  assert(b.top>=-1&&b.bottom<=b.viewport+1&&b.left>=-1&&b.right<=b.width+1,JSON.stringify(b));
  assert.equal(await popup.evaluate(`(()=>{const el=document.querySelector('[data-sol-select="${id}"] [role="listbox"]'),r=el.getBoundingClientRect();return document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)?.closest('[role="listbox"]')===el})()`),true,'list painted above other settings');
};
try{
  sidebar=await host.launch();await openSettings();
  const toggle='[data-setting="memory"]';
  assert.equal(await popup.evaluate(`!!document.querySelector('${toggle} [role="switch"]')`),true);
  const before=await popup.evaluate(`document.querySelector('${toggle} [role="switch"]').getAttribute('aria-checked')==='true'`);
  await click(`${toggle} .setting-text`);
  await host.wait(()=>settings().then(s=>s.MemorySaver!==before),'label toggles persisted setting');
  await delay(200);assert.equal((await settings()).MemorySaver,!before);
  await click(`${toggle} [role="switch"]`);
  await host.wait(()=>settings().then(s=>s.MemorySaver===before),'switch button toggles once');
  await click('[data-sol-select="engine"] [aria-haspopup="listbox"]');await bounds('engine');
  await press('End',35);await press('Enter',13);
  await host.wait(()=>settings().then(s=>s.SearchUrlTemplate.startsWith('https://www.bing.com/')),'engine changed');
  await host.wait(()=>popup.evaluate('document.activeElement.getAttribute("aria-haspopup")==="listbox"'),'focus returned to select trigger');
  await click('[data-sol-select="engine"] [aria-haspopup="listbox"]');await press('Escape',27);
  assert.equal(await popup.evaluate('document.querySelector("[data-sol-select=engine] [role=listbox]").getAttribute("aria-hidden")'),'true');
  assert.equal(await popup.evaluate('!!document.querySelector(".settings-popup")'),true);
  await section('Apparence');await click('[data-sol-select="font"] [aria-haspopup="listbox"]');await bounds('font');
  await shot('settings-font-light');
  await press('End',35);await press('Enter',13);
  await host.wait(()=>settings().then(s=>s.Font==='lora'),'font selection');
  await host.wait(()=>popup.evaluate('document.querySelector("[data-sol-select=font] [aria-haspopup=listbox]").textContent.includes("Lora")'),'font label updated');
  await click('[data-sol-select="font"] [aria-haspopup="listbox"]');
  await click('.popup-header-title');
  await host.wait(()=>popup.evaluate('document.querySelector("[data-sol-select=font] [role=listbox]").getAttribute("aria-hidden")==="true"'),'outside closes select');
  await cmd(sidebar,{type:'settings.set',id:'theme',index:2});await delay(200);
  await click('[data-sol-select="font"] [aria-haspopup="listbox"]');await bounds('font');await shot('settings-font-dark');await press('Escape',27);
  for(const index of [0,1,2]){
    await cmd(sidebar,{type:'settings.set',id:'uiScale',index});await delay(200);
    await click('[data-sol-select="font"] [aria-haspopup="listbox"]');await bounds('font');await press('Escape',27);
  }
  await cmd(sidebar,{type:'settings.set',id:'uiScale',index:0});
  // A short native window must keep settings scrollable and selectors inside it.
  await cmd(sidebar,{type:'popup.close'});await host.close(sidebar);sidebar=undefined;popup=undefined;
  sidebar=await host.launch(500);await openSettings();await section('Apparence');
  await click('[data-sol-select="font"] [aria-haspopup="listbox"]');await bounds('font');await shot('settings-small');await press('Escape',27);
  const scrolled=await popup.evaluate('document.querySelector("#root").scrollTop');
  await popup.call('Input.dispatchMouseEvent',{type:'mouseWheel',x:100,y:160,deltaY:350,deltaX:0});await delay(200);
  assert.notEqual(await popup.evaluate('document.querySelector("#root").scrollTop'),scrolled,'settings mouse scroll');
  await cmd(sidebar,{type:'popup.close'});
  const s=await sidebar.evaluate('window.__sidebarState'),group=s.groups[0];
  await cmd(sidebar,{type:'group.menu',id:group.id,x:180,y:60});
  await host.wait(()=>popup.evaluate('!!document.querySelector(".beui-menu-popup [role=menu]")'),'embedded menu');await delay(300);
  await host.wait(()=>popup.evaluate('Number(getComputedStyle(document.querySelector("[role=menu]")).opacity)>.99'),'menu visible');
  assert.equal(await popup.evaluate('document.activeElement.getAttribute("role")'),'menuitem');
  assert.equal(await popup.evaluate(`document.querySelector('.sol-menu-item [data-icon]')?.getAttribute('data-icon')`),'tab','new tab uses an animated tab icon');
  assert.equal(await popup.evaluate(`[...document.querySelectorAll('.sol-menu-item')].find(b=>b.textContent.includes('Modifier le groupe'))?.querySelector('[data-icon]')?.getAttribute('data-icon')`),'edit','edit group uses an animated pencil');
  assert.equal(await popup.evaluate(`[...document.querySelectorAll('.sol-menu-item')].find(b=>b.textContent.includes('en veille'))?.querySelector('[data-icon]')?.getAttribute('data-icon')`),'sleep','sleep group uses an animated moon');
  await press('End',35);assert.equal(await popup.evaluate('document.activeElement.textContent.includes("Fermer le groupe")'),true);
  await press('Home',36);await press('ArrowDown',40);await press('ArrowDown',40);
  assert.equal(await popup.evaluate('document.activeElement.textContent.includes("Modifier le groupe")'),true);
  await shot('group-menu');await press('Enter',13);
  await host.wait(()=>popup.evaluate('!!document.querySelector(".group-editor")'),'menu opens editor without second close');
  await delay(200);assert.equal(await popup.evaluate('document.querySelector(".group-editor input").value'),group.name);
  await cmd(sidebar,{type:'popup.close'});
  await popup.call('Emulation.setEmulatedMedia',{features:[{name:'prefers-reduced-motion',value:'reduce'}]});
  await cmd(sidebar,{type:'group.menu',id:group.id,x:180,y:60});await delay(250);
  await host.wait(()=>popup.evaluate('Number(getComputedStyle(document.querySelector("[role=menu]")).opacity)>.99'),'reduced-motion menu visible');
  await press('Escape',27);
  const tab=s.tabs.find(t=>t.url===host.origin+'/2');await cmd(sidebar,{type:'tab.activate',id:tab.id});
  const page=await host.connect(tab.url);
  await host.wait(()=>page.evaluate('document.readyState==="complete"').catch(()=>false),'site page');
  await page.call('Input.dispatchMouseEvent',{type:'mousePressed',x:130,y:140,button:'right',buttons:2,clickCount:1});
  await page.call('Input.dispatchMouseEvent',{type:'mouseReleased',x:130,y:140,button:'right',buttons:0,clickCount:1});
  await host.wait(()=>popup.evaluate('!!document.querySelector(".sol-menu-item")'),'page menu');
  const pageIcons=await popup.evaluate('[...document.querySelectorAll(".sol-menu-item")].map(b=>({label:b.textContent,icon:b.querySelector("[data-icon]")?.getAttribute("data-icon"),path:b.querySelector("svg path")?.getAttribute("d")}))');
  for(const item of pageIcons){if(/enregistrer|save .*as/i.test(item.label))assert.equal(item.icon,'save',item.label);if(/imprimer|print/i.test(item.label))assert.equal(item.path,menuPaths.print,item.label);}
  const menuPick=async(text)=>{
    await host.wait(()=>popup.evaluate(`[...document.querySelectorAll('[role=menuitem]')].some(b=>b.textContent.includes(${JSON.stringify(text)}))`),'native menu '+text);
    await popup.evaluate(`[...document.querySelectorAll('[role=menuitem]')].find(b=>b.textContent.includes(${JSON.stringify(text)})).dispatchEvent(new MouseEvent('mousedown',{bubbles:true,button:0}))`);
  };
  await menuPick('Voir en mobile');await menuPick('Choisir les écrans à comparer');
  await host.wait(()=>popup.evaluate('!!document.querySelector("[role=checkbox]")'),'native comparison checkboxes');
  await delay(300);
  const checked=()=>popup.evaluate('document.querySelectorAll("[role=checkbox][aria-checked=true]").length');
  while(await checked()>1)await click('[role=checkbox][aria-checked=true]');
  assert.equal(await popup.evaluate('document.querySelector(".name-form-actions button[type=submit]").disabled'),true);
  while(await checked()<4)await click('[role=checkbox][aria-checked=false]:not(:disabled)');
  assert.equal(await popup.evaluate('[...document.querySelectorAll("[role=checkbox][aria-checked=false]")].every(b=>b.disabled)'),true);
  await shot('comparison-checkboxes');
  await click('.name-form-actions button[type=submit]');
  await host.wait(()=>settings().then(s=>s.CompareDevices.length===4),'comparison selection persisted');
  await cmd(sidebar,{type:'device.set',id:tab.id,query:''});
  assert((await readFile('ui/dist/licenses/third-party.txt','utf8')).includes('Saurabh Chauhan'));
  console.log(JSON.stringify({passed:'beUI native switches and persistence; engine/font selection, keyboard, focus and dismissal; three scales; light/dark; short window and mouse scrolling; native context-menu keyboard, submenus and editor transition; comparison checkboxes, min/max and persistence; reduced motion; delivered MIT notice',evidence:host.base}));
}catch(error){if(popup)console.error('POPUP',await popup.evaluate('({text:document.body.innerText,focus:document.activeElement.tagName,size:[innerWidth,innerHeight],root:document.querySelector("#root").scrollTop})').catch(()=>null));throw error;}
finally{if(sidebar)await host.close(sidebar).catch(()=>{});await host.dispose();}
