import assert from 'node:assert/strict';
import {readFile,writeFile,rm} from 'node:fs/promises';
import {resolve,relative} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';

const bytes=Buffer.alloc(131072,37),times={};
let downloadRequest=0;
const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{
  downloads:true,serve(req,res){
    const path=new URL(req.url,'http://fixture').pathname;
    if(path==='/clip.mp4'){
      assert.match(req.headers.cookie??'',/instant=ok/);
      downloadRequest=performance.now();
      res.writeHead(200,{'Content-Type':'video/mp4','Content-Length':bytes.length});res.end(bytes);return true;
    }
    if(path==='/master.m3u8'){
      res.writeHead(200,{'Content-Type':'application/vnd.apple.mpegurl'});
      res.end('#EXTM3U\n#EXT-X-STREAM-INF:RESOLUTION=1280x720\n720.m3u8\n#EXT-X-STREAM-INF:RESOLUTION=1920x1080\n1080.m3u8');return true;
    }
    if(path==='/instant'||path==='/manifest'){
      const body=path==='/instant'?`<div id="movie_player"></div><script>
        document.querySelector('#movie_player').getPlayerResponse=()=>({streamingData:{formats:[
          {itag:22,url:location.origin+'/clip.mp4',height:720,mimeType:'video/mp4',audioQuality:'AUDIO_QUALITY_MEDIUM'},
          {itag:18,url:location.origin+'/clip.mp4',height:1080,mimeType:'video/mp4',audioQuality:'AUDIO_QUALITY_MEDIUM'}]}});
        </script>`:`<video></video><script>fetch('/master.m3u8')</script>`;
      res.writeHead(200,{'Content-Type':'text/html; charset=utf-8','Set-Cookie':'instant=ok; HttpOnly; SameSite=Lax; Path=/'});
      res.end(`<html><head><title>Instant video fixture</title></head><body>${body}</body></html>`);return true;
    }
    return false;
  }
});
let ui,popup,dock;
const cmd=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const state=()=>ui.evaluate('window.__sidebarState');
async function open(path){
  const id=(await state()).activeId,start=performance.now();
  await cmd({type:'nav.go',id,input:host.origin+path});
  const page=await host.connect(host.origin+path);
  await host.wait(()=>page.evaluate('document.readyState==="complete"'),'new document',5000);
  await host.wait(async()=>{const s=await state();return s.tabs.find(t=>t.id===id)?.videos>0;},'automatic detection',5000);
  await cmd({type:'video.open',id});
  const view=await host.wait(()=>popup.evaluate('window.__video?.phase==="ready"&&window.__video'), 'immediate qualities',5000);
  times[path]=Math.round(performance.now()-start);
  assert(times[path]<5000,`Detection and quality UI ${times[path]}ms`);
  return {id,view};
}
try{
  ui=await host.launch();popup=await host.connect('#popup');
  await popup.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup==='videos')window.__video=e.data})`);
  await cmd({type:'settings.set',id:'videoQuality',index:1440});
  const player=await open('/instant');
  assert.deepEqual(player.view.qualities,[1080,720]);assert.equal(player.view.selected,1080);
  assert.equal(downloadRequest,0,'No transfer before the click');
  await host.wait(()=>popup.evaluate('document.querySelectorAll(".video-qualities [role=radio]").length===2'),'beUI quality choices');
  await popup.evaluate('document.querySelector(".video-qualities [aria-checked=true]").focus()');
  await popup.call('Input.dispatchKeyEvent',{type:'keyDown',key:'ArrowRight',code:'ArrowRight',windowsVirtualKeyCode:39});
  await host.wait(()=>popup.evaluate('document.querySelector(".video-quality-item.is-selected").textContent.includes("720p")'),'keyboard quality selection');
  const source=await host.connect(host.origin+'/instant');
  await source.evaluate(`document.querySelector('#movie_player').getPlayerResponse=()=>({streamingData:{formats:[2160,1080,720].map(height=>({itag:height,url:location.origin+'/clip.mp4',height,mimeType:'video/mp4',audioQuality:'AUDIO_QUALITY_MEDIUM'}))}});document.dispatchEvent(new Event('yt-player-updated'))`);
  await host.wait(()=>popup.evaluate('document.querySelectorAll(".video-qualities [role=radio]").length===3'),'live quality catalogue');
  assert.equal(await popup.evaluate('document.querySelector(".video-quality-item.is-selected").textContent.includes("720p")'),true,'Live updates preserve manual selection');
  assert.equal(await popup.evaluate('Array.from(document.querySelectorAll(".video-qualities [role=radio]")).filter(b=>b.tabIndex===0).length'),1,'One keyboard tab stop');
  await popup.call('Emulation.setDeviceMetricsOverride',{width:320,height:650,deviceScaleFactor:1,mobile:false});
  await host.wait(()=>popup.evaluate('innerWidth===320'),'compact picker');
  assert.equal(await popup.evaluate('document.documentElement.scrollWidth<=innerWidth'),true,'No horizontal overflow at 320px');
  assert.equal(await popup.evaluate('document.querySelectorAll(".video-picker").length'),1,'Single picker surface');
  assert.equal(await popup.evaluate('getComputedStyle(document.querySelector(".video-qualities [aria-checked=false]")).borderTopStyle'),'solid','Unselected controls remain visible');
  const clip=await popup.evaluate('({x:0,y:0,width:innerWidth,height:Math.min(innerHeight,document.querySelector(".video-picker").getBoundingClientRect().height+3),scale:1})');
  await writeFile(resolve(host.base,'video-picker-compact.png'),Buffer.from((await popup.call('Page.captureScreenshot',{clip})).data,'base64'));
  await cmd({type:'settings.set',id:'theme',index:2});
  await host.wait(()=>popup.evaluate('document.documentElement.dataset.theme==="dark"'),'native dark theme');
  await writeFile(resolve(host.base,'video-picker-dark.png'),Buffer.from((await popup.call('Page.captureScreenshot',{clip})).data,'base64'));
  await popup.call('Emulation.clearDeviceMetricsOverride');
  const start=performance.now();
  await popup.evaluate('document.querySelector(".video-download-btn").click()');
  await host.wait(()=>downloadRequest>start,'first HTTP bytes',5000);
  times.downloadRequest=Math.round(downloadRequest-start);
  dock=await host.connect('#dock');
  await dock.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup==='downloads')window.__downloads=e.data});chrome.webview.postMessage('{"type":"dock.ready"}')`);
  const job=await host.wait(()=>dock.evaluate('window.__downloads?.items?.find(d=>d.state==="Completed")'),'direct transfer with cold engines',5000);
  times.downloadRequest=Math.round(downloadRequest-start);times.downloadCompleted=Math.round(performance.now()-start);
  assert(times.downloadCompleted<5000);assert(downloadRequest>start);
  assert.deepEqual(await readFile(resolve(host.base,'downloads',job.name)),bytes);
  await popup.evaluate('window.__video=null');
  const manifest=await open('/manifest');assert.deepEqual(manifest.view.qualities,[1080,720]);
  await writeFile(resolve(host.base,'video-instant.png'),Buffer.from((await popup.call('Page.captureScreenshot')).data,'base64'));
  await writeFile(resolve(host.base,'video-instant-verification.json'),JSON.stringify({times,checks:['automatic-player-metadata','automatic-HLS-manifest','no-probe-click','cold-engine-direct-download','HttpOnly-cookie','quality-default-fallback','beUI keyboard selection','live updates preserve choice','compact picker without overflow','download button uses manual quality','downloaded-bytes']},null,2));
  console.log(JSON.stringify({report:host.base,times}));
  await host.close(ui);
}finally{
  await host.dispose();assert.match(relative(resolve('bin'),host.base),/^sidebar-check-\d+$/);
  await rm(resolve(host.base,'tools'),{recursive:true,force:true,maxRetries:10,retryDelay:200});
}
