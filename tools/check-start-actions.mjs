import assert from 'node:assert/strict';
import {DatabaseSync} from 'node:sqlite';
import {access,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const baseline=process.argv.includes('--baseline');
const host=await sidebarHost('src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
let ui;
try {
  ui=await host.launch(850,1200);
  const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
  const favorites=Array.from({length:300},(_,i)=>({url:host.origin+'/favorite-'+i,title:'Favori '+i}));
  // Données en place avant start.ready : une réponse native tardive reste cohérente avec la fixture.
  const db=new DatabaseSync(resolve(host.profile,'browser.db'));
  const addedAt=Date.now();
  try {const add=db.prepare('INSERT INTO bookmarks (url,title,added) VALUES (?,?,?)');db.exec('BEGIN');
      favorites.forEach((site,i)=>add.run(site.url,site.title,addedAt+i));db.exec('COMMIT');}
  finally {db.close();}
  await command({type:'tab.open',url:'https://start.sol/index.html#start'});
  const page=await host.connect('https://start.sol/index.html#start');
  await host.wait(()=>page.evaluate('!!document.querySelector(".start-favorites")'),'initial start document');
  await page.evaluate(`
    const nativeRaf=window.requestAnimationFrame.bind(window),nativeTimer=window.setTimeout.bind(window);
    window.__fanStats={raf:0,timers:0,longTasks:[]};
    window.requestAnimationFrame=(fn)=>{__fanStats.raf++;return nativeRaf(fn)};
    window.setTimeout=(...args)=>{__fanStats.timers++;return nativeTimer(...args)};
    new PerformanceObserver(list=>{for(const e of list.getEntries())__fanStats.longTasks.push(e.duration)}).observe({type:'longtask',buffered:true});
  `);
  await host.wait(()=>page.evaluate('!!document.querySelector(".start-favorites")'),'start ready');
  await page.call('Emulation.setCPUThrottlingRate',{rate:4});
  const data={event:'start',privateMode:false,lang:'fr',wallpaper:false,favorites,history:[],groups:[]};
  await page.evaluate(`window.__fanStats.raf=0;window.__fanStats.timers=0;window.__fanStats.longTasks=[];window.__fanBegin=performance.now();
    window.__fanObserver=new MutationObserver(()=>{if(document.querySelector('.start-site')){window.__fanPaint=performance.now()-__fanBegin;__fanObserver.disconnect()}});
    __fanObserver.observe(document.querySelector('.start-favorites'),{childList:true,subtree:true});
    chrome.webview.dispatchEvent(new MessageEvent('message',{data:${JSON.stringify(data)}}));`);
  await host.wait(()=>page.evaluate('document.querySelectorAll(".start-site").length>10'),'favorites rendered');
  await delay(350);
  const initial=await page.evaluate('({...__fanStats,paintMs:__fanPaint,cards:document.querySelectorAll(".start-favorite").length,buttons:document.querySelectorAll(".nod-act").length})');
  await page.evaluate(`__fanStats.raf=0;__fanStats.timers=0;window.__fanExpand=performance.now();document.querySelector('.start-more').click()`);
  await host.wait(()=>page.evaluate(`document.querySelector(".start-more")?.getAttribute("aria-expanded")==="true"&&document.querySelectorAll(".start-site").length>36`),'all favorites');
  await delay(350);
  const expanded=await page.evaluate('({...__fanStats,elapsedMs:performance.now()-__fanExpand,cards:document.querySelectorAll(".start-favorite").length,buttons:document.querySelectorAll(".nod-act").length})');
  const report={baseline,initial,expanded};
  await writeFile(resolve(host.base,'start-actions.json'),JSON.stringify(report,null,2));
  console.log(JSON.stringify({base:host.base,...report},null,2));
  if(!baseline){
    assert.equal(initial.buttons,0,'closed fans do not mount their buttons');
    assert.equal(expanded.buttons,0,'300 favorites do not mount 900 unused action buttons');
    assert(expanded.cards<100,'only visible favorite rows are mounted');
    assert(initial.raf<12&&expanded.raf<12,'mounting cards starts no per-card animation loops');
    assert(initial.timers<12&&expanded.timers<12,'mounting cards starts no per-card stagger timers');
    await page.evaluate('document.querySelector(".start-favorites").scrollIntoView({block:"end"})');
    await host.wait(()=>page.evaluate(`!!document.querySelector('.start-site[title="${favorites[299].url}"]')`),'last favorite reachable by scrolling');
    await page.evaluate('document.querySelector(".start-more").scrollIntoView({block:"start"})');
    await host.wait(()=>page.evaluate(`!!document.querySelector('.start-site[title="${favorites[0].url}"]')`),'first favorite returns');
    const tile=await page.evaluate(`(()=>{const b=document.querySelector('.start-site');b.scrollIntoView({block:'center'});const r=b.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
    await page.call('Input.dispatchMouseEvent',{type:'mouseMoved',...tile});
    for(const type of ['mousePressed','mouseReleased'])await page.call('Input.dispatchMouseEvent',{type,...tile,button:'right',clickCount:1});
    assert.equal(await page.evaluate('document.querySelectorAll(".nod,.nod-act,sol-fan").length'),0,'start has no fan, including on right click');
    await command({type:'popup.close'});
    await page.evaluate('document.querySelectorAll(".start-site")[1].focus()');
    assert.equal(await page.evaluate('document.querySelectorAll(".nod-act").length'),0,'keyboard focus opens no fan');
    await page.call('Emulation.setDeviceMetricsOverride',{width:360,height:650,deviceScaleFactor:1,mobile:false});
    await host.wait(()=>page.evaluate(`getComputedStyle(document.querySelector('.start-favorites')).gridTemplateColumns.split(' ').length===3&&[...document.querySelectorAll('.start-favorites>.start-favorite')].every(e=>Number(e.style.gridColumn)<=3)`),'narrow columns reflow');
    assert(await page.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),'narrow start page fits');
    await writeFile(resolve(host.base,'start-actions-narrow.png'),Buffer.from((await page.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await page.call('Emulation.clearDeviceMetricsOverride');
    await command({type:'settings.set',id:'theme',index:2});
    await host.wait(()=>page.evaluate('document.documentElement.dataset.theme==="dark"'),'dark theme');
    await writeFile(resolve(host.base,'start-actions-dark.png'),Buffer.from((await page.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await page.evaluate('document.querySelector(".start-site").click()');
    await host.wait(()=>ui.evaluate(`window.__sidebarState.tabs.some(t=>t.url===${JSON.stringify(favorites[0].url)})`),'normal favorite navigation');
    console.log('PASS 300 favorites, virtualization, no fan on hover/right click/focus, normal navigation, narrow layout at 4x CPU slowdown');
  }
  await host.close(ui);
} catch(error) { console.error(error); const target=(await host.targets()).find(t=>t.url.endsWith('#start')); if(target){const p=await host.connect(target.url);console.error(await p.evaluate('({url:location.href,text:document.body.innerText.slice(0,400),scripts:[...document.scripts].map(s=>s.src)})').catch(()=>null));} throw error; } finally {await host.dispose();await assert.rejects(access(host.profile));}
