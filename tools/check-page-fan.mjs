import assert from 'node:assert/strict';
import {access,readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const baseline=process.argv.includes('--baseline');
const fixtureCSS='body{margin:0;font:16px system-ui;background:#eff1e7;color:#18181b}main{padding:72px 64px;display:grid;gap:64px;justify-items:start}a.card{display:block;width:220px;height:90px;border-radius:16px;background:#cfdbc9;color:inherit;padding:16px;box-sizing:border-box}pre{width:240px;min-height:80px;margin:0;background:#d8dfec;padding:12px;box-sizing:border-box}#selection{max-width:400px}input{width:180px}#end{height:900px}';
const host=await sidebarHost('src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{serve:(req,res)=>{
  if(req.url==='/image.svg'){res.writeHead(200,{'Content-Type':'image/svg+xml'});res.end('<svg xmlns="http://www.w3.org/2000/svg" width="240" height="140"><rect width="240" height="140" fill="#83a87c"/></svg>');return true;}
  if(req.url==='/fixture.css'){res.writeHead(200,{'Content-Type':'text/css'});res.end(fixtureCSS);return true;}
  res.writeHead(200,{'Content-Type':'text/html; charset=utf-8','Content-Security-Policy':"default-src 'self'; style-src 'self'; script-src 'none'; require-trusted-types-for 'script'"});
  res.end(`<!doctype html><html lang="fr"><title>Éventail de test</title><link rel="stylesheet" href="/fixture.css"><main><a class="card" id="first" href="/card-a">Première carte</a><a class="card" id="second" href="/card-b">Deuxième carte</a><img id="image" src="/image.svg" alt="Image de test"><pre id="code" tabindex="0">const answer = 42;\nconsole.log(answer);</pre><p id="selection" tabindex="0">Un passage de texte à sélectionner dans la page.</p><input id="input" value="Texte du formulaire"><div id="end"></div></main></html>`);return true;
}});
let ui,page,stage='native';
const stats=()=>page.evaluate('({...__fanStats})');
const reset=()=>page.evaluate('__fanStats.raf=0;__fanStats.timers=0');
const visible=async()=>{
  const tree=(await page.call('DOM.getDocument',{depth:-1,pierce:true})).root;
  const find=n=>n.localName==='sol-fan'?n:[...(n.children??[]),...(n.shadowRoots??[])].map(find).find(Boolean);
  const node=find(tree);if(!node)return [];
  const buttons=[];
  const walk=n=>{if(n.localName==='button')buttons.push(n);for(const c of [...(n.children??[]),...(n.shadowRoots??[])])walk(c);};walk(node);
  return Promise.all(buttons.map(async node=>{
    const {object}=await page.call('DOM.resolveNode',{nodeId:node.nodeId});
    try {const result=await page.call('Runtime.callFunctionOn',{objectId:object.objectId,functionDeclaration:`function(){const r=this.getBoundingClientRect();return {hidden:this.hidden,visible:getComputedStyle(this).visibility!=='hidden',moving:this.getAnimations().some(a=>a.playState==='running'||a.pending),label:this.getAttribute('aria-label'),x:r.x,y:r.y,width:r.width,height:r.height}}`,returnByValue:true});return result.result.value;}
    finally {await page.call('Runtime.releaseObject',{objectId:object.objectId});}
  }));
};
const hover=async selector=>{
  const point=await page.evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(selector)});e.scrollIntoView({block:'center'});const r=e.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
  await page.call('Input.dispatchMouseEvent',{type:'mouseMoved',...point});
  await host.wait(async()=>(await visible()).filter(b=>!b.hidden&&b.visible).length===3,'three available commands',5000);
  await host.wait(async()=>(await visible()).every(b=>!b.moving),'spring animation settled',5000);
};
const escape=async()=>{await page.call('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape'});await host.wait(async()=>(await visible()).every(b=>b.hidden),'Escape closes commands',5000);};
const select=async()=>{
  await page.evaluate(`(()=>{document.querySelector('#selection').scrollIntoView({block:'center'});const r=document.createRange();r.selectNodeContents(document.querySelector('#selection'));getSelection().removeAllRanges();getSelection().addRange(r)})()`);
  await page.call('Input.dispatchKeyEvent',{type:'keyUp',key:'ArrowRight',code:'ArrowRight',modifiers:8});
  await host.wait(async()=>(await visible()).filter(b=>!b.hidden&&b.visible).length===3,'selection commands');
  await host.wait(async()=>(await visible()).every(b=>!b.moving),'selection spring settled');
};
try {
  ui=await host.launch(850,1200);page=await host.connect(host.origin+'/2');
  await ui.evaluate(`chrome.webview.postMessage('{"type":"settings.set","id":"fanTrigger","index":0}')`);
  await delay(250);
  await host.wait(()=>page.evaluate('!!window.__solFan&&document.querySelector("#image").complete'),'native injection');
  await page.call('Emulation.setCPUThrottlingRate',{rate:4});
  await page.evaluate(`window.__fanStats={raf:0,timers:0};const raf=window.requestAnimationFrame.bind(window),timer=window.setTimeout.bind(window);window.requestAnimationFrame=fn=>{__fanStats.raf++;return raf(fn)};window.setTimeout=(...args)=>{__fanStats.timers++;return timer(...args)};`);
  await reset();await hover('#first');await delay(1000);
  const opened=await stats();await reset();await delay(250);const idle=await stats();
  const report={baseline,opened,idle};
  console.log(JSON.stringify({base:host.base,...report}));
  if(!baseline){
    assert.equal(opened.raf,0,'opening needs no animation loop');assert.equal(opened.timers,0,'opening has no hover/stagger timeout');
    assert.equal(idle.raf,0);assert.equal(idle.timers,0);
    assert.equal(await page.evaluate('document.querySelector("sol-fan").shadowRoot'),null,'closed shadow stays private');
    // Mesurer les lectures de la vraie carte, sans compter les animations d'autres composants UI.
    await page.evaluate(`(()=>{const e=document.querySelector('#first'),measure=e.getBoundingClientRect.bind(e);window.__rectReads=0;e.getBoundingClientRect=()=>{__rectReads++;return measure()};for(let i=0;i<100;i++)dispatchEvent(new Event('scroll'))})()`);
    await host.wait(()=>page.evaluate('__rectReads>0'),'coalesced layout');
    assert.equal(await page.evaluate('__rectReads'),1,'100 scroll notifications share one geometry read');
    await page.evaluate(`delete document.querySelector('#first').getBoundingClientRect`);
    // Un vrai clic valide aussi la clé native, sans lire ni journaliser sa valeur.
    const before=await ui.evaluate('window.__sidebarState.tabs.length');
    const button=(await visible())[0],point={x:button.x+button.width/2,y:button.y+button.height/2};
    for(const type of ['mouseMoved','mousePressed','mouseReleased'])await page.call('Input.dispatchMouseEvent',{type,...point,...(type==='mouseMoved'?{}:{button:'left',clickCount:1})});
    await host.wait(()=>ui.evaluate(`window.__sidebarState.tabs.length===${before+1}&&window.__sidebarState.tabs.some(t=>t.url===${JSON.stringify(host.origin+'/card-a')})`),'native tab opening');
    // Les 12 charges utiles sont exercées dans un WebView UI distinct, avec le script de production inchangé.
    // Le canal est remplacé AVANT son chargement pour préserver le presse-papiers et éviter toute requête IA.
    await ui.evaluate(`chrome.webview.postMessage('{"type":"chat.toggle"}')`);
    page=await host.connect('#chat');await host.wait(()=>page.evaluate('!!document.querySelector(".sol-chat")'),'isolated action fixture');
    const html=(await (await fetch(host.origin+'/2')).text()).replace('<title>',`<base href="${host.origin}/"><title>`);
    const {frameTree}=await page.call('Page.getFrameTree');await page.call('Page.setDocumentContent',{frameId:frameTree.frame.id,html});
    await page.evaluate(`const fixtureSheet=new CSSStyleSheet();fixtureSheet.replaceSync(${JSON.stringify(fixtureCSS)});document.adoptedStyleSheets=[fixtureSheet];`);
    await page.call('Emulation.setDeviceMetricsOverride',{width:900,height:850,deviceScaleFactor:1,mobile:false});
    await page.call('Emulation.setCPUThrottlingRate',{rate:4});
    await page.evaluate(`window.__fanCommands=[];chrome.webview.postMessage=value=>{const c=JSON.parse(value);__fanCommands.push({kind:c.kind,action:c.action,value:c.value})};`);
    await page.evaluate((await readFile('src/Browser.App/inject/fan.js','utf8')).replace('/*__SOL_FAN_SETTINGS__*/null', '{enabled:true,trigger:0}'));
    await host.wait(()=>page.evaluate('document.querySelector("#image").complete'),'fixture image loaded');
    for(const [selector,kind,actions,value] of [
      ['#second','link',['tab','split','link'],host.origin+'/card-b'],
      ['#image','image',['tab','link','note'],host.origin+'/image.svg'],
      ['#code','code',['copy','explain','note'],'const answer = 42;\nconsole.log(answer);'],
      ['#selection','selection',['copy','sol','more'],'Un passage de texte à sélectionner dans la page.'],
    ])for(let i=0;i<actions.length;i++){
      stage=`${kind}:${actions[i]}`;
      if(kind==='selection')await select();else await hover(selector);
      const b=(await visible())[i],p={x:b.x+b.width/2,y:b.y+b.height/2};
      for(const type of ['mouseMoved','mousePressed','mouseReleased'])await page.call('Input.dispatchMouseEvent',{type,...p,...(type==='mouseMoved'?{}:{button:'left',clickCount:1})});
      await host.wait(()=>page.evaluate('__fanCommands.length===1'),`${kind} ${actions[i]} emitted`);
      assert.deepEqual(await page.evaluate('__fanCommands.splice(0)'),[{kind,action:actions[i],value}]);
    }
    stage='form and rapid switching';await escape();await page.evaluate('getSelection().removeAllRanges()');
    // Les champs de formulaire ne déclenchent pas les actions de sélection.
    await page.evaluate(`const e=document.querySelector('#input');e.focus();e.select()`);
    await page.call('Input.dispatchKeyEvent',{type:'keyUp',key:'ArrowRight',code:'ArrowRight',modifiers:8});
    assert((await visible()).every(b=>b.hidden));
    // Les passages rapides entre cartes n'ont pas de réouverture différée.
    await page.evaluate('document.activeElement.blur()');await hover('#first');await hover('#second');await escape();await delay(600);
    assert((await visible()).every(b=>b.hidden));
    stage='narrow viewport';await page.call('Emulation.setDeviceMetricsOverride',{width:360,height:650,deviceScaleFactor:1,mobile:false});
    await page.evaluate(`document.querySelector('#first').style.cssText='position:fixed;right:0;top:0;width:160px;height:70px'`);
    await hover('#first');
    assert((await visible()).every(b=>b.x>=0&&b.y>=0&&b.x+b.width<=360&&b.y+b.height<=650),'commands fit viewport edges');
    await writeFile(resolve(host.base,'page-fan-narrow.png'),Buffer.from((await page.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    stage='keyboard and scroll';await escape();await page.evaluate(`document.querySelector('#first').style.cssText='';document.querySelector('#code').focus()`);
    await host.wait(async()=>(await visible()).filter(b=>!b.hidden).length===3,'keyboard focus opens commands');
    await page.evaluate(`document.querySelector('#code').remove();dispatchEvent(new Event('scroll'))`);
    await host.wait(async()=>(await visible()).every(b=>b.hidden),'removed target hides safely');
    await escape();
    console.log('PASS strict CSP/Trusted Types, four target types, 12 action payloads, trusted native tab opening, keyboard, Escape, fields excluded, viewport bounds, coalesced scroll and no idle animation');
  }
  await writeFile(resolve(host.base,'page-fan.json'),JSON.stringify(report,null,2));
  await host.close(ui);
}catch(error){console.error(JSON.stringify({stage,details:await page?.evaluate(`({url:location.href,fan:window.__solFan,hosts:document.querySelectorAll('sol-fan').length,width:innerWidth,probe:window.__probe,text:document.body.innerText.slice(0,120),commands:window.__fanCommands})`).catch(()=>null),buttons:await visible().catch(()=>[])},null,2));throw error;}
finally{await host.dispose();await assert.rejects(access(host.profile));}
