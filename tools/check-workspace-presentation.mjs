import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost(process.argv[2]);let ui,page;
const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const layout=()=>ui.evaluate(`(()=>{const r=document.querySelector('.workspace-page').getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height}})()`);
async function aligned(){
  const rect=await layout(),actual=await page.evaluate('({width:innerWidth,height:innerHeight})');
  return Math.abs(rect.width-actual.width)<=2&&Math.abs(rect.height-actual.height)<=2;
}
async function startup(){
  await host.wait(async()=>/startup.ready coordinated/.test(await readFile(resolve(host.profile,'debug.log'),'utf8')),'coordinated startup');
  const log=await readFile(resolve(host.profile,'debug.log'),'utf8');
  assert.match(log,/startup.chrome.ready/);
  assert.match(log,/startup.content.ready/);
  assert.doesNotMatch(log,/startup.content.timeout|startup.failed/,'local startup waits for real content');
}
try{
  // Exercise the internal React first-paint handshake, including restored notes.
  const settings=JSON.parse(await readFile(resolve(host.profile,'settings.json'),'utf8'));
  await writeFile(resolve(host.profile,'session.json'),JSON.stringify({Urls:['https://start.sol/index.html#notes'],ActiveIndex:0}));
  ui=await host.launch(720);page=await host.connect('https://start.sol/index.html#notes');
  await startup();await host.wait(aligned,'native page and common document align');
  const targets=await host.targets();
  assert.equal(targets.filter(t=>t.url==='https://ui.navigateur/index.html').length,1);
  assert.equal(targets.filter(t=>t.url.endsWith('#sidebar')).length,0,'no separate sidebar renderer');
  await ui.evaluate(`window.__frames=[];const sample=()=>{window.__frames.push(document.querySelector('.workspace-page').getBoundingClientRect().x);if(window.__frames.length<40)requestAnimationFrame(sample)};requestAnimationFrame(sample)`);
  await command({type:'sidebar.toggle'});
  await host.wait(()=>ui.evaluate('window.__frames.length===40'),'animated common-layout frames');
  const frames=await ui.evaluate('window.__frames');
  assert.ok(new Set(frames.map(Math.round)).size>4,'beUI spring keeps intermediate layout frames');
  assert.ok(await ui.evaluate('document.querySelector(".workspace-sidebar").inert'),'hidden controls cannot take keyboard focus');
  await host.wait(aligned,'native page follows collapsed sidebar');
  await ui.call('Emulation.setEmulatedMedia',{features:[{name:'prefers-reduced-motion',value:'reduce'}]});
  await delay(100);await command({type:'sidebar.toggle'});
  await host.wait(async()=>Math.abs((await layout()).x-settings.SidebarWidth*.92)<1,'reduced-motion final position');
  await host.wait(aligned,'native page follows expanded sidebar');
  await command({type:'settings.set',id:'blur',index:0});
  await command({type:'settings.set',id:'blur',index:1});
  assert.ok(await ui.evaluate('!document.body.classList.contains("native-blur")'),'page blur setting does not enable Windows transparency');
  assert.notEqual(await ui.evaluate('getComputedStyle(document.body).backgroundColor'),'rgba(0, 0, 0, 0)','chrome keeps its page-coloured background');
  await command({type:'profile.menu',x:180,y:110});const popup=await host.connect('#popup');
  await host.wait(()=>popup.evaluate('!!document.querySelector(".profiles-popup")'),'original profile popup opens');
  assert.ok(await popup.evaluate('!document.body.classList.contains("native-blur")'),'profile popup keeps its original styling');
  await command({type:'popup.close'});
  await ui.evaluate(`document.querySelector('[data-action="notes.manage"]').dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,button:2}))`);
  await host.wait(()=>popup.evaluate('!!document.querySelector("[role=menuitem]")'),'original animated context menu opens');
  console.log(JSON.stringify({passed:'one chrome document; real notes first-paint startup; shared beUI spring frames; native page bounds; keyboard inert state; reduced motion; original panels and context menus; opaque chrome',evidence:host.base}));
}finally{if(ui)await host.close(ui).catch(()=>{});await host.dispose()}
