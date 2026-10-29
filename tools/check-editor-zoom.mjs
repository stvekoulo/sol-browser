import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{
  serve(req,res){
    if(!['/2','/frame'].includes(req.url))return false;
    res.writeHead(200,{'Content-Type':'text/html; charset=utf-8'});
    res.end(`<title>Editor zoom</title><style>body{margin:0;background:#181818;color:white;font:20px system-ui}#editor{height:300px;background:#303030;padding:20px}iframe{width:100%;height:160px;border:0}</style>
      <div id="editor">Workflow canvas — zoom: <span id="scale">1</span></div><p id="normal">Ordinary page</p>
      ${req.url==='/2'?'<iframe src="/frame"></iframe>':''}<script>window.wheels=0;window.scale=1;
      window.addEventListener('wheel',e=>{wheels++;if(e.ctrlKey&&e.target.closest('#editor')){e.preventDefault();scale+=.1;document.querySelector('#scale').textContent=scale.toFixed(1)}},{passive:false});</script>`);return true;
  }
});
let ui;
try{
  ui=await host.launch();const page=await host.connect(host.origin+'/2');
  await host.wait(()=>page.evaluate('!!document.querySelector("#editor")'),'fixture loaded');
  const settings=async()=>JSON.parse(await readFile(resolve(host.profile,'settings.json'),'utf8'));
  const wheel=async(x,y)=>{
    await page.call('Input.dispatchMouseEvent',{type:'mouseWheel',x,y,deltaX:0,deltaY:-120,modifiers:2});
    await delay(350);
  };
  const before=(await settings()).PageZoom??1;
  await wheel(100,100);
  assert((await page.evaluate('scale'))>1,'editor receives trusted Ctrl+wheel');
  assert.equal((await settings()).PageZoom??1,before,'editor gesture leaves page zoom unchanged');
  const frame=await page.evaluate(`(()=>{const r=document.querySelector('iframe').getBoundingClientRect();return {x:r.left+60,y:r.top+60}})()`);
  await wheel(frame.x,frame.y);
  assert((await page.evaluate(`document.querySelector('iframe').contentWindow.scale`))>1,'embedded editor receives its zoom');
  assert.equal((await settings()).PageZoom??1,before,'embedded editor gesture leaves page zoom unchanged');
  await wheel(100,370);
  await host.wait(async()=> (await settings()).PageZoom>before,'ordinary page keeps browser zoom');
  const zoom=(await settings()).PageZoom;
  await page.call('Page.captureScreenshot',{format:'png'}).then(r=>writeFile(resolve(host.base,'editor-zoom.png'),Buffer.from(r.data,'base64')));
  await writeFile(resolve(host.base,'editor-zoom.json'),JSON.stringify({canvasZoom:await page.evaluate('scale'),pageZoom:zoom,editorKeptPageZoom:true}));
  await host.close(ui);console.log(`PASS native editor versus page zoom: ${host.base}`);
}finally{await host.dispose();}
