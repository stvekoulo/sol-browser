import assert from 'node:assert/strict';
import {access,cp,mkdir,readFile,readdir,rm,writeFile} from 'node:fs/promises';
import {resolve,basename,extname,relative} from 'node:path';
import {spawnSync} from 'node:child_process';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const cache=resolve('bin/video-engine-cache');
if(!await access(resolve(cache,'ffmpeg.json')).then(()=>true,()=>false)) {
  console.log('Preparing verified video engines for local fixtures…');
  const prepare=spawnSync(process.env.DOTNET_ROOT?resolve(process.env.DOTNET_ROOT,'dotnet.exe'):'dotnet',
    ['run','--project',resolve('tools/video-check-engine/VideoCheck.csproj'),'--',cache],{windowsHide:true,stdio:'inherit'});
  assert.equal(prepare.status,0,'Video engine preparation');
}
const receipt=JSON.parse(await readFile(resolve(cache,'ffmpeg.json'),'utf8'));
const ffmpeg=resolve(cache,'ffmpeg',receipt.find(e=>basename(e.File)==='ffmpeg.exe').File);
const ffprobe=resolve(ffmpeg,'../ffprobe.exe');
const fixture=resolve('bin/video-fixtures'); await mkdir(fixture,{recursive:true});
function run(args) {
  const p=spawnSync(ffmpeg,['-hide_banner','-loglevel','error','-y',...args],{windowsHide:true,encoding:'utf8',cwd:fixture});
  assert.equal(p.status,0,p.stderr);
}
for(const height of [720,1080]) {
  const width=height===720?1280:1920;
  run(['-f','lavfi','-i',`testsrc2=size=${width}x${height}:rate=12`,'-f','lavfi','-i','sine=frequency=440:sample_rate=44100',
    '-t','3','-c:v','mpeg2video','-b:v','1500k','-g','12','-c:a','aac','-f','hls','-hls_time','1','-hls_list_size','0',resolve(fixture,`${height}.m3u8`)]);
}
run(['-f','lavfi','-i','testsrc2=size=1280x720:rate=12','-f','lavfi','-i','sine=frequency=440:sample_rate=44100','-t','3','-c:v','mpeg4','-q:v','4','-c:a','aac',resolve(fixture,'direct.mp4')]);
run(['-i',resolve(fixture,'direct.mp4'),'-map','0:v','-map','0:a','-c','copy','-f','dash','-seg_duration','1',resolve(fixture,'stream.mpd')]);
await writeFile(resolve(fixture,'master.m3u8'),'#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1500000,RESOLUTION=1280x720\n720.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=2500000,RESOLUTION=1920x1080\n1080.m3u8\n');
const files=new Map(await Promise.all((await readdir(fixture)).map(async name=>[name,await readFile(resolve(fixture,name))])));
const mime=name=>({'.mp4':'video/mp4','.mpd':'application/dash+xml','.m3u8':'application/vnd.apple.mpegurl','.ts':'video/mp2t','.m4s':'video/mp4'}[extname(name)]??'application/octet-stream');
let slow=false;
const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{downloads:true,serve(req,res){
  const path=new URL(req.url,'http://fixture').pathname;
  const name=path.split('/').at(-1);
  if(files.has(name)) {
    if(!(req.headers.cookie??'').includes('sol_video_session=ok')) {res.writeHead(403);res.end();return true;}
    const bytes=files.get(name),start=Number(/^bytes=(\d+)-/.exec(req.headers.range??'')?.[1]??0);
    res.writeHead(start?206:200,{'Content-Type':mime(name),'Accept-Ranges':'bytes','Content-Length':bytes.length-start,...(start?{'Content-Range':`bytes ${start}-${bytes.length-1}/${bytes.length}`}:{})});
    if(slow&&name==='direct.mp4') {
      let offset=start;const timer=setInterval(()=>{const end=Math.min(offset+4096,bytes.length);res.write(bytes.subarray(offset,end));offset=end;if(offset>=bytes.length){clearInterval(timer);res.end();}},80);
      res.on('close',()=>clearInterval(timer));
    } else res.end(bytes.subarray(start));return true;
  }
  if(['/video','/hls','/dash','/plain','/iframe'].includes(path)) {
    const body=path==='/plain'?'<h1>Pas de vidéo</h1>':path==='/iframe'?'<iframe src="/video"></iframe>':path==='/video'?'<video controls src="/direct.mp4"></video>':`<video controls></video><script>fetch('${path==='/hls'?'/master.m3u8':'/stream.mpd'}')</script>`;
    res.writeHead(200,{'Content-Type':'text/html; charset=utf-8','Set-Cookie':'sol_video_session=ok; HttpOnly; SameSite=Lax; Path=/'});res.end(`<html><head><title>Vidéo de test Sol</title></head><body>${body}</body></html>`);return true;
  }
  return false;
}});
await mkdir(resolve(host.base,'tools'),{recursive:true});
await cp(cache,resolve(host.base,'tools/video'),{recursive:true});
let ui,popup,dock,page;
const cmd=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const state=()=>ui.evaluate('window.__sidebarState');
const active=async()=>{const s=await state();return s.tabs.find(t=>t.id===s.activeId);};
const latest=()=>dock.evaluate('window.__downloads?.items??[]');
async function navigate(path) {
  const id=(await state()).activeId;
  await cmd({type:'nav.go',id,input:host.origin+path});
  page=await host.connect(host.origin+path);
  await host.wait(()=>page.evaluate('document.readyState==="complete"'),'page ready');return id;
}
async function inspect(kind) {
  const id=(await state()).activeId;
  await popup.evaluate('window.__video=null');
  await cmd({type:'video.open',id});
  await host.wait(()=>popup.evaluate('window.__video?.phase==="ready"||window.__video?.items?.length'),'cached qualities or sources');
  let view=await popup.evaluate('window.__video');
  if(view.phase!=='ready') {
    const source=view.items.find(s=>s.kind===kind);assert(source,`Source ${kind}`);
    await cmd({type:'video.inspect',id,query:source.id});
  }
  await host.wait(async()=>{
    const p=await popup.evaluate('window.__video');
    if(p.phase==='failed')throw new Error('Video inspection failed');
    return p.phase==='ready';
  },'qualities',60000);
  view=await popup.evaluate('window.__video');
  return {id,source:{id:view.sourceId},view};
}
function probe(path) {
  const p=spawnSync(ffprobe,['-v','error','-show_streams','-of','json',path],{windowsHide:true,encoding:'utf8'});
  assert.equal(p.status,0,p.stderr);return JSON.parse(p.stdout).streams;
}
try {
  ui=await host.launch();popup=await host.connect('#popup');
  await popup.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup==='videos')window.__video=e.data})`);
  await cmd({type:'settings.set',id:'videoQuality',index:1440});
  await navigate('/hls');await host.wait(async()=>(await active()).videos>0,'HLS detection');
  assert.equal((await readdir(resolve(host.base,'downloads'))).length,0,'No automatic download');
  const hls=await inspect('hls');assert.deepEqual(hls.view.qualities,[1080,720]);assert.equal(hls.view.selected,1080);assert.equal(hls.view.preferred,1440);
  assert.match(await popup.evaluate('document.body.innerText'),/1440p/);
  const shot=await popup.call('Page.captureScreenshot');await writeFile(resolve(host.base,'video-quality-picker.png'),Buffer.from(shot.data,'base64'));
  await cmd({type:'video.download',id:hls.id,query:hls.source.id,index:720});
  dock=await host.connect('#dock');
  await dock.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup==='downloads')window.__downloads=e.data});chrome.webview.postMessage('{"type":"ui.ready"}')`);
  await host.wait(async()=>(await latest()).some(d=>d.state==='Completed'),'HLS completed',60000);
  const hlsJob=(await latest()).find(d=>d.state==='Completed');
  const hlsFiles=await readdir(resolve(host.base,'downloads'));assert.equal(hlsFiles.length,1);
  const hlsStreams=probe(resolve(host.base,'downloads',hlsFiles[0]));assert(hlsStreams.some(s=>s.height===720));assert(hlsStreams.some(s=>s.codec_type==='audio'));
  await navigate('/dash');await host.wait(async()=>(await active()).videos>0,'DASH detection');
  const dash=await inspect('dash');await cmd({type:'video.download',id:dash.id,query:dash.source.id,index:dash.view.selected});
  await host.wait(async()=>(await latest()).some(d=>d.state==='Completed'&&d.id!==hlsJob.id),'DASH completed',60000);
  const dashFiles=(await readdir(resolve(host.base,'downloads'))).filter(p=>!hlsFiles.includes(p));assert.equal(dashFiles.length,1);
  const dashStreams=probe(resolve(host.base,'downloads',dashFiles[0]));assert(dashStreams.some(s=>s.codec_type==='video'));assert(dashStreams.some(s=>s.codec_type==='audio'));
  await navigate('/iframe');await host.wait(async()=>(await active()).videos>0,'iframe detection');
  await navigate('/video');await host.wait(async()=>(await active()).videos>0,'direct detection');
  await cmd({type:'settings.set',id:'fanEnabled',index:1});await cmd({type:'settings.set',id:'fanTrigger',index:2});
  const videoPoint=await page.evaluate(`(()=>{const r=document.querySelector('video').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+30}})()`);
  for(const type of ['mouseMoved','mousePressed','mouseReleased'])await page.call('Input.dispatchMouseEvent',{type,...videoPoint,...(type==='mouseMoved'?{}:{button:'right',clickCount:1})});
  const downloadButton=await host.wait(async()=>{
    const tree=(await page.call('DOM.getDocument',{depth:-1,pierce:true})).root;
    const find=n=>n.localName==='button'&&n.attributes?.some((v,i)=>v==='data-action'&&n.attributes[i+1]==='download')?n:[...(n.children??[]),...(n.shadowRoots??[])].map(find).find(Boolean);
    return find(tree);
  },'video fan download shortcut');
  await delay(650);
  const box=(await page.call('DOM.getBoxModel',{nodeId:downloadButton.nodeId})).model.border;
  const point={x:(box[0]+box[4])/2,y:(box[1]+box[5])/2};
  await writeFile(resolve(host.base,'video-fan.png'),Buffer.from((await page.call('Page.captureScreenshot')).data,'base64'));
  for(const type of ['mouseMoved','mousePressed','mouseReleased'])await page.call('Input.dispatchMouseEvent',{type,...point,...(type==='mouseMoved'?{}:{button:'left',clickCount:1})});
  await host.wait(()=>popup.evaluate('window.__video.phase==="ready"||window.__video.items.length>0'),'fan opens quality flow');
  await cmd({type:'popup.close'});
  const direct=await inspect('file');slow=true;
  await cmd({type:'video.download',id:direct.id,query:direct.source.id,index:direct.view.selected});
  const job=await host.wait(async()=>(await latest()).find(d=>d.state==='InProgress'&&d.received>0),'direct progression');
  await cmd({type:'download.pause',id:job.id});
  await host.wait(async()=>(await latest()).find(d=>d.id===job.id)?.paused,'video pause');
  await host.wait(async()=>(await latest()).find(d=>d.id===job.id)?.canResume,'video resumable');
  await cmd({type:'download.resume',id:job.id});
  await host.wait(async()=>(await latest()).find(d=>d.id===job.id)?.state==='InProgress','video resume');
  await cmd({type:'download.cancel',id:job.id});
  await host.wait(async()=>(await latest()).find(d=>d.id===job.id)?.canceled,'video cancel');
  await navigate('/plain');await delay(600);assert.equal((await active()).videos,0);
  await navigate('/video');await host.wait(async()=>(await active()).videos>0,'video returns');
  await page.evaluate(`document.querySelector('video').dispatchEvent(new Event('encrypted',{bubbles:true}))`);
  await host.wait(async()=>(await active()).videoProtected,'DRM detection');
  await cmd({type:'video.open',id:(await state()).activeId});
  await host.wait(()=>popup.evaluate('window.__video.protectedMedia'),'DRM blocked');
  assert.equal(await popup.evaluate('document.querySelectorAll(".video-source").length'),0);
  await cmd({type:'settings.set',id:'videoDownloads',index:0});
  await host.wait(async()=>(await active()).videos===0,'disable video detection');
  const settings=JSON.parse(await readFile(resolve(host.profile,'settings.json'),'utf8'));assert.equal(settings.VideoQuality,1440);assert.equal(settings.VideoDownloads,false);
  assert.equal((await readdir(resolve(host.base,'tools/video'))).filter(n=>n.startsWith('cookies-')).length,0,'Temporary cookie jars removed');
  await writeFile(resolve(host.base,'video-verification.json'),JSON.stringify({hls:{selected:720,available:hls.view.qualities,streams:hlsStreams.map(s=>({type:s.codec_type,height:s.height}))},dash:{streams:dashStreams.map(s=>({type:s.codec_type,height:s.height}))},checks:['no-auto-download','preference-fallback','iframe','video-fan-native-click','progress','pause','resume','cancel','navigation-clears','DRM-block','settings-persist','session-cookie','cookie-cleanup'],toolsVerified:true},null,2));
  console.log(`Video checks passed: ${host.base}`);await host.close(ui);
} finally {
  await host.dispose();
  assert.match(relative(resolve('bin'),host.base),/^sidebar-check-\d+$/);
  await rm(resolve(host.base,'tools'),{recursive:true,force:true,maxRetries:10,retryDelay:200});
}
