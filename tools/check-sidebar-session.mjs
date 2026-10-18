import assert from 'node:assert/strict';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';
const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');let ui;
try{
 ui=await host.launch(650);
 for(const index of [0,1,2]){
  await ui.evaluate(`chrome.webview.postMessage(JSON.stringify({type:'settings.set',id:'uiScale',index:${index}}))`);await delay(150);
  for(const width of [300,220,180]){
   await ui.call('Emulation.setDeviceMetricsOverride',{width:width+400,height:650,deviceScaleFactor:1,mobile:false});
   const bounds=await ui.evaluate(`(()=>{const b=document.querySelector('[data-action="session.menu"]'),r=b.getBoundingClientRect(),icon=b.querySelector('.sidebar-session-symbol').getBoundingClientRect(),name=b.querySelector('.sidebar-session-name').getBoundingClientRect(),arrow=b.lastElementChild.getBoundingClientRect();return {left:r.left,right:r.right,bottom:r.bottom,viewport:innerWidth,height:innerHeight,label:b.getAttribute('aria-label'),icon:!!b.querySelector('.sol-animated-icon'),aligned:icon.right<name.left&&name.right<arrow.left&&arrow.right<r.right}})()`);
   assert.ok(bounds.left>=0&&bounds.right<=bounds.viewport+1&&bounds.bottom<100,JSON.stringify(bounds));
   assert.ok(bounds.label.startsWith('Session :')&&bounds.icon,'session is an accessible animated action at the top');
   assert.ok(bounds.aligned,'icon, label and chevron remain in separate aligned columns');
  }
 }
 await ui.call('Emulation.clearDeviceMetricsOverride');
 assert.ok(await ui.evaluate(`!!document.querySelector('.sidebar-quick-actions [data-action="window.private"] [data-icon=private]')&&!document.querySelector('.sidebar-quick-actions [data-action="profile.menu"]')`),'footer has the animated private action');
 assert.equal(await ui.evaluate(`(()=>{const channel=chrome.webview,post=channel.postMessage.bind(channel);let command;channel.postMessage=s=>{const c=JSON.parse(s);if(c.type==='window.private')command=c.type;else post(s)};try{document.querySelector('.private-action').click()}finally{channel.postMessage=post}return command})()`),'window.private','private action sends the existing native command');
 await ui.evaluate(`document.querySelector('[data-action="session.menu"]').click()`);const popup=await host.connect('#popup');
 await host.wait(()=>popup.evaluate('!!document.querySelector(".sessions-popup")').catch(()=>false),'session menu opens');
 assert.equal(await popup.evaluate('document.querySelector(".sessions-popup").getAttribute("role")'),'dialog');
 assert.equal(await popup.evaluate('document.querySelectorAll(".session-entry.current").length'),1);
 assert.ok(await popup.evaluate('!!document.querySelector(".session-entry.current [data-icon=check]")'),'selected session is marked clearly');
 await delay(350);
 const point=await popup.evaluate('(()=>{const r=document.querySelector(".session-create").getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()');
 await popup.call('Input.dispatchMouseEvent',{type:'mouseMoved',...point});await delay(200);
 assert.ok(await popup.evaluate('(()=>{const s=getComputedStyle(document.querySelector(".session-create"));return parseFloat(s.borderTopLeftRadius)>=10&&s.borderTopLeftRadius===s.borderBottomLeftRadius&&s.borderTopStyle===s.borderBottomStyle&&s.boxShadow==="none"})()'),'new session keeps a regular rounded border while hovered');
 console.log(JSON.stringify({passed:'compact session selector aligned at three widths and UI scales; original session panel and animated icons; selected session clearly marked',evidence:host.base}));
}finally{if(ui)await host.close(ui).catch(()=>{});await host.dispose()}
