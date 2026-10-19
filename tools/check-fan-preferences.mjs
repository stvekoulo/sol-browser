import assert from 'node:assert/strict';
import {DatabaseSync} from 'node:sqlite';
import {access, readFile, writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const startUrl='https://start.sol/index.html#start';
const host=await sidebarHost('src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe', {serve(req,res) {
  res.writeHead(200,{'Content-Type':'text/html; charset=utf-8'});
  res.end('<html><title>Fan preferences</title><body style="margin:60px"><a id="card" href="/link" style="display:block;width:220px;height:100px;background:#aabbcc">Carte de test</a><input id="field" value="Formulaire"><p>Contenu ordinaire</p></body></html>');return true;
}});
const initial=JSON.parse(await readFile(resolve(host.profile,'settings.json'),'utf8'));
await writeFile(resolve(host.profile,'settings.json'),JSON.stringify({...initial,MemorySaver:false}));
await writeFile(resolve(host.profile,'session.json'),JSON.stringify({Urls:[host.origin+'/2'],ActiveIndex:0}));
let ui,web,start;
const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const settings=()=>readFile(resolve(host.profile,'settings.json'),'utf8').then(JSON.parse);
async function set(id,index) {
  await command({type:'settings.set',id,index});
  await host.wait(async()=>{const value=await settings();return id==='fanTrigger'?value.FanTrigger===index:value.FanEnabled===!!index;},'persisted '+id);
  await delay(250);
}
async function point(page,selector) {
  return page.evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(selector)});e.scrollIntoView({block:'center'});const r=e.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
}
const hover=async(page,selector)=>page.call('Input.dispatchMouseEvent',{type:'mouseMoved',...await point(page,selector)});
async function right(page,selector) {
  const p=await point(page,selector);
  for(const type of ['mouseMoved','mousePressed','mouseReleased'])await page.call('Input.dispatchMouseEvent',{type,...p,...(type==='mouseMoved'?{}:{button:'right',clickCount:1})});
}
async function webButtons() {
  const {root}=await web.call('DOM.getDocument',{depth:-1,pierce:true});
  const nodes=[];
  const walk=(n,fan=false)=>{fan ||= n.localName==='sol-fan';if(fan&&n.localName==='button')nodes.push(n);for(const child of [...(n.children??[]),...(n.shadowRoots??[])])walk(child,fan);};walk(root);
  return Promise.all(nodes.map(async n=>{
    const {object}=await web.call('DOM.resolveNode',{nodeId:n.nodeId});
    try{return (await web.call('Runtime.callFunctionOn',{objectId:object.objectId,functionDeclaration:'function(){return !this.hidden&&this.style.pointerEvents!=="none"}',returnByValue:true})).result.value;}
    finally{await web.call('Runtime.releaseObject',{objectId:object.objectId});}
  })).then(buttons=>buttons.filter(Boolean).length);
}
const reactButtons=()=>start.evaluate('document.querySelectorAll(".nod-act:not(:disabled)").length');
async function closed() { await host.wait(async()=>await webButtons()===0&&await reactButtons()===0,'all fans closed'); }
try {
  ui=await host.launch(850,1200);web=await host.connect(host.origin+'/2');
  const db=new DatabaseSync(resolve(host.profile,'browser.db'));
  try { const add=db.prepare('INSERT INTO bookmarks (url,title,added) VALUES (?,?,?)'); for(let i=0;i<20;i++)add.run(host.origin+'/fav-'+i,'Favori très long '+i,Date.now()-i); }
  finally { db.close(); }
  await command({type:'tab.open',url:startUrl});start=await host.connect(startUrl);
  await host.wait(()=>start.evaluate('!!document.querySelector(".start-favorites")'),'start ready');
  const data={event:'start',lang:'fr',privateMode:false,wallpaper:false,favorites:Array.from({length:20},(_,i)=>({url:host.origin+'/fav-'+i,title:'Un favori au nom très long '+i})),
    history:[{url:'https://'+'domainetreslong'.repeat(15)+'.invalid',title:'Titre très long '.repeat(20),last:Date.now()}],groups:[{id:'test',name:'Groupe au nom très long '.repeat(20),color:'#aa77cc',count:12}],categories:[{name:'Un sujet très long '.repeat(25),total:20,links:[]}]};
  await start.evaluate(`chrome.webview.dispatchEvent(new MessageEvent('message',{data:${JSON.stringify(data)}}))`);
  await host.wait(()=>start.evaluate('document.querySelectorAll(".start-site").length===20'),'fixture');
  assert.equal((await settings()).FanTrigger,2,'right click is the initial setting');
  assert.equal(await start.evaluate('document.querySelectorAll(".nod,.nod-act,sol-fan").length'),0,'no fan integration on start');
  await hover(web,'#card');assert.equal(await webButtons(),0,'no automatic fan by default');
  await right(web,'#card');await host.wait(async()=>await webButtons()===3,'default right click');
  await set('fanTrigger',0);await closed();
  await hover(web,'body');await hover(web,'#card');await hover(start,'.start-site');
  await host.wait(async()=>await webButtons()===3&&await reactButtons()===0,'web hover with no start fan');
  await set('fanEnabled',0);await closed();
  await hover(web,'body');await hover(web,'#card');await right(start,'.start-site');
  assert.equal(await webButtons(),0);assert.equal(await reactButtons(),0);
  await set('fanTrigger',1);await set('fanEnabled',1);
  await hover(web,'body');await hover(start,'h2');
  await hover(web,'#card');await hover(start,'.start-site');await delay(1100);
  assert.equal(await webButtons(),0);assert.equal(await reactButtons(),0);
  await hover(web,'#field');await hover(start,'h2');await delay(4100);await closed();
  await hover(web,'#card');await hover(start,'.start-site');const begin=Date.now();
  await delay(4300);assert.equal(await webButtons(),0);assert.equal(await reactButtons(),0);
  await host.wait(async()=>await webButtons()===3&&await reactButtons()===0,'five-second hover',3000);
  assert(Date.now()-begin>=4900);
  await web.call('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape'});
  await host.wait(async()=>await webButtons()===0,'selection starts closed');
  await web.evaluate(`(()=>{const r=document.createRange();r.selectNodeContents(document.querySelector('p'));getSelection().removeAllRanges();getSelection().addRange(r)})()`);
  await web.call('Input.dispatchKeyEvent',{type:'keyUp',key:'ArrowRight',code:'ArrowRight',modifiers:8});
  await web.evaluate("document.dispatchEvent(new Event('selectionchange'))");
  await delay(1000);assert.equal(await webButtons(),0);
  await host.wait(async()=>await webButtons()===3,'delayed selection survives queued selectionchange',7000);
  await web.evaluate('getSelection().removeAllRanges()');
  await set('fanTrigger',2);await closed();
  await hover(web,'body');await hover(web,'#card');await hover(start,'h2');await hover(start,'.start-site');
  assert.equal(await webButtons(),0);assert.equal(await reactButtons(),0);
  await command({type:'popup.close'});
  await right(web,'#card');
  await host.wait(async()=>await webButtons()===3&&await reactButtons()===0,'right click opens fan');
  const popup=await host.connect('#popup');
  assert(!await popup.evaluate('!!document.querySelector(".context-menu")'),'eligible right click has no second menu');
  await start.evaluate(`chrome.webview.dispatchEvent(new MessageEvent('message',{data:${JSON.stringify(data)}}))`);
  for(const width of [320,360,480,680,1200]) {
    await start.call('Emulation.setDeviceMetricsOverride',{width,height:800,deviceScaleFactor:1,mobile:false});
    await hover(start,'.start-site');await right(start,'.start-site');
    await host.wait(()=>start.evaluate('[...document.querySelectorAll(".nod-act")].every(b=>b.getAnimations().every(a=>a.playState!=="running"))'),'spring settles');
    assert(await start.evaluate('document.documentElement.scrollWidth<=innerWidth'),'no horizontal overflow at '+width);
    assert(await start.evaluate('[...document.querySelectorAll(".nod-act")].every(b=>{const r=b.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight})'),'actions fit at '+width);
    await writeFile(resolve(host.base,`start-responsive-${width}.png`),Buffer.from((await start.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
  }
  // Les nouveaux documents récupèrent aussi les préférences, sans attendre un message de thème.
  await command({type:'tab.activate',id:await ui.evaluate('window.__sidebarState.tabs.find(t=>t.url.endsWith("/2")).id')});
  const origin=await web.evaluate('performance.timeOrigin');
  await web.call('Page.reload');await host.wait(()=>web.evaluate(`performance.timeOrigin>${origin}&&document.readyState==="complete"&&!!window.__solFan`),'reload');
  await hover(web,'#card');assert.equal(await webButtons(),0);
  await right(web,'#card');await host.wait(async()=>await webButtons()===3,'right click after reload');
  await host.close(ui);
  ui=await host.launch();await command({type:'tab.activate',id:await ui.evaluate('window.__sidebarState.tabs.find(t=>t.url.endsWith("/2")).id')});web=await host.connect(host.origin+'/2');
  assert.equal((await settings()).FanTrigger,2);
  await hover(web,'#card');assert.equal(await webButtons(),0);await right(web,'#card');
  await host.wait(async()=>await webButtons()===3,'right click after restart');
  await host.close(ui);
  console.log('PASS default right click, global modes/live/restart/selection; start without fans at 320–1200; '+host.base);
} finally { await host.dispose();await assert.rejects(access(host.profile)); }
