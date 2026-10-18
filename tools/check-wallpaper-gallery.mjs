import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';
const host=await sidebarHost(process.argv[2]);let sidebar,popup;
const cmd=c=>sidebar.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const wait=expression=>host.wait(()=>popup.evaluate(expression).catch(()=>false),expression);
try{
 sidebar=await host.launch(620);await cmd({type:'settings.set',id:'wallpaper',index:0});await cmd({type:'settings.set',id:'startWallpaper',index:1});
 await cmd({type:'settings.open',x:980,y:65});popup=await host.connect('#popup');await wait('!!document.querySelector(".settings-popup")');
 await popup.evaluate(`[...document.querySelectorAll('.settings-nav [role=tab]')].find(b=>b.textContent.includes('Apparence')).click()`);
 await wait('!!document.querySelector(".wallpaper-settings")');
 await popup.evaluate(`document.querySelector('.wallpaper-settings [role=tab]').click()`);
 await popup.evaluate(`document.querySelector('.wallpaper-settings .pill-action').click()`);await wait('!!document.querySelector(".wallpaper-grid")');
 assert.equal(await popup.evaluate('document.querySelectorAll(".wallpaper-tile").length'),73);
 await popup.evaluate(`document.querySelector('.wallpaper-tile img[src*="/102/"]').closest('button').click()`);
 await host.wait(()=>readFile(resolve(host.profile,'settings.json'),'utf8').then(s=>JSON.parse(s).StartWallpaperPhoto===102),'photo selection persisted');
 for(const width of [500,320]){
  await popup.call('Emulation.setDeviceMetricsOverride',{width,height:600,deviceScaleFactor:1,mobile:false});
  assert.ok(await popup.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),'gallery fits '+width);
  assert.ok(await popup.evaluate('[...document.querySelectorAll(".wallpaper-tile")].every(el=>el.getBoundingClientRect().width>30)'),'thumbnails remain usable');
 }
 const shot=await popup.call('Page.captureScreenshot',{format:'png'});await writeFile(resolve(host.base,'wallpapers-small.png'),Buffer.from(shot.data,'base64'));
 console.log(JSON.stringify({passed:'72 credited photos, persisted selection, beUI tabs and responsive gallery',evidence:host.base}));
}finally{if(sidebar)await host.close(sidebar).catch(()=>{});await host.dispose()}
