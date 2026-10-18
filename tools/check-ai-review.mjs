import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {access,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const exe=process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe';
const host=await sidebarHost(exe,{serve:(req,res)=>{
  res.writeHead(200,{'Content-Type':'text/html; charset=utf-8'});
  res.end('<html lang="fr"><title>Article de vérification</title><body><main><h1>Sol AI et le navigateur</h1><p>'+('Voici un article public destiné au test de résumé. '.repeat(60))+'</p><input type="password" value="fixture-private-password"><textarea>fixture-private-draft</textarea><div contenteditable>fixture-private-editor</div></main></body></html>');return true;
}});
const shell=script=>execFileSync('powershell.exe',['-NoProfile','-NonInteractive','-Command',script],{encoding:'utf8',windowsHide:true});
const shot=async(view,name)=>writeFile(resolve(host.base,name+'.png'),Buffer.from((await view.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
const fakeTokens={ClientId:'oaiapp_test',Subject:'fixture',Email:'test-with-a-very-long-address@example.invalid',IdToken:'fixture',AccessToken:'fixture-no-inference',RefreshToken:'fixture',Expires:new Date(Date.now()+3600000).toISOString(),Scopes:'openid chatgpt.tokens.use.direct'};
// Synthetic credentials only. Tests never click Run or Submit, so no inference reaches OpenAI.
const bytes=Buffer.from(JSON.stringify(fakeTokens)).toString('base64');
shell(`Add-Type -AssemblyName System.Security;[IO.File]::WriteAllBytes('${resolve(host.profile,'chatgpt.bin')}',[Security.Cryptography.ProtectedData]::Protect([Convert]::FromBase64String('${bytes}'),[Text.Encoding]::UTF8.GetBytes('Sol.ChatGPT'),[Security.Cryptography.DataProtectionScope]::CurrentUser))`);
let ui;
try {
  ui=await host.launch(800,1200);
  const command=async c=>{await ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);};
  const state=()=>ui.evaluate('window.__sidebarState');
  await host.wait(()=>ui.evaluate('window.__sidebarState.tabs.find(t=>t.id===window.__sidebarState.activeId)?.ai'),'synthetic connected account');
  const normal=(await state()).activeId;
  const popup=await host.connect('#popup');
  await popup.evaluate(`window.__aiMessages=0;chrome.webview.addEventListener('message',e=>{if(e.data.popup==='ai-action'){window.__aiContext=e.data;window.__aiMessages++}})`);
  await command({type:'ai.open',id:normal});
  await host.wait(()=>popup.evaluate('!!document.querySelector(".ai-action")'),'contextual AI action');
  const extracted=await popup.evaluate('window.__aiContext.text');
  assert(extracted.includes('article public'));
  assert(!extracted.includes('fixture-private-'),'form drafts are excluded from page extraction');
  await shot(popup,'context-public');
  await command({type:'popup.close'});
  await command({type:'chat.toggle'});
  const chat=await host.connect('#chat');
  await host.wait(()=>chat.evaluate('!!document.querySelector(".sol-chat-input textarea")'),'native connected chat');
  const inject=async data=>chat.evaluate(`chrome.webview.dispatchEvent(new MessageEvent('message',{data:${JSON.stringify(data)}}))`);
  const messages=[{from:'user',text:'Explique cette page en français.'},{from:'assistant',text:'# Réponse de Sol AI\n\nUne réponse **lisible** avec des liens longs et du code.\n\n'+('Texte de démonstration. '.repeat(50))+'\n\n```js\nconst adresse = "'+('x'.repeat(200))+'";\n```\n\nhttps://example.invalid/'+('a'.repeat(240))}];
  const fixture={chat:true,ready:true,account:fakeTokens.Email,busy:false,error:null,messages};
  await inject(fixture);
  await host.wait(()=>chat.evaluate('!!document.querySelector(".sol-chat-log h1")'),'Markdown renders');
  const resize=async(width,height)=>{
    shell(`Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public class AiSize{[DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int z,uint f);}';$p=Get-Process -Id ${host.pid};if(-not [AiSize]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,-9000,-9000,${width},${height},0x14)){exit 2}`);
    await delay(400);
  };
  const dimensions=[];
  for (const [width,height] of [[1200,800],[640,600],[420,540]]) {
    await resize(width,height);
    await inject(fixture);await delay(200);
    const dim=await chat.evaluate(`({width:innerWidth,scroll:document.documentElement.scrollWidth,height:innerHeight,bodyHeight:document.querySelector('.sol-chat').getBoundingClientRect().height,controls:[...document.querySelectorAll('.sol-chat-head button')].map(b=>({right:b.getBoundingClientRect().right,bottom:b.getBoundingClientRect().bottom}))})`);
    assert(dim.width>100,'chat retains available space');
    assert(dim.scroll<=dim.width+1,'chat has no horizontal overflow');
    assert(dim.controls.every(b=>b.right<=dim.width+1),'header buttons fit');
    assert(dim.bodyHeight<=dim.height+2,'composer stays inside panel');
    dimensions.push({windowWidth:width,...dim});
    await shot(chat,'chat-'+width);
  }
  await command({type:'settings.set',id:'theme',index:2});
  await host.wait(()=>chat.evaluate('document.documentElement.dataset.theme==="dark"'),'chat follows dark theme');
  await shot(chat,'chat-dark');
  await command({type:'settings.set',id:'uiScale',index:2});
  await host.wait(()=>chat.evaluate('document.body.style.zoom==="0.86"'),'chat follows interface scale');
  await command({type:'window.private'});
  await host.wait(async()=>(await state()).privateMode&&!((await state()).chatOpen),'private tab hides normal conversation');
  assert.equal((await state()).aiAvailable,false);
  assert.equal(await ui.evaluate('window.__sidebarState.tabs.find(t=>t.id===window.__sidebarState.activeId).ai'),false);
  await command({type:'chat.toggle'});assert.equal((await state()).chatOpen,false,'private tab cannot reopen normal chat');
  const before=await popup.evaluate('window.__aiMessages');
  await command({type:'ai.open',id:normal});await delay(200);
  assert.equal(await popup.evaluate('window.__aiMessages'),before,'private mode rejects cross-tab AI requests');
  await command({type:'tab.activate',id:normal});
  await host.wait(async()=>(await state()).aiAvailable,'normal AI returns');
  await command({type:'chat.toggle'});
  await host.wait(async()=>(await state()).chatOpen,'normal conversation reopens');
  await command({type:'chat.toggle'});
  await command({type:'settings.open',x:350,y:58});
  await host.wait(()=>popup.evaluate('!!document.querySelector(".settings-nav")'),'settings navigation');
  await popup.evaluate('Array.from(document.querySelectorAll(".settings-nav [role=tab]")).at(-1).click()');
  await host.wait(()=>popup.evaluate('!!document.querySelector(".ai-settings .ai-badge")'),'connected account settings');
  assert(await popup.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),'account settings fit a small window');
  assert(await popup.evaluate('[...document.querySelectorAll(".ai-settings .row-actions button")].every(b=>b.getBoundingClientRect().right<=innerWidth+1)'),'account buttons fit');
  await shot(popup,'settings-narrow');
  await writeFile(resolve(host.base,'ai-review.json'),JSON.stringify({syntheticAccount:true,realInferenceTested:false,dimensions,privateIsolation:true,formValuesExcluded:true},null,2));
  await host.close(ui);
  console.log('PASS native AI context, responsive chat at 1200/640/420, Markdown, dark theme, interface scale, private isolation; '+host.base);
} finally { await host.dispose(); await assert.rejects(access(host.profile)); }
