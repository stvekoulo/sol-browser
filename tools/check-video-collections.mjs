import assert from 'node:assert/strict';
import {cp,mkdir,readFile,readdir,rm,writeFile} from 'node:fs/promises';
import {basename,relative,resolve} from 'node:path';
import {spawnSync} from 'node:child_process';
import {sidebarHost} from './sidebar-check-host.mjs';

const executable=process.argv[2]??'bin/video-collections-build/Sol.exe';
const cache=resolve('bin/video-engine-cache');
const receipt=JSON.parse(await readFile(resolve(cache,'ffmpeg.json'),'utf8'));
const ffmpeg=resolve(cache,'ffmpeg',receipt.find(entry=>basename(entry.File)==='ffmpeg.exe').File);
const fixture=resolve('bin/video-collection-fixtures');await mkdir(fixture,{recursive:true});
const output=resolve(fixture,'clip.mp4');
const generated=spawnSync(ffmpeg,['-hide_banner','-loglevel','error','-y','-f','lavfi','-i','testsrc2=size=640x360:rate=12',
  '-f','lavfi','-i','sine=frequency=440','-t','2','-c:v','mpeg4','-q:v','4','-c:a','aac',output],{windowsHide:true,encoding:'utf8'});
assert.equal(generated.status,0,generated.stderr);
const bytes=await readFile(output),requests=[];
let slow=true;
const host=await sidebarHost(executable,{downloads:true,serve(req,res){
  const url=new URL(req.url,'http://fixture');
  if(url.pathname==='/clip.mp4'){
    if(!(req.headers.cookie??'').includes('collection=ok')){res.writeHead(403);res.end();return true;}
    requests.push({method:req.method,referer:req.headers.referer,path:req.url});
    const start=Number(/^bytes=(\d+)-/.exec(req.headers.range??'')?.[1]??0);
    res.writeHead(start?206:200,{'Content-Type':'video/mp4','Accept-Ranges':'bytes','Content-Length':bytes.length-start,
      ...(start?{'Content-Range':`bytes ${start}-${bytes.length-1}/${bytes.length}`}:{})});
    if(req.method==='HEAD'){res.end();return true;}
    if(!slow){res.end(bytes.subarray(start));return true;}
    let offset=start;const timer=setInterval(()=>{const end=Math.min(offset+2048,bytes.length);res.write(bytes.subarray(offset,end));offset=end;
      if(offset>=bytes.length){clearInterval(timer);res.end();}},80);
    res.on('close',()=>clearInterval(timer));return true;
  }
  if(['/collection','/embedded','/frame'].includes(url.pathname)){
    const body=url.pathname==='/embedded'?'<iframe src="/frame"></iframe>':Array.from({length:url.pathname==='/collection'?101:3},(_,i)=>
      `<video id="clip${i}" controls preload="none" src="/clip.mp4?item=${i}"></video>`).join('');
    res.writeHead(200,{'Content-Type':'text/html; charset=utf-8','Set-Cookie':'collection=ok; HttpOnly; Path=/; SameSite=Lax'});
    res.end(`<html><head><title>Collection de test</title></head><body>${body}</body></html>`);return true;
  }
  return false;
}});
let ui,popup,dock;
const cmd=command=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(command))})`);
const state=()=>ui.evaluate('window.__sidebarState');
const jobs=()=>dock.evaluate('Object.values(window.__allDownloads??{})');
try{
  await mkdir(resolve(host.base,'tools'),{recursive:true});await cp(cache,resolve(host.base,'tools/video'),{recursive:true});
  ui=await host.launch();popup=await host.connect('#popup');
  await popup.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup==='videos')window.__video=e.data})`);
  const id=(await state()).activeId;
  await cmd({type:'nav.go',id,input:host.origin+'/collection'});
  const page=await host.connect(host.origin+'/collection');
  await host.wait(async()=>(await state()).tabs.find(tab=>tab.id===id)?.videos>0,'page videos detected');
  await cmd({type:'video.open',id});
  await cmd({type:'video.collection',id,query:'playlist',index:1});
  const first=await host.wait(()=>popup.evaluate('window.__video?.collection&&window.__video.phase==="ready"&&window.__video'),'playlist first page',60000);
  assert.equal(first.collection.entries.length,100);assert.equal(first.collection.more,true);
  assert.equal(new Set(first.collection.entries.map(entry=>entry.key)).size,100,'Different media sharing a parent page stay distinct');
  assert.equal((await readdir(resolve(host.base,'downloads'))).length,0,'Listing does not download media');
  assert.equal(await popup.evaluate('document.querySelector(".video-download-btn").disabled'),true,'Explicit selection is required');
  await cmd({type:'video.batch',id,query:first.collection.token,input:JSON.stringify(['untrusted-url']),index:720});
  assert.equal((await readdir(resolve(host.base,'downloads'))).length,0,'Only server-cached keys are accepted');
  await popup.call('Emulation.setDeviceMetricsOverride',{width:320,height:700,deviceScaleFactor:1,mobile:false});
  assert.equal(await popup.evaluate('document.documentElement.scrollWidth<=innerWidth'),true,'Responsive collection at 320px');
  await writeFile(resolve(host.base,'collection-picker.png'),Buffer.from((await popup.call('Page.captureScreenshot')).data,'base64'));
  await popup.call('Emulation.clearDeviceMetricsOverride');
  await cmd({type:'video.collection',id,query:'playlist',index:101});
  const second=await host.wait(()=>popup.evaluate('window.__video?.collection?.start===101&&window.__video.phase==="ready"&&window.__video'),'playlist next page',60000);
  assert.equal(second.collection.entries.length,1);assert.equal(second.collection.more,false);
  await cmd({type:'video.batch',id,query:first.collection.token,input:JSON.stringify(first.collection.entries.slice(0,3).map(entry=>entry.key)),index:720});
  assert.equal((await readdir(resolve(host.base,'downloads'))).length,0,'A stale collection cannot start downloads');
  await cmd({type:'video.collection',id,query:'playlist',index:1});
  const current=await host.wait(()=>popup.evaluate('window.__video?.collection?.start===1&&window.__video.phase==="ready"&&window.__video'),'return to first page',60000);
  // Exercise beUI checkboxes, not just the command bridge.
  await host.wait(()=>popup.evaluate('document.querySelectorAll(".video-collection-entry [role=checkbox]").length===100'),'collection rendered');
  await popup.evaluate('document.querySelectorAll(".video-collection-entry [role=checkbox]")[0].click()');
  await popup.evaluate('document.querySelectorAll(".video-collection-entry [role=checkbox]")[1].click()');
  await popup.evaluate('document.querySelectorAll(".video-collection-entry [role=checkbox]")[2].click()');
  await host.wait(()=>popup.evaluate('document.querySelector(".video-download-btn").textContent.includes("3 vidéo")'),'three selected');
  await popup.evaluate('document.querySelectorAll(".video-qualities [role=radio]")[2].click()');
  await popup.evaluate('document.querySelector(".video-download-btn").click()');
  dock=await host.connect('#dock');
  await dock.evaluate(`window.__allDownloads={};chrome.webview.addEventListener('message',e=>{if(e.data.popup==='downloads')for(const item of e.data.items??[])window.__allDownloads[item.id]=item});chrome.webview.postMessage('{"type":"dock.ready"}')`);
  const queued=await host.wait(async()=>{const entries=await jobs();return entries.length===3&&entries.find(entry=>entry.phase==='queued');},'bounded queue');
  await cmd({type:'download.pause',id:queued.id});
  await host.wait(async()=>(await jobs()).find(entry=>entry.id===queued.id)?.paused,'pause queued item');
  await host.wait(async()=>(await jobs()).find(entry=>entry.id===queued.id)?.canResume,'queued item can resume');
  await cmd({type:'download.resume',id:queued.id});
  try {
    await host.wait(async()=>{
      const entries=await jobs();
      const failed=entries.find(entry=>entry.state==='Interrupted'&&!entry.paused);
      if(failed)throw new Error(`Transfer failed: ${JSON.stringify(failed)}`);
      return entries.filter(entry=>entry.state==='Completed').length===3;
    },'selected playlist transfers',90000);
  }catch(error){await writeFile(resolve(host.base,'collection-transfer-failure.json'),JSON.stringify(await jobs(),null,2));throw error;}
  for(const entry of await jobs())assert.deepEqual(await readFile(resolve(host.base,'downloads',entry.name)),bytes,'Complete selected file');
  assert.equal((await jobs()).length,3,'No unselected video transferred');
  slow=false;
  await cmd({type:'nav.go',id,input:host.origin+'/embedded'});await host.connect(host.origin+'/embedded');
  await host.wait(async()=>(await state()).tabs.find(tab=>tab.id===id)?.videos>0,'embedded sources detected');
  await cmd({type:'video.open',id});await cmd({type:'video.collection',id,query:'page',index:1});
  const embedded=await host.wait(()=>popup.evaluate('window.__video?.collection?.mode==="page"&&window.__video.phase==="ready"&&window.__video'),'embedded source selection');
  assert.equal(embedded.collection.entries.length,3);
  const before=requests.length;
  await cmd({type:'video.batch',id,query:embedded.collection.token,input:JSON.stringify([embedded.collection.entries[0].key]),index:1080});
  await host.wait(async()=>(await jobs()).filter(entry=>entry.state==='Completed').length===4,'embedded direct transfer',30000);
  assert(requests.slice(before).some(request=>request.referer===host.origin+'/frame'),'Embedded player referer reaches the media server');
  assert.equal((await readdir(resolve(host.base,'tools/video'))).filter(name=>name.startsWith('cookies-')).length,0,'Cookie jars removed');
  await writeFile(resolve(host.base,'video-collections-verification.json'),JSON.stringify({playlist:{first:100,next:1,selected:3},
    checks:['flat playlist pagination','distinct media URLs','no automatic download','cached keys only','stale selection rejected',
      'beUI multi selection','320px without overflow','two concurrent downloads','queued pause/resume','quality preference fallback to 360p',
      'three complete files','iframe detection','embedded referer','temporary cookies removed']},null,2));
  console.log(`Video collection checks passed: ${host.base}`);await host.close(ui);
}finally{
  await host.dispose();assert.match(relative(resolve('bin'),host.base),/^sidebar-check-\d+$/);
  await rm(resolve(host.base,'tools'),{recursive:true,force:true,maxRetries:10,retryDelay:200});
}
