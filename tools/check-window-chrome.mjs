import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';
const host=await sidebarHost(process.argv[2]);let sidebar,toolbar;
const command=c=>toolbar.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
try{
 sidebar=await host.launch(720);toolbar=await host.connect('');
 await host.wait(()=>toolbar.evaluate('!!document.querySelector(".topbar")').catch(()=>false),'toolbar ready');
 const page=await host.connect(host.origin+'/2');
 assert.equal(await sidebar.evaluate('document.querySelectorAll(".sidebar-window-bar .light").length'),3);
 assert.equal(await toolbar.evaluate('document.querySelectorAll(".topbar .light").length'),0,'no window controls above the sidebar');
 const heights=await Promise.all([sidebar.evaluate('innerHeight'),toolbar.evaluate('innerHeight'),page.evaluate('innerHeight')]);
 assert.equal(heights[0],heights[1],'sidebar and navigation share the same chrome document');
 assert.ok(Math.abs(await toolbar.evaluate('document.querySelector(".workspace-page").getBoundingClientRect().height')-heights[2])<=2,'native page follows the shared document');
 await sidebar.evaluate(`window.__windowActions=[];window.__nativePost=chrome.webview.postMessage.bind(chrome.webview);chrome.webview.postMessage=m=>{const c=JSON.parse(m);if(c.type.startsWith('window.'))window.__windowActions.push(c);else window.__nativePost(m)}`);
 await sidebar.evaluate(`document.querySelector('.light.minimize').click();document.querySelector('.light.maximize').click()`);
 assert.deepEqual(await sidebar.evaluate('window.__windowActions.map(c=>c.type)'),['window.minimize','window.maximize']);
 const point=await sidebar.evaluate(`(()=>{const r=document.querySelector('.sidebar-window-bar').getBoundingClientRect();return {x:r.right-5,y:r.top+r.height/2}})()`);
 await sidebar.call('Input.dispatchMouseEvent',{type:'mousePressed',...point,button:'left',buttons:1,clickCount:1});
 assert.equal(await sidebar.evaluate('window.__windowActions.length'),2,'press alone does not trap the window in a native drag');
 await sidebar.call('Input.dispatchMouseEvent',{type:'mouseMoved',x:point.x+12,y:point.y,buttons:1});
 await host.wait(()=>sidebar.evaluate('window.__windowActions.some(c=>c.type==="window.drag")'),'drag starts after actual movement');
 await sidebar.call('Input.dispatchMouseEvent',{type:'mouseReleased',...point,button:'left',buttons:0,clickCount:1});
 for(const event of ['blur','mouseleave']){
  await sidebar.call('Input.dispatchMouseEvent',{type:'mousePressed',...point,button:'left',buttons:1,clickCount:1});
  await sidebar.evaluate(`window.dispatchEvent(new Event('${event}'))`);
  await sidebar.call('Input.dispatchMouseEvent',{type:'mouseMoved',x:point.x+12,y:point.y,buttons:1});
  assert.equal(await sidebar.evaluate('window.__windowActions.filter(c=>c.type==="window.drag").length'),1,`${event} cancels a pending drag`);
  await sidebar.call('Input.dispatchMouseEvent',{type:'mouseReleased',...point,button:'left',buttons:0,clickCount:1});
 }
 await sidebar.evaluate('chrome.webview.postMessage=window.__nativePost');
 await command({type:'window.drag'});await command({type:'settings.open',x:600,y:50});
 const latePopup=await host.connect('#popup');
 await host.wait(()=>latePopup.evaluate('!!document.querySelector(".settings-popup")').catch(()=>false),'late drag without a held physical button does not block native commands',3000);
 await command({type:'popup.close'});
 await command({type:'sidebar.toggle'});await host.wait(()=>toolbar.evaluate('document.querySelectorAll(".topbar .light").length===3'),'window controls available when sidebar is hidden');
 await command({type:'sidebar.toggle'});await host.wait(()=>toolbar.evaluate('document.querySelectorAll(".topbar .light").length===0'),'window controls return to sidebar');
 for(const width of [760,500,350]){
  await toolbar.call('Emulation.setDeviceMetricsOverride',{width,height:720,deviceScaleFactor:1,mobile:false});await delay(150);
  assert.ok(await toolbar.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),'navigation fits '+width);
 }
 await toolbar.call('Emulation.clearDeviceMetricsOverride');
 const button=await toolbar.evaluate(`(()=>{const r=document.querySelector('[data-action="settings.open"]').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
 await toolbar.call('Input.dispatchMouseEvent',{type:'mousePressed',...button,button:'left',buttons:1,clickCount:1});await toolbar.call('Input.dispatchMouseEvent',{type:'mouseReleased',...button,button:'left',buttons:0,clickCount:1});
 const popup=await host.connect('#popup');await host.wait(()=>popup.evaluate('!!document.querySelector(".settings-popup")').catch(()=>false),'right-toolbar settings menu opens');
 await command({type:'popup.close'});
  await page.evaluate('document.documentElement.requestFullscreen()');
  await host.wait(async()=>await page.evaluate('innerHeight')>=heights[0]-2,'page fills the window in fullscreen');
  await page.evaluate('document.exitFullscreen()');
  await host.wait(async()=>Math.abs(await page.evaluate('innerHeight')-heights[2])<=2,'page geometry restored after fullscreen');
 const shot=await sidebar.call('Page.captureScreenshot',{format:'png'});await writeFile(resolve(host.base,'full-height-sidebar.png'),Buffer.from(shot.data,'base64'));
  console.log(JSON.stringify({passed:'full-height layout; rounded beUI sidebar; window controls and drag threshold; hidden-sidebar fallback; responsive navigation; right-toolbar menus; fullscreen restore',evidence:host.base}));
}finally{if(sidebar)await Promise.race([host.close(sidebar).catch(()=>{}),delay(1500)]);await host.dispose()}
