import assert from 'node:assert/strict';
import {access,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const baseline=process.argv.includes('--baseline');
const host=await sidebarHost('src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
const results=[];
try {
  const ui=await host.launch(850,1200);
  const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
  await command({type:'chat.toggle'});
  const chat=await host.connect('#chat');
  await host.wait(()=>chat.evaluate('!!document.querySelector(".sol-chat")'),'chat ready');
  // Attendre la réponse native avant de remplacer le compte par le jeu de données de test.
  await chat.evaluate(`new Promise(resolve=>{const ready=e=>{if(e.data.chat){chrome.webview.removeEventListener('message',ready);resolve(true)}};chrome.webview.addEventListener('message',ready);chrome.webview.postMessage(JSON.stringify({type:'chat.ready'}))})`);
  await command({type:'settings.set',id:'fanTrigger',index:0});
  const fixture={chat:true,ready:true,account:'test@example.invalid',busy:false,error:null,messages:[
    {from:'user',text:"Ma limite s’est réinitialisée ou pas encore ?"},
    {from:'assistant',text:'D’après la page GitLab, elle affiche **430 utilisés sur 400**, soit un dépassement de 30.\n\nLe quota mensuel se réinitialise normalement le premier du mois suivant.\n\nLien : https://example.invalid/'+('a'.repeat(160))+'\n\n```js\nconst message = "'+('x'.repeat(200))+'";\n```\n\n| Élément | Détail |\n| --- | --- |\n| Quota | '+('mot'.repeat(60))+' |'},
  ]};
  const inject=()=>chat.evaluate(`chrome.webview.dispatchEvent(new MessageEvent('message',{data:${JSON.stringify(fixture)}}))`);
  await inject();
  await host.wait(()=>chat.evaluate('!!document.querySelector(".sol-markdown pre")'),'Markdown loaded');
  await chat.evaluate(`window.__commands=[];const nativePost=chrome.webview.postMessage.bind(chrome.webview);chrome.webview.postMessage=value=>{const c=typeof value==='string'?JSON.parse(value):value;if(c.type==='chat.copy'||c.type==='chat.note'){__commands.push(c);return;}nativePost(value);}`);
  for(const theme of ['light','dark']) {
    await command({type:'settings.set',id:'theme',index:theme==='dark'?2:1});
    await host.wait(()=>chat.evaluate(`document.documentElement.dataset.theme==='${theme}'`),'theme');
    for(const width of [220,300,380,520]) {
      await chat.call('Emulation.setDeviceMetricsOverride',{width,height:800,deviceScaleFactor:1,mobile:false});
      await inject();await delay(250);
      await chat.evaluate(`document.querySelector('.sol-chat-log section').scrollTo(0,0)`);
      const bubble=await chat.evaluate(`(()=>{const b=document.querySelector('.nod');b.dispatchEvent(new PointerEvent('pointerover',{bubbles:true}));const r=b.getBoundingClientRect();return {x:r.x+10,y:r.y+10}})()`);
      await chat.call('Input.dispatchMouseEvent',{type:'mouseMoved',...bubble});
      await delay(60);
      const dimensions=await chat.evaluate(`(()=>{const log=document.querySelector('.sol-chat-log section');const rect=log.getBoundingClientRect();return {width:innerWidth,scroll:log.scrollWidth,client:log.clientWidth,left:rect.left,right:rect.right-(log.offsetWidth-log.clientWidth)*rect.width/log.offsetWidth,
        content:[...document.querySelectorAll('[data-slot="message-bubble"],.nod,.sol-markdown')].map(e=>{const r=e.getBoundingClientRect();return {left:r.left,right:r.right,width:r.width}}),
        buttons:[...document.querySelectorAll('.nod-act')].map(b=>{const r=b.getBoundingClientRect();return {left:r.left,right:r.right,top:r.top,bottom:r.bottom}})}})()`);
      results.push({theme,...dimensions});
      await writeFile(resolve(host.base,`chat-actions-${theme}-${width}.png`),Buffer.from((await chat.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
      if(!baseline) {
        assert(dimensions.scroll<=dimensions.client+1,'transcript fits without hiding horizontal content');
        assert(dimensions.content.every(r=>r.left>=dimensions.left-1&&r.right<=dimensions.right+1),'messages and Markdown stay inside transcript');
        assert.equal(dimensions.buttons.length,2,'both answer actions remain available');
        assert(dimensions.buttons.every(r=>r.left>=dimensions.left&&r.right<=dimensions.right),'actions remain inside narrow panel');
        await chat.evaluate(`document.querySelector('.nod-act').focus();document.querySelector('.nod-act').click();document.querySelectorAll('.nod-act')[1].click()`);
        assert.deepEqual(await chat.evaluate('__commands.splice(0)'),[{type:'chat.copy',id:'1'},{type:'chat.note',id:'1'}]);
        await chat.evaluate(`document.querySelector('.nod-act').scrollIntoView({block:'end'})`);
        assert(await chat.evaluate(`(()=>{const log=document.querySelector('.sol-chat-log section').getBoundingClientRect();return [...document.querySelectorAll('.nod-act')].every(b=>{const r=b.getBoundingClientRect();return r.top>=log.top&&r.bottom<=log.bottom+1})})()`),'commands can be fully reached by scrolling');
        await writeFile(resolve(host.base,`chat-actions-end-${theme}-${width}.png`),Buffer.from((await chat.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
      }
    }
  }
  if(!baseline) {
    await chat.evaluate(`document.querySelector('.nod-act').scrollIntoView({block:'center'})`);
    const point=await chat.evaluate(`(()=>{const r=document.querySelector('.nod-act').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
    for(const type of ['mouseMoved','mousePressed','mouseReleased']) await chat.call('Input.dispatchMouseEvent',{type,...point,...(type==='mouseMoved'?{}:{button:'left',clickCount:1})});
    assert.deepEqual(await chat.evaluate('__commands.splice(0)'),[{type:'chat.copy',id:'1'}],'real pointer reaches copy action');
    await chat.evaluate(`chrome.webview.dispatchEvent(new MessageEvent('message',{data:${JSON.stringify({...fixture,busy:true})}}))`);
    await host.wait(()=>chat.evaluate('document.querySelectorAll(".nod-act").length===0'),'streaming has no premature actions');
    await inject();
    await chat.evaluate(`document.querySelector('.nod')?.dispatchEvent(new PointerEvent('pointerover',{bubbles:true}))`);
    await host.wait(()=>chat.evaluate('document.querySelectorAll(".nod-act").length===2'),'completed reply actions restored');
  }
  await writeFile(resolve(host.base,'chat-actions.json'),JSON.stringify({baseline,results},null,2));
  console.log(JSON.stringify({base:host.base,baseline,results},null,2));
  await host.close(ui);
  console.log('PASS chat actions, Markdown, narrow panels, light/dark and action commands');
} finally {await host.dispose();await assert.rejects(access(host.profile));}
