import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {access,readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost('src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
const tokens={ClientId:'oaiapp_test',Subject:'fixture',Email:'permissions@example.invalid',IdToken:'fixture',AccessToken:'fixture-no-inference',RefreshToken:'fixture',Expires:new Date(Date.now()+3600000).toISOString(),Scopes:'openid chatgpt.tokens.use.direct'};
const bytes=Buffer.from(JSON.stringify(tokens)).toString('base64');
execFileSync('powershell.exe',['-NoProfile','-NonInteractive','-Command',`Add-Type -AssemblyName System.Security;[IO.File]::WriteAllBytes('${resolve(host.profile,'chatgpt.bin')}',[Security.Cryptography.ProtectedData]::Protect([Convert]::FromBase64String('${bytes}'),[Text.Encoding]::UTF8.GetBytes('Sol.ChatGPT'),[Security.Cryptography.DataProtectionScope]::CurrentUser))`],{windowsHide:true});
const settings=()=>readFile(resolve(host.profile,'settings.json'),'utf8').then(JSON.parse);
let ui,chat;
const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
async function openChat() {
  await command({type:'chat.toggle'});chat=await host.connect('#chat');
  await host.wait(()=>chat.evaluate('!!document.querySelector(".sol-chat-input textarea")'),'ready chat');
  await chat.evaluate(`window.__native=[];window.__commands=[];chrome.webview.addEventListener('message',e=>{if(e.data.chat||e.data.voice)__native.push(e.data)});const post=chrome.webview.postMessage.bind(chrome.webview);chrome.webview.postMessage=m=>{__commands.push(JSON.parse(m));post(m)}`);
  await command({type:'chat.ready'});await host.wait(()=>chat.evaluate('__native.some(m=>m.chat)'),'native chat state');
}
try {
  ui=await host.launch(900,1250);const normal=await ui.evaluate('window.__sidebarState.activeId');await openChat();
  assert.equal(await chat.evaluate(`document.querySelectorAll('[aria-label="Mode vocal"],[aria-label="Voice mode"],.sol-voice,[data-slot=voice-orb]').length`),0);
  for(const type of ['voice.open','voice.listen','voice.settings']) {
    await command({type});await host.wait(()=>chat.evaluate('__native.at(-1)?.voice?.available===false&&!__native.at(-1).voice.open'),'voice blocked');
  }
  assert.equal(await chat.evaluate('performance.getEntriesByType("resource").some(r=>r.name.includes("/SolVoice-"))'),false,'voice chunk is not loaded');
  await command({type:'settings.open',x:500,y:58});const popup=await host.connect('#popup');
  await host.wait(()=>popup.evaluate('!!document.querySelector(".settings-nav")'),'settings');
  await popup.evaluate('[...document.querySelectorAll(".settings-nav [role=tab]")].at(-1).click()');
  const toggle=`[...document.querySelectorAll('.ai-settings [role=switch]')].find(b=>/Toujours autoriser|Always allow/.test(b.textContent))`;
  await host.wait(()=>popup.evaluate(`!!(${toggle})`),'permission switch');
  assert.equal(await popup.evaluate(`(${toggle}).getAttribute('aria-checked')`),'false');await popup.evaluate(`(${toggle}).click()`);
  await host.wait(async()=>!!(await settings()).SolAiAutoApproveActions,'grant persisted');
  await host.wait(()=>chat.evaluate('!!document.querySelector(".sol-chat-permission")'),'standing permission shown');
  await writeFile(resolve(host.base,'permissions-settings.png'),Buffer.from((await popup.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
  await command({type:'popup.close'});await host.close(ui);ui=await host.launch(900,1250);await openChat();
  await host.wait(()=>chat.evaluate('!!document.querySelector(".sol-chat-permission")'),'permission survives restart');
  await command({type:'settings.set',id:'aiAutoApproveActions',index:0});await host.wait(async()=>!(await settings()).SolAiAutoApproveActions,'revocation persisted');
  const fixture={chat:true,ready:true,account:'fixture',busy:true,error:null,messages:[],voice:{available:false,open:false},agent:{steps:[],approval:{id:'stale-fixture',label:'Remplir le formulaire',detail:'Nom et adresse fournis pour cette demande'}}};
  for(const theme of ['light','dark']) {
    await command({type:'settings.set',id:'theme',index:theme==='light'?1:2});
    await host.wait(()=>chat.evaluate(`document.documentElement.dataset.theme==='${theme}'`),'theme applied');
    for(const width of [220,380,520]) {
      await chat.call('Emulation.setDeviceMetricsOverride',{width,height:800,deviceScaleFactor:1,mobile:false});
      await chat.evaluate(`chrome.webview.dispatchEvent(new MessageEvent('message',{data:${JSON.stringify(fixture)}}))`);
      await host.wait(()=>chat.evaluate('!!document.querySelector(".ai-approval-always")'),'permanent approval card');
      assert(await chat.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),'approval card fits narrow chat');
      if(width===380)await writeFile(resolve(host.base,`permissions-${theme}.png`),Buffer.from((await chat.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    }
  }
  await chat.evaluate('document.querySelector(".ai-approval-always").click()');
  await host.wait(()=>chat.evaluate('__commands.some(c=>c.type==="ai.approve.always"&&c.id==="stale-fixture")'),'chat button dispatch');
  await command({type:'chat.ready'});await host.wait(()=>chat.evaluate('!document.querySelector(".ai-approval-always")'),'native state restored');
  assert.equal((await settings()).SolAiAutoApproveActions,false,'stale card cannot grant native permission');
  await command({type:'window.private'});await host.wait(()=>ui.evaluate('window.__sidebarState.privateMode&&!window.__sidebarState.chatOpen'),'private context');
  await command({type:'settings.set',id:'aiAutoApproveActions',index:1});await command({type:'tab.activate',id:normal});
  await host.wait(()=>ui.evaluate('!window.__sidebarState.privateMode'),'normal context');
  assert.equal((await settings()).SolAiAutoApproveActions,false,'private context cannot alter permission');
  await writeFile(resolve(host.base,'permissions-ui.json'),JSON.stringify({voiceHidden:true,microphoneUsed:false,persistence:true,revocation:true,privateIsolation:true,staleCardRejected:true,realChatGptInference:false},null,2));
  await host.close(ui);console.log('PASS voice hidden/blocked/lazy, settings opt-in, restart, revoke, chat approval at 220/380/520px light/dark, stale card and private isolation; '+host.base);
} finally {await host.dispose();await assert.rejects(access(host.profile));}
