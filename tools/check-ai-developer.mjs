import assert from 'node:assert/strict';
import {access,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost('src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{serve(req,res){
  if(req.url.startsWith('/api')){res.writeHead(503,{'Content-Type':'application/json'});res.end(JSON.stringify({error:'Service temporarily unavailable',token:'synthetic-private-value'}));}
  else {res.writeHead(200,{'Content-Type':'text/html; charset=utf-8'});res.end('<main><h1>Diagnostic développeur Sol</h1><p>Fixture locale</p></main>');}return true;
}});
let ui,panel;
const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
try {
  ui=await host.launch(950,1300);const id=await ui.evaluate('window.__sidebarState.activeId');const page=await host.connect(host.origin+'/2');
  await command({type:'network.toggle'});panel=await host.connect('#panel');
  await host.wait(()=>panel.evaluate('!!document.querySelector(".net-bar")'),'network UI');
  await panel.evaluate(`window.__network=null;window.__detail=null;window.__commands=[];chrome.webview.addEventListener('message',e=>{if(e.data.panel==='network')__network=e.data;if(e.data.panel==='network-detail')__detail=e.data});const post=chrome.webview.postMessage.bind(chrome.webview);chrome.webview.postMessage=m=>{__commands.push(JSON.parse(m));post(m)}`);
  await command({type:'network.ready'});await host.wait(()=>panel.evaluate(`__network?.tabId===${JSON.stringify(id)}`),'capture started');
  const status=await page.evaluate(`fetch('/api').then(r=>r.status)`);assert.equal(status,503);
  await page.evaluate(`console.error("TypeError: Cannot read properties of undefined (reading 'map')")`);
  await host.wait(()=>panel.evaluate('__network?.items.some(r=>r.status===503)&&__network.console.some(c=>c.text.includes("TypeError"))'),'real network failure and console error');
  const request=await panel.evaluate('__network.items.find(r=>r.status===503)');
  await command({type:'network.detail',id:request.id});await host.wait(()=>panel.evaluate('!!__detail'),'request detail');
  const detail=await panel.evaluate('__detail');assert(!JSON.stringify(detail).includes('synthetic-private-value'),'existing inspector masks known secrets');
  assert(await panel.evaluate('[...document.querySelectorAll(".net-detail-actions button")].some(b=>/Expliquer avec ChatGPT|Explain with ChatGPT/.test(b.textContent))'),'network explanation action');
  const draft={Url:host.origin+'/api',Method:'GET',Headers:{'Content-Type':'application/json'},Body:'{"error":"Simulated gateway failure"}',Status:502,Delay:25,Mode:'response'};
  await command({type:'network.mock',id,input:JSON.stringify(draft)});
  await host.wait(()=>panel.evaluate('__network.mock?.Status===502&&!__network.mockBusy'),'mock ready');
  assert.equal(await page.evaluate(`fetch('/api').then(r=>r.status)`),502,'real WebView2 response simulation');
  await command({type:'network.mock',id,input:JSON.stringify({...draft,Mode:'timeout'})});
  await host.wait(()=>panel.evaluate('__network.mock?.Mode==="timeout"&&!__network.mockBusy'),'timeout mock ready');
  assert.equal(await page.evaluate(`fetch('/api').then(()=>false).catch(e=>e.name)`),'TypeError','real timeout simulation');
  await command({type:'network.unmock',id});await host.wait(()=>panel.evaluate('!__network.mock&&!__network.mockBusy'),'mock removed');
  assert.equal(await page.evaluate(`fetch('/api').then(r=>r.status)`),503,'original server behavior restored');
  await panel.evaluate(`[...document.querySelectorAll('[role=tab]')].find(b=>b.textContent.includes('Console')).click()`);
  await host.wait(()=>panel.evaluate('!!document.querySelector(".net-console button")'),'console explanation action');
  for(const width of [420,900,1300]) {
    await panel.call('Emulation.setDeviceMetricsOverride',{width,height:500,deviceScaleFactor:1,mobile:false});
    assert(await panel.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),'developer panel fits viewport');
  }
  await writeFile(resolve(host.base,'developer-console.png'),Buffer.from((await panel.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
  await panel.evaluate(`[...document.querySelectorAll('.net-bar button')].find(b=>b.textContent==='Sol AI').click()`);
  const chat=await host.connect('#chat');await host.wait(()=>chat.evaluate('document.querySelector(".sol-chat-empty")?.textContent.includes("diagnostic")'),'development help entry');
  assert.equal(await panel.evaluate('__commands.at(-1).type'),'chat.toggle','opening help sends no log payload');
  assert.equal(await panel.evaluate('Object.keys(__commands.at(-1)).sort().join(",")'),'id,type');
  await writeFile(resolve(host.base,'developer-results.json'),JSON.stringify({localFixture:true,httpFailure:true,consoleError:true,secretsMasked:true,httpMock:true,timeoutMock:true,restored:true,explainActions:true,realChatGptInference:false},null,2));
  await host.close(ui);console.log('PASS native console/HTTP diagnostics, secret masking, response and timeout simulation, restore, developer help and explanation controls; '+host.base);
} finally {await host.dispose();await assert.rejects(access(host.profile));}
