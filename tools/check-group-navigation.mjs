import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{
  serve:(req,res)=>{
    if(req.url==='/group-redirect'){res.writeHead(302,{Location:'/group-destination'});res.end();return true;}
    res.writeHead(200,{'Content-Type':'text/html'});res.end('<title>Grouped page</title><p>Grouped fixture</p>');return true;
  }
});
let ui;
const state=()=>ui.evaluate('window.__sidebarState');
const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const baseline=process.argv.includes('--layout-only');
const session=JSON.parse(await readFile(resolve(host.profile,'session.json'),'utf8'));
Object.assign(session,{
  Urls:Array.from({length:124},(_,i)=>`${host.origin}/stress-${i}`),
  Titles:Array.from({length:124},(_,i)=>`Un titre très long pour cet onglet ${i}`),
  Icons:[],PinnedCount:4,ActiveIndex:4,
  Groups:Array.from({length:12},(_,g)=>({Name:`Groupe chargé ${g}`,Color:'#8fa8e8',Collapsed:false})),
  GroupOf:Array.from({length:124},(_,i)=>i<4?null:Math.floor((i-4)/10))
});
await writeFile(resolve(host.profile,'session.json'),JSON.stringify(session));
try {
  ui=await host.launch(650,900);
  const original=await state();
  await host.wait(()=>ui.evaluate(`document.querySelectorAll('.tab-group-block').length===12`),'stress groups rendered');
  for(const height of [650,400,800]) {
    await ui.call('Emulation.setDeviceMetricsOverride',{width:900,height,deviceScaleFactor:1,mobile:false});
    await host.wait(()=>ui.evaluate(`(()=>{
      const groups=[...document.querySelectorAll('.tab-group-block')],zoom=Number(getComputedStyle(document.body).zoom);
      return groups.length===12&&groups.every((group,i)=>{
        const heading=group.querySelector('.tab-group-heading').getBoundingClientRect();
        const tabs=[...group.querySelectorAll('.sidebar-tab')].map(e=>e.getBoundingClientRect());
        const r=group.getBoundingClientRect();
        return heading.height>=30*zoom&&tabs.every((tab,j)=>tab.height>=35*zoom&&tab.top>=(j?tabs[j-1].bottom:heading.bottom)-1)
          &&r.bottom>=tabs.at(-1).bottom-1&&(!i||r.top>=groups[i-1].getBoundingClientRect().bottom-1);
      });
    })()`),'groups retain full height without overlapping',5000);
    assert(await ui.evaluate(`document.querySelector('.sidebar-scroll').scrollHeight>document.querySelector('.sidebar-scroll').clientHeight`),'long group list scrolls');
    assert(await ui.evaluate(`document.documentElement.scrollWidth<=innerWidth`),'no horizontal document overflow');
  }
  const firstGroup=original.groups[0].id;
  await command({type:'group.toggle',id:firstGroup});
  await host.wait(()=>ui.evaluate(`document.querySelectorAll('.tab-group-tabs .sidebar-tab').length===110`),'large group collapses');
  await command({type:'group.toggle',id:firstGroup});
  await host.wait(()=>ui.evaluate(`document.querySelectorAll('.tab-group-tabs .sidebar-tab').length===120`),'large group expands');
  await ui.evaluate(`const list=document.querySelector('.sidebar-scroll');list.scrollTop=list.scrollHeight`);
  await host.wait(()=>ui.evaluate(`(()=>{const groups=[...document.querySelectorAll('.tab-group-block')];return groups.every((g,i)=>!i||g.getBoundingClientRect().top>=groups[i-1].getBoundingClientRect().bottom-1)})()`),'scrolled groups stay separate after expand');
  assert(await ui.evaluate(`document.querySelector('.sidebar-scroll').getBoundingClientRect().bottom<=document.querySelector('.sidebar-quick-actions').getBoundingClientRect().top+1`),'groups cannot cover bottom actions');
  await writeFile(resolve(host.base,'groups-stress.png'),Buffer.from((await ui.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
  await ui.call('Emulation.clearDeviceMetricsOverride');
  if(!baseline) {
    const opener=original.tabs.find(t=>t.groupId);
    const group=opener.groupId;
    await command({type:'tab.activate',id:opener.id});
    await command({type:'nav.go',id:opener.id,input:host.origin+'/group-source'});
    let page=await host.connect(host.origin+'/group-source');
    const before=(await state()).tabs.length;
    await page.evaluate(`location.href=${JSON.stringify(host.origin+'/group-redirect')};true`);
    await host.wait(async()=>(await state()).tabs.find(t=>t.id===opener.id)?.url===host.origin+'/group-destination','HTTP redirect finishes');
    assert.equal((await state()).tabs.length,before,'redirect stays in the same tab');
    assert.equal((await state()).tabs.find(t=>t.id===opener.id).groupId,group,'redirect preserves group');
    page=await host.connect(host.origin+'/group-destination');
    const other=original.tabs.find(t=>t.groupId&&t.groupId!==group);
    await command({type:'tab.activate',id:other.id});
    await page.evaluate(`window.open(${JSON.stringify(host.origin+'/group-child')},'_blank');true`);
    const child=await host.wait(async()=>(await state()).tabs.find(t=>t.url===host.origin+'/group-child'),'new-window child');
    assert.equal(child.groupId,group,'new windows inherit their opener group, even when another tab is active');
    assert.equal(child.sessionId,opener.sessionId,'new windows retain the opener session');
    const tabs=(await state()).tabs, members=tabs.map((t,i)=>t.groupId===group?i:-1).filter(i=>i>=0);
    assert.equal(members.at(-1)-members[0]+1,members.length,'group members remain adjacent');
    await host.wait(()=>ui.evaluate(`document.querySelectorAll('.tab-group-block').length===12`),'child does not split a group into two headers');
    await command({type:'tab.activate',id:opener.id});
    await host.wait(async()=>(await state()).activeId===opener.id,'source active for link preview');
    await command({type:'preview.open',id:opener.id,input:host.origin+'/group-preview'});
    await host.connect(host.origin+'/group-preview');
    await command({type:'preview.tab'});
    const promoted=await host.wait(async()=>(await state()).tabs.find(t=>t.url===host.origin+'/group-preview'),'preview opens as tab');
    assert.equal(promoted.groupId,group,'opening a link from preview retains its source group');
  }
  console.log(JSON.stringify({passed:baseline?'group layout stress':'group layout, same-tab redirects and opener group/session inheritance',evidence:host.base}));
} catch(error) {
  if(ui) {
    await writeFile(resolve(host.base,'groups-failure.png'),Buffer.from((await ui.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    const geometry=await ui.evaluate(`[...document.querySelectorAll('.tab-group-block')].map(g=>({box:g.getBoundingClientRect().toJSON(),heading:g.querySelector('.tab-group-heading').getBoundingClientRect().toJSON(),tabs:[...g.querySelectorAll('.sidebar-tab')].map(t=>t.getBoundingClientRect().toJSON())}))`);
    await writeFile(resolve(host.base,'groups-failure.json'),JSON.stringify(geometry,null,2));
  }
  throw error;
} finally {await host.dispose();}
