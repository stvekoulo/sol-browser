import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {access,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost('src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
const tokens={ClientId:'oaiapp_test',Subject:'fixture',Email:'voice-test@example.invalid',IdToken:'fixture',AccessToken:'fixture-no-inference',RefreshToken:'fixture',Expires:new Date(Date.now()+3600000).toISOString(),Scopes:'openid chatgpt.tokens.use.direct'};
const bytes=Buffer.from(JSON.stringify(tokens)).toString('base64');
execFileSync('powershell.exe',['-NoProfile','-NonInteractive','-Command',`Add-Type -AssemblyName System.Security;[IO.File]::WriteAllBytes('${resolve(host.profile,'chatgpt.bin')}',[Security.Cryptography.ProtectedData]::Protect([Convert]::FromBase64String('${bytes}'),[Text.Encoding]::UTF8.GetBytes('Sol.ChatGPT'),[Security.Cryptography.DataProtectionScope]::CurrentUser))`],{windowsHide:true});
const dimensions=[];
try {
  const ui=await host.launch(900,1250);
  const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
  const normal=await ui.evaluate('window.__sidebarState.activeId');
  await command({type:'chat.toggle'});
  const chat=await host.connect('#chat');
  await host.wait(()=>chat.evaluate('!!document.querySelector(".sol-chat-input textarea")'),'ready chat');
  await chat.evaluate(`window.__voiceMessages=[];chrome.webview.addEventListener('message',e=>{if(e.data.voice)__voiceMessages.push(e.data.voice)})`);
  await chat.evaluate(`document.querySelector('[aria-label="Mode vocal"], [aria-label="Voice mode"]').click()`);
  await host.wait(()=>chat.evaluate('!!document.querySelector("[data-slot=voice-orb] canvas")'),'official voice orb');
  assert.equal(await chat.evaluate('document.querySelector("[data-slot=voice-orb]").dataset.renderState'),'ready','WebGL orb initializes');
  assert(await chat.evaluate('__voiceMessages.some(m=>m.open&&m.provider==="windows"&&m.mode==="idle")'),'native voice starts idle without opening microphone');
  for(const theme of ['light','dark']) {
    await command({type:'settings.set',id:'theme',index:theme==='light'?1:2});
    await host.wait(()=>chat.evaluate(`document.documentElement.dataset.theme==='${theme}'`),'theme applied');
    for(const width of [220,380,520]) {
      await chat.call('Emulation.setDeviceMetricsOverride',{width,height:800,deviceScaleFactor:1,mobile:false});
      await chat.evaluate(`chrome.webview.dispatchEvent(new MessageEvent('message',{data:{voice:{open:true,provider:'windows',mode:'listening',level:.8,text:'Ouvre YouTube et prépare mes notes.',error:null}}}))`);
      const bounds=await host.wait(()=>chat.evaluate(`(()=>{const orb=document.querySelector('[data-slot=voice-orb]');if(!orb)return false;const rect=orb.getBoundingClientRect();return {width:innerWidth,scroll:document.documentElement.scrollWidth,orb:{left:rect.left,right:rect.right,width:rect.width},buttons:[...document.querySelectorAll('.sol-voice button')].map(b=>b.getBoundingClientRect().right)}})()`),'voice bounds');
      assert(bounds.scroll<=bounds.width+1,'no horizontal overflow');
      assert(bounds.orb.width>50&&bounds.orb.left>=0&&bounds.orb.right<=bounds.width+1,'orb fits narrow panel');
      assert(bounds.buttons.every(x=>x<=bounds.width+1),'voice controls stay reachable');
      dimensions.push({theme,...bounds});
      await writeFile(resolve(host.base,`voice-${theme}-${width}.png`),Buffer.from((await chat.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    }
  }
  await command({type:'voice.stop'});
  await host.wait(()=>chat.evaluate('__voiceMessages.at(-1)?.mode==="idle"'),'voice stop returns idle');
  await command({type:'voice.close'});
  await host.wait(()=>chat.evaluate('!document.querySelector(".sol-voice")'),'voice close unmounts orb');
  await command({type:'voice.open'});
  await host.wait(()=>chat.evaluate('!!document.querySelector(".sol-voice")'),'voice reopens');
  await command({type:'window.private'});
  await host.wait(()=>ui.evaluate('window.__sidebarState.privateMode&&!window.__sidebarState.chatOpen'),'private mode hides chat');
  await command({type:'voice.open'});
  await command({type:'tab.activate',id:normal});
  await command({type:'chat.toggle'});
  await command({type:'chat.ready'});
  await host.wait(()=>chat.evaluate('!document.querySelector(".sol-voice")'),'private navigation disposed voice session');
  await writeFile(resolve(host.base,'voice-ui.json'),JSON.stringify({dimensions,microphoneUsed:false,realChatGptInference:false,privateIsolation:true},null,2));
  await host.close(ui);
  console.log('PASS native Windows voice entry/stop/close/private isolation, official beUI orb, light/dark at 220/380/520px; '+host.base);
} finally {await host.dispose();await assert.rejects(access(host.profile));}
