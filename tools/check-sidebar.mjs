// Real WinUI/WebView2 checks in a disposable profile. No installed Sol or personal session is touched.
import assert from 'node:assert/strict';
import { writeFile, readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { sidebarHost } from './sidebar-check-host.mjs';

const host = await sidebarHost(process.argv[2] ?? 'bin/sol-sidebar-preview/Sol.exe');
let ui;
const state = () => ui.evaluate('window.__sidebarState');
const command = c => ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const click = selector => ui.evaluate(`document.querySelector(${JSON.stringify(selector)}).click()`);
const box = (selector, fraction=.5) => ui.evaluate(`(()=>{const el=document.querySelector(${JSON.stringify(selector)});el.scrollIntoView({block:'nearest'});const r=el.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height*${fraction}}})()`);
const mouse = (type, point, buttons=0) => ui.call('Input.dispatchMouseEvent',{type,...point,button:type==='mouseMoved'?'none':'left',buttons,clickCount:1});
async function checkPinnedSpacing() {
  const spaced=await ui.evaluate(`(()=>{
    const strip=document.querySelector('.pinned-strip').getBoundingClientRect(),gap=10*Number(getComputedStyle(document.body).zoom);
    const rows=[...document.querySelectorAll('.pinned-row')];
    return rows.every((row,i)=>{
      const boxes=[...row.children].map(el=>el.getBoundingClientRect());
      return (!i||row.getBoundingClientRect().top-rows[i-1].getBoundingClientRect().bottom>=gap-1)
        && boxes.every((r,j)=>r.left>=strip.left-1&&r.right<=strip.right+1&&(!j||
          (Math.abs(r.top-boxes[j-1].top)<1?r.left-boxes[j-1].right:r.top-boxes[j-1].bottom)>=gap-1));
    });
  })()`);
  assert.ok(spaced,'pinned items have breathing room horizontally and vertically without overflowing');
  assert.equal(await ui.evaluate('document.querySelectorAll(".sidebar-organizer,.sidebar-search").length'),0,'sidebar search and organizer buttons are removed');
}
async function checkIntegratedSurface() {
  const surface=await ui.evaluate(`(()=>{
    const panel=document.querySelector('.sidebar-card'),r=panel.getBoundingClientRect(),s=getComputedStyle(panel);
    return {left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:innerWidth,height:innerHeight,zoom:Number(getComputedStyle(document.body).zoom),radius:s.borderRadius,shadow:s.boxShadow,slot:panel.closest('[data-slot="sidebar"]').dataset.slot,inset:document.querySelector('.workspace-page').getBoundingClientRect().left};
  })()`);
  assert.ok(Math.abs(surface.left)<1&&Math.abs(surface.top)<1&&Math.abs(surface.right-surface.inset)<1&&Math.abs(surface.bottom-surface.height)<1,'sidebar is flush with its native column');
  assert.equal(surface.radius,'0px','sidebar itself is not a floating card');
  assert.equal(await ui.evaluate('document.querySelectorAll(".sidebar-heading,.sidebar-url-pill").length'),0,'shortcuts follow window controls without duplicated toolbar blocks');
  assert.equal(surface.shadow,'none','integrated sidebar has no outer shadow');
  assert.equal(surface.slot,'sidebar','beUI desktop sidebar remains in use');
}
async function drag(source, target, fraction=.5, cancel=false) {
  const a=await box(source);
  await mouse('mousePressed',a,1);
  await mouse('mouseMoved',{x:a.x+7,y:a.y+7},1);
  const b=await box(target,fraction);
  await mouse('mouseMoved',b,1); await delay(120);
  assert.ok(await ui.evaluate('!!document.querySelector(".sidebar-drag-ghost")'), 'visible drag preview');
  if(cancel) await ui.call('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});
  await mouse('mouseReleased',b); await delay(180);
  assert.equal(await ui.evaluate('document.querySelectorAll(".sidebar-drag-ghost").length'),0,'drag cleanup');
}
const slot = id => `[data-kind="tab"][data-id="${id}"]`;
const group = id => `[data-kind="group"][data-id="${id}"]`;
try {
  ui = await host.launch();
  await host.wait(()=>ui.evaluate('document.querySelectorAll(".sidebar-tab").length===10'), 'restored sidebar');
  await checkIntegratedSurface();
  assert.equal(await ui.evaluate('document.querySelectorAll(".sidebar-window-bar .light").length'),3,'window controls belong to the full-height sidebar');
  const toolbar = await host.connect('');
  await host.wait(()=>toolbar.evaluate('!!document.querySelector(".topbar")').catch(()=>false),'toolbar ready');
  for (const surface of [ui, toolbar]) {
    assert.equal(await surface.evaluate(`document.querySelectorAll('[title]:not([title=""])').length`),0,'HTML tooltips cannot duplicate Sol tooltips');
  }
  await ui.evaluate(`window.__tooltips=[];const post=chrome.webview.postMessage.bind(chrome.webview);chrome.webview.postMessage=message=>{const c=JSON.parse(message);if(c.type==='ui.tooltip'&&c.title)window.__tooltips.push(c);post(message)}`);
  const shortcutBox=await box('[data-kind="shortcut"]');
  await mouse('mouseMoved',shortcutBox);
  await host.wait(()=>ui.evaluate('window.__tooltips.length===1'),'shortcut tooltip sent');
  await delay(600);
  assert.equal(await ui.evaluate('window.__tooltips.length'),1,'one tooltip per hover');
  assert.match(await readFile(resolve(host.profile,'debug.log'),'utf8'),/ui.tooltip shown/,'native tooltip shown');
  await mouse('mouseMoved',{x:1,y:1});
  await toolbar.evaluate(`const b=document.createElement('button');b.id='tooltip-check';b.title='Description initiale';b.innerHTML='<svg></svg>';document.body.append(b)`);
  await host.wait(()=>toolbar.evaluate(`document.querySelector('#tooltip-check').getAttribute('aria-label')==='Description initiale'`),'new icon accessible name');
  await toolbar.evaluate(`document.querySelector('#tooltip-check').title='Description mise à jour'`);
  await host.wait(()=>toolbar.evaluate(`document.querySelector('#tooltip-check').getAttribute('data-sol-tooltip')==='Description mise à jour' && document.querySelector('#tooltip-check').title==='' && document.querySelector('#tooltip-check').getAttribute('aria-label')==='Description mise à jour'`),'updated title keeps one tooltip');
  await toolbar.evaluate(`document.querySelector('#tooltip-check').removeAttribute('title')`);
  await host.wait(()=>toolbar.evaluate(`!document.querySelector('#tooltip-check').hasAttribute('data-sol-tooltip') && !document.querySelector('#tooltip-check').hasAttribute('aria-label')`),'removed title does not leave stale tooltip');
  await toolbar.evaluate(`document.querySelector('#tooltip-check').remove()`);
  for (const index of [0,1,2]) {
    await command({type:'settings.set',id:'uiScale',index});
    await host.wait(()=>ui.evaluate(`Number(getComputedStyle(document.body).zoom)===${[.92,.89,.86][index]}`),'requested UI scale applied before dragging');
    const drawerWidth=await ui.evaluate('innerWidth');
    await ui.evaluate(`(()=>{window.__sidebarFrames=[];const record=()=>{window.__sidebarFrames.push(document.querySelector('.workspace-page').getBoundingClientRect().left);if(window.__sidebarFrames.length<40)requestAnimationFrame(record)};requestAnimationFrame(record)})()`);
    await toolbar.evaluate(`chrome.webview.postMessage('{"type":"sidebar.toggle"}');setTimeout(()=>chrome.webview.postMessage('{"type":"sidebar.toggle"}'),70);setTimeout(()=>chrome.webview.postMessage('{"type":"sidebar.toggle"}'),110);setTimeout(()=>chrome.webview.postMessage('{"type":"sidebar.toggle"}'),160)`);
    await host.wait(()=>ui.evaluate('window.__sidebarFrames.length===40'),'sidebar animation frames');
    assert.ok(await ui.evaluate(`window.__sidebarFrames.every(x=>x>=0)&&new Set(window.__sidebarFrames.map(Math.round)).size>5`),'shared inset follows intermediate sidebar frames throughout rapid toggles');
    await host.wait(()=>ui.evaluate(`Math.abs(document.querySelector('.workspace-sidebar').getBoundingClientRect().width-document.querySelector('.workspace-sidebar-content').getBoundingClientRect().width)<1`),'sidebar animation settles after interruption');
    await checkIntegratedSurface();
    assert.ok(await ui.evaluate(`Math.abs(innerWidth-${drawerWidth})<=1`),'chrome viewport remains stable when entering and leaving');
    await checkPinnedSpacing();
    const beforeDrag=(await state()).tabs.map(tab=>tab.id);
    for (const selector of ['.sidebar-tab', '.tab-group-header']) {
      const a=await box(selector);
      const origin=await ui.evaluate(`(()=>{const el=document.querySelector('${selector}'),source=el.closest('.tab-group-heading')??el,r=source.getBoundingClientRect();return {left:r.left,top:r.top,width:r.width,height:r.height,detail:source.querySelector('.favicon,.tab-group-name').getBoundingClientRect().height,group:getComputedStyle(source).getPropertyValue('--group')}})()`);
      const b={x:a.x+60,y:a.y+48};
      await mouse('mousePressed',a,1);await mouse('mouseMoved',{x:a.x+7,y:a.y+7},1);await mouse('mouseMoved',b,1);await delay(100);
      const preview=await ui.evaluate(`(()=>{const el=document.querySelector('.sidebar-drag-ghost'),r=el.getBoundingClientRect(),c=document.querySelector('.sidebar-card').getBoundingClientRect();return {left:r.left,top:r.top,right:r.right,width:r.width,height:r.height,cardLeft:c.left,cardRight:c.right,detail:el.querySelector('.favicon,.tab-group-name').getBoundingClientRect().height,group:getComputedStyle(el).getPropertyValue('--group')}})()`);
      assert.ok(Math.abs(preview.left-origin.left)<1,`vertical drag keeps the original horizontal alignment: ${JSON.stringify({index,selector,origin,preview})}`);
      assert.ok(Math.abs(preview.top-origin.top-48)<1,'drag keeps the pointer anchor without jumping');
      assert.ok(Math.abs(preview.width-origin.width)<1 && Math.abs(preview.height-origin.height)<1,'drag keeps the original size at every UI scale');
      assert.ok(Math.abs(preview.detail-origin.detail)<1,'preview icons and text keep the sidebar scale');
      assert.equal(preview.group,origin.group,'group preview keeps its inherited color');
      assert.ok(preview.left>=preview.cardLeft && preview.right<=preview.cardRight+1,'drag preview stays within sidebar');
      const shot=await ui.call('Page.captureScreenshot',{format:'png'});
      await writeFile(resolve(host.base,`sidebar-drag-${index}-${selector.slice(1)}.png`),Buffer.from(shot.data,'base64'));
      await ui.call('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});await mouse('mouseReleased',b);
    }
    assert.deepEqual((await state()).tabs.map(tab=>tab.id),beforeDrag,'alignment checks do not reorder tabs');
  }
  await command({type:'settings.set',id:'uiScale',index:0});
  const initial=await state(), active=initial.activeId, work=initial.groups[0].id, lecture=initial.groups[1].id;
  await click(`${group(work)} .tab-group-header`);
  await host.wait(async()=>(await state()).groups[0].collapsed, 'collapse group');
  assert.equal((await state()).activeId,active,'collapse keeps active page');
  assert.ok(await ui.evaluate(`document.querySelector('${group(work)}').classList.contains('has-active')`),'collapsed group shows active state');
  await click(`${group(work)} .tab-group-header`);
  const before=(await state()).tabs.map(t=>t.id);
  await drag(slot(before[6]),slot(before[8]),.8,true);
  assert.deepEqual((await state()).tabs.map(t=>t.id),before,'Escape cancels without mutation');
  await drag(slot(before[6]),slot(before[8]),.8);
  await host.wait(async()=>(await state()).tabs[8].id===before[6],'reorder tab on release');
  await drag(`${group(work)} .tab-group-header`,group(lecture),.9);
  await host.wait(async()=>(await state()).tabs[2].groupId===lecture,'move whole group');
  assert.deepEqual((await state()).tabs.filter(t=>t.groupId===work).map(t=>t.id),[before[2],before[3]],'group member order');
  await drag(slot(before[7]),'[data-pin-zone]',.9);
  await host.wait(async()=>(await state()).tabs.find(t=>t.id===before[7]).pinned,'drag to pin');
  await drag(slot(before[7]),slot(before[9]),.8);
  await host.wait(async()=>!(await state()).tabs.find(t=>t.id===before[7]).pinned,'drag to unpin');
  await click(`${group(lecture)} .tab-group-header`);
  await host.wait(()=>ui.evaluate(`document.querySelector('${group(lecture)}').classList.contains('collapsed')`),'collapsed group rendered');
  await drag(slot(before[10]),`${group(lecture)} .tab-group-header`,.5);
  await host.wait(async()=>(await state()).tabs.find(t=>t.id===before[10]).groupId===lecture,'drop into collapsed group');
  await command({type:'group.toggle',id:lecture});
  await host.wait(()=>ui.evaluate(`!document.querySelector('${group(lecture)}').classList.contains('collapsed')`),'group expanded to remove a member');
  await drag(slot(before[10]),'.sidebar-new-tab');
  await host.wait(async()=>(await state()).tabs.find(t=>t.id===before[10]).groupId===null,'drag out of group');
  await drag('[data-kind="shortcut"][data-id="0"]','[data-kind="shortcut"][data-id="2"]');
  await host.wait(async()=>(await state()).shortcuts[2].name==='A','reorder saved shortcuts');
  await click(`${group(work)} .group-add`);
  await host.wait(async()=>(await state()).tabs.length===13,'new tab belongs to group');
  const opened=await state();
  assert.equal(opened.tabs.find(t=>t.id===opened.activeId).groupId,work);
  const groupedPage=await host.connect(`${host.origin}/home`);
  await host.wait(()=>groupedPage.evaluate(`document.readyState==="complete"&&location.href===${JSON.stringify(`${host.origin}/home`)}`),'new group page loaded before opening its menu');
  await click(`${group(work)} .sidebar-item-action:last-child`);
  const popup=await host.connect('#popup');
  await host.wait(()=>popup.evaluate('document.body.textContent.includes("Modifier le groupe")'),'group menu follows Sol popup');
  await command({type:'popup.close'});
  await command({type:'split.open',id:before[8]});
  await host.wait(async()=>(await state()).split.visible,'split pair');
  for(const index of [0,1,2]) {
    await command({type:'settings.set',id:'uiScale',index}); await delay(250);
    const layout=await ui.evaluate(`(()=>{const list=document.querySelector('.sidebar-scroll'),footer=document.querySelector('.sidebar-quick-actions'),card=document.querySelector('.sidebar-card');const r=footer.getBoundingClientRect(),c=card.getBoundingClientRect();return {footer:r.bottom,card:c.bottom,width:list.clientWidth,overflow:list.scrollWidth-list.clientWidth,buttons:[...footer.children].map(e=>{const b=e.getBoundingClientRect();return {left:b.left,right:b.right,top:b.top,bottom:b.bottom}}),left:c.left,right:c.right}})()`);
    assert.ok(layout.footer<=layout.card+1,'footer visible at every scale');
    assert.ok(layout.overflow<=1,'no horizontal scroll');
    assert.ok(layout.buttons.every(b=>b.left>=layout.left && b.right<=layout.right+1),'footer buttons within sidebar');
    assert.equal(layout.buttons.length,6,'profile, notes and split menu available in footer');
  }
  await command({type:'split.close'});
  await command({type:'settings.set',id:'theme',index:2}); await delay(200);
  for(let i=0;i<20;i++) await command({type:'tab.open',url:`${host.origin}/extra-${i}`});
  await host.wait(async()=>(await state()).tabs.length>=33,'many tabs');
  await ui.evaluate('document.querySelector(".sidebar-scroll").scrollTop=0');
  const scroll=await ui.evaluate('(()=>{const el=document.querySelector(".sidebar-scroll");return {height:el.clientHeight,all:el.scrollHeight}})()');
  assert.ok(scroll.all>scroll.height,'tab list scrolls with overflow');
  const firstNormal=(await state()).tabs.find(t=>!t.pinned&&!t.groupId).id;
  const a=await box(slot(firstNormal));
  const edge=await ui.evaluate('(()=>{const r=document.querySelector(".sidebar-scroll").getBoundingClientRect();return {x:r.x+20,y:r.bottom-5}})()');
  await mouse('mousePressed',a,1);await mouse('mouseMoved',{x:a.x+7,y:a.y+7},1);await mouse('mouseMoved',edge,1);await delay(600);
  assert.ok(await ui.evaluate('!!document.querySelector(".sidebar-drag-ghost")'),'autoscroll drag started');
  assert.ok(await ui.evaluate('document.querySelector(".sidebar-scroll").scrollTop>40'),'drag autoscroll');
  await ui.call('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',windowsVirtualKeyCode:27});await mouse('mouseReleased',edge);
  await command({type:'settings.set',id:'uiScale',index:0});
  await ui.evaluate('document.querySelector(".sidebar-scroll").scrollTop=0');
  const screenshot=await ui.call('Page.captureScreenshot',{format:'png'});
  await writeFile(resolve(host.base,'sidebar.png'),Buffer.from(screenshot.data,'base64'));
  await host.close(ui);ui=null;
  const saved=JSON.parse(await readFile(resolve(host.profile,'settings.json'),'utf8'));
  assert.equal(saved.Shortcuts[2].Name,'A','reordered shortcuts persisted');
  ui=await host.launch();
  assert.equal((await state()).shortcuts[2].name,'A','shortcut order restored');
  const resumed=await state();
  assert.equal(resumed.tabs.find(t=>t.url===initial.tabs[7].url).pinned,false,'unpin persisted');
  const normal=(await state()).tabs.find(t=>!t.pinned&&!t.groupId).id;
  await ui.evaluate(`document.querySelector('${slot(normal)}').focus()`);
  await ui.call('Input.dispatchKeyEvent',{type:'keyDown',key:'Enter',code:'Enter',windowsVirtualKeyCode:13});
  await host.wait(async()=>(await state()).activeId===normal,'keyboard activation');
  const mid=await box(slot(normal));
  await ui.call('Input.dispatchMouseEvent',{type:'mousePressed',...mid,button:'middle',buttons:4,clickCount:1});
  await ui.call('Input.dispatchMouseEvent',{type:'mouseReleased',...mid,button:'middle',buttons:0,clickCount:1});
  await host.wait(async()=>!(await state()).tabs.some(t=>t.id===normal),'middle-click closes once');
  await command({type:'tab.activate',id:resumed.tabs.find(t=>t.url===initial.tabs[2].url).id});
  await command({type:'group.toggle',id:work});
  await host.wait(async()=>(await state()).groups.find(g=>g.id===work).collapsed,'collapse active group before save');
  const extraPins=(await state()).tabs.filter(tab=>tab.url.includes('/extra-')).slice(-4);
  for(const tab of extraPins) await command({type:'tab.pin',id:tab.id,index:1});
  await host.wait(async()=>(await state()).tabs.filter(tab=>tab.pinned).length===6,'enough pinned tabs to check wrapping');
  await host.close(ui);ui=null;
  for(const width of [220,520]) {
    const settings=JSON.parse(await readFile(resolve(host.profile,'settings.json'),'utf8'));
    settings.SidebarWidth=width;settings.UiScale=1;settings.Theme='light';
    await writeFile(resolve(host.profile,'settings.json'),JSON.stringify(settings));
    ui=await host.launch(380);
    assert.equal((await state()).groups.find(g=>g.id===work).collapsed,true,'active group collapse restored');
    const restoredSplit=(await state()).tabs.find(tab=>tab.url===initial.tabs[8].url);
    await command({type:'split.open',id:restoredSplit.id});
    await host.wait(async()=>(await state()).split.visible,'split in small window');
    for(const index of [0,1,2]) {
      await command({type:'settings.set',id:'uiScale',index});await delay(180);
      await ui.evaluate('document.querySelector(".sidebar-scroll").scrollTop=0');
      await checkPinnedSpacing();
      const small=await ui.evaluate(`(()=>{const card=document.querySelector('.sidebar-card').getBoundingClientRect(),footer=document.querySelector('.sidebar-quick-actions').getBoundingClientRect(),list=document.querySelector('.sidebar-scroll');return {bottom:footer.bottom,card:card.bottom,height:list.clientHeight,overflow:list.scrollWidth-list.clientWidth,buttons:[...document.querySelectorAll('.sidebar-quick-btn')].every(e=>{const r=e.getBoundingClientRect();return r.left>=card.left && r.right<=card.right+1})}})()`);
      assert.ok(small.bottom<=small.card+1 && small.height>30 && small.buttons,'footer and scroll list fit in small window');
      assert.ok(small.overflow<=1,'wide and narrow lists have no horizontal overflow');
    }
    await command({type:'split.close'});await command({type:'settings.set',id:'uiScale',index:0});
    const smallShot=await ui.call('Page.captureScreenshot',{format:'png'});
    await writeFile(resolve(host.base,`sidebar-small-${width}.png`),Buffer.from(smallShot.data,'base64'));
    await host.close(ui);ui=null;
  }
  console.log(`Sidebar integration passed. Native screenshot: ${resolve(host.base,'sidebar.png')}`);
} catch(error) {
  console.error('Sidebar check failed before cleanup:',error);
  throw error;
} finally {
  if(ui) await host.close(ui);
  await host.dispose();
}
