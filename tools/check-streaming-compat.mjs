// All pages are intercepted fixtures; no Prime account, licence or personal cookies are accessed.
import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
let ui;
try {
  ui=await host.launch();
  const state=await ui.evaluate('window.__sidebarState');
  const page=await host.connect(state.tabs.find(t=>t.id===state.activeId).url);
  const html=`<!doctype html><title>Streaming compatibility fixture</title>
    <style>.a-modal-backdrop{position:fixed;inset:0;background:#0008;pointer-events:none}body{overflow:hidden}.a-modal-scroller{position:relative}</style>
    <form action="/cookie-preferences"><button name="acceptCookies">Accept cookies</button></form>
    <div class="a-modal-backdrop"></div><div class="a-modal-scroller"><h1>Profile PIN</h1><input type="password"></div>
    <button id="profile" class="profile-card">Profile</button><button id="play">Play</button>
    <script>window.accepts=0;window.profiles=0;window.plays=0;
    const descriptor=Object.getOwnPropertyDescriptor(chrome.webview,'hostObjects');
    window.bridgeDescriptor={configurable:descriptor?.configurable,writable:descriptor?.writable};
    // Amazon's public DVWebClient constructor checks this synchronously, without try/catch.
    // A denied WebView2 host-object proxy must behave like an absent native component.
    try {window.nativeProbe=chrome.webview.hostObjects.sync.PrimeVideoWinRTComponent===undefined?'browser':'native'}
    catch(error){window.nativeProbe=error.message}
    window.webAppReady=nativeProbe==='browser';
    document.querySelector('form').onsubmit=e=>{e.preventDefault();window.accepts++};
    if(webAppReady){document.querySelector('#profile').onclick=()=>window.profiles++;
    document.querySelector('#play').onclick=()=>window.plays++}</script>`;
  const off=page.on('Fetch.requestPaused',event=>void page.call('Fetch.fulfillRequest',{
    requestId:event.requestId,responseCode:200,responseHeaders:[{name:'Content-Type',value:'text/html'}],body:Buffer.from(html).toString('base64')
  }).catch(()=>{}));
  await page.call('Fetch.enable',{patterns:[{urlPattern:'https://www.primevideo.com/*',requestStage:'Request'},{urlPattern:'https://unrelated.example/*',requestStage:'Request'}]});
  const url='https://www.primevideo.com/sol-compatibility-fixture';
  await ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify({type:'nav.go',id:state.activeId,input:url}))})`);
  await host.wait(()=>page.evaluate('location.hostname==="www.primevideo.com"&&!!document.querySelector("#profile")').catch(()=>false),'Prime-host fixture');
  await page.evaluate('new Promise(resolve=>setTimeout(resolve,1400))');
  const interference=await page.evaluate('({automaticSubmits:accepts,backdrop:getComputedStyle(document.querySelector(".a-modal-backdrop")).display,overflow:getComputedStyle(document.body).overflow,nativeProbe,bridgeDescriptor})');
  await writeFile(resolve(host.base,'streaming-interference.json'),JSON.stringify(interference,null,2));
  assert.equal(interference.automaticSubmits,0,'Browser must not repeatedly submit Amazon cookie forms');
  assert.notEqual(interference.backdrop,'none','Legitimate profile/PIN backdrop must remain intact');
  assert.equal(interference.overflow,'hidden','Site modal owns the scroll lock');
  assert.equal(interference.nativeProbe,'browser','Amazon native-app detection must not throw access denied in a browser');
  for(const id of ['profile','play']){
    const point=await page.evaluate(`(()=>{const r=document.getElementById('${id}').getBoundingClientRect();return{x:r.left+r.width/2,y:r.top+r.height/2}})()`);
    for(const type of ['mouseMoved','mousePressed','mouseReleased'])await page.call('Input.dispatchMouseEvent',{type,...point,...(type==='mouseMoved'?{}:{button:'left',clickCount:1})});
  }
  assert.deepEqual(await page.evaluate('({profiles,plays,accepts})'),{profiles:1,plays:1,accepts:0},'Real clicks reach the site once');
  const drm=await page.evaluate(`(async()=>{const results={};for(const key of ['com.widevine.alpha','com.microsoft.playready.recommendation']){let timer;try{results[key]=await Promise.race([navigator.requestMediaKeySystemAccess(key,[{initDataTypes:['cenc'],videoCapabilities:[{contentType:'video/mp4; codecs="avc1.42E01E"'}]}]).then(()=>'available'),new Promise(resolve=>{timer=setTimeout(()=>resolve('probe timeout'),3000)})])}catch(e){results[key]=e.name}finally{clearTimeout(timer)}}return results})()`);
  await ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify({type:'nav.go',id:state.activeId,input:'https://unrelated.example/sol-compatibility-fixture'}))})`);
  await host.wait(()=>page.evaluate('location.hostname==="unrelated.example"&&typeof nativeProbe==="string"').catch(()=>false),'unrelated site');
  assert.match(await page.evaluate('nativeProbe'),/0x80070005/,'Unrelated sites retain denied native-object access');
  await writeFile(resolve(host.base,'streaming-compatibility.json'),JSON.stringify({interference,drm,checks:['Amazon native-app detection sees no WinRT component without throwing','web app initialization reaches its click handlers','native access remains denied on unrelated sites','no automatic consent','PIN modal preserved','scroll lock preserved','trusted profile/play clicks','runtime EME capability only; no authenticated playback verified']},null,2));
  await page.call('Fetch.disable');off();await host.close(ui);console.log(JSON.stringify({passed:true,drm,evidence:host.base}));
} finally {await host.dispose()}
