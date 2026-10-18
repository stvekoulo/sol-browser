// Banc natif pour le test manuel de la poignée (Computer Use), avec assertions et profil jetable.
import assert from 'node:assert/strict';
import {access,readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';
import {setTimeout as delay} from 'node:timers/promises';

export async function chatResizeCheck() {
  const host=await sidebarHost('D:/projet-navigateur/src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{visible:true});
  let ui,chat,page;
  const results=[];
  const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
  const settings=async()=>JSON.parse(await readFile(resolve(host.profile,'settings.json'),'utf8'));
  const measure=async()=>({chat:await chat.evaluate('innerWidth'),page:await page.evaluate('innerWidth'),preferred:(await settings()).ChatWidth??380});
  const open=async(width=1200)=>{
    if(ui)await host.close(ui);
    ui=await host.launch(640,width);
    await command({type:'chat.toggle'});
    chat=await host.connect('#chat');page=await host.connect(host.origin+'/2');
    await host.wait(()=>chat.evaluate('!!document.querySelector(".sol-chat")'),'chat ready');
    await chat.evaluate(`chrome.webview.dispatchEvent(new MessageEvent('message',{data:{chat:true,ready:true,account:'',busy:false,error:null,messages:[{from:'user',text:'Je peux agrandir ce panneau ?'},{from:'assistant',text:'Oui : tirez le bord gauche pour ajuster la largeur. Le texte et les commandes suivent la place disponible.'}]}}))`);
    return measure();
  };
  const record=async label=>{
    const value=await measure();
    assert(await chat.evaluate(`(()=>{const s=document.querySelector('.sol-chat-log section');return s.scrollWidth<=s.clientWidth+1})()`),'chat content stays within chosen width');
    results.push({label,...value});
    await writeFile(resolve(host.base,'chat-resize.json'),JSON.stringify(results,null,2));
    await writeFile(resolve(host.base,`chat-resize-${label}.png`),Buffer.from((await chat.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
    return value;
  };
  const finish=async()=>{try {if(ui)await host.close(ui);} finally {await host.dispose();await assert.rejects(access(host.profile));}};
  return {host,open,measure,record,finish};
}

// Pilotage du banc uniquement ; le glisser-déposer natif est exécuté séparément par Computer Use.
if(process.argv.includes('--interactive')) {
  const test=await chatResizeCheck();
  const control=resolve(test.host.base,'control.json');
  let last=0;
  try {
    console.log(JSON.stringify({base:test.host.base,ready:await test.open()}));
    const end=Date.now()+600000;
    while(Date.now()<end) {
      const request=await readFile(control,'utf8').then(JSON.parse).catch(()=>null);
      if(request&&request.id!==last) {
        last=request.id;
        if(request.action==='finish')break;
        const result=request.action==='open'?await test.open(request.width):await test.record(request.label);
        console.log(JSON.stringify({id:last,result}));
      }
      await delay(150);
    }
  } finally {await test.finish();console.log('Test browser closed and profile cleaned');}
}
