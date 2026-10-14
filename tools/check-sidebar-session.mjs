import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';
const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
let ui;
try{
  ui=await host.launch(650);
  for(const index of [0,1,2]){
    await ui.evaluate(`chrome.webview.postMessage(JSON.stringify({type:'settings.set',id:'uiScale',index:${index}}))`);await delay(150);
    for(const width of [300,220,180]){
      await ui.call('Emulation.setDeviceMetricsOverride',{width,height:650,deviceScaleFactor:1,mobile:false});
      for(const label of ['Session actuelle','Session · Compte professionnel avec un nom très long']){
        await ui.evaluate(`document.querySelector('.sidebar-session > span:not(.sol-animated-icon)').textContent=${JSON.stringify(label)}`);await delay(100);
        const b=await ui.evaluate(`(()=>{const button=document.querySelector('.sidebar-session'),icon=button.querySelector('.sol-animated-icon'),text=button.querySelector('span:not(.sol-animated-icon)'),arrow=button.querySelector(':scope > svg'),rect=el=>{const r=el.getBoundingClientRect();return {left:r.left,right:r.right,width:r.width,center:r.top+r.height/2}};return {button:rect(button),icon:rect(icon),text:rect(text),arrow:rect(arrow),scale:Number(getComputedStyle(document.body).zoom),flex:getComputedStyle(icon).flexGrow,viewport:innerWidth,labelStyle:getComputedStyle(text).textOverflow}})()`);
        assert.equal(b.flex,'0','animated session icon must not grow like the label');assert.equal(b.labelStyle,'ellipsis');
        assert(Math.abs(b.icon.width-13*b.scale)<1,JSON.stringify(b));
        assert(Math.abs(b.icon.left-b.button.left-11*b.scale)<1,JSON.stringify(b));
        assert(Math.abs(b.text.left-b.icon.right-6*b.scale)<1,JSON.stringify(b));
        assert(b.text.right<=b.arrow.left+1&&b.arrow.right<=b.button.right+1&&b.button.right<=b.viewport+1,JSON.stringify(b));
        assert(Math.abs(b.icon.center-b.text.center)<1&&Math.abs(b.arrow.center-b.text.center)<1,JSON.stringify(b));
      }
    }
  }
  const image=await ui.call('Page.captureScreenshot',{format:'png'});await writeFile(resolve(host.base,'session-narrow.png'),Buffer.from(image.data,'base64'));
  await ui.evaluate(`document.querySelector('.sidebar-session').click()`);const popup=await host.connect('#popup');
  await host.wait(()=>popup.evaluate('!!document.querySelector(".sessions-popup")').catch(()=>false),'session menu opens');
  console.log(JSON.stringify({passed:'native sidebar: session icon fixed width and left alignment, vertically aligned label/chevron, long label ellipsis at three widths and scales, session menu opens',evidence:host.base}));
}finally{if(ui)await host.close(ui).catch(()=>{});await host.dispose();}
