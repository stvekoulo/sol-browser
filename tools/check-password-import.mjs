import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
import {createDecipheriv} from 'node:crypto';
import {access,readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {DatabaseSync} from 'node:sqlite';
import {sidebarHost} from './sidebar-check-host.mjs';

// Only the isolated test profile and these dummy credentials are read or written.
const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{serve(req,res){
  if(req.url!=='/login')return false;
  res.writeHead(200,{'Content-Type':'text/html'});
  res.end('<title>Import test</title><form action="/signed-in" method="post"><input name="username" autocomplete="username"><input name="password" type="password" autocomplete="current-password"><button>Sign in</button></form>');return true;
}});
const exists=path=>access(path).then(()=>true,()=>false);
function dpapi(bytes,operation){
  const script=`Add-Type -AssemblyName System.Security; $bytes=[Convert]::FromBase64String([Console]::In.ReadToEnd().Trim()); [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::${operation}($bytes,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))`;
  const result=spawnSync('powershell.exe',['-NoProfile','-NonInteractive','-Command',script],{input:Buffer.from(bytes).toString('base64'),encoding:'utf8',windowsHide:true,timeout:15000});
  assert.equal(result.status,0,result.stderr);return Buffer.from(result.stdout.trim(),'base64');
}
let sidebar;
try{
  sidebar=await host.launch();await host.close(sidebar);sidebar=null;
  const pending=resolve(host.profile,'passwords.pending'),engine=resolve(host.profile,'webview2/EBWebView');
  const login={OriginUrl:host.origin+'/login',ActionUrl:'',UsernameElement:'',Username:'sol-import-test',PasswordElement:'',Password:'dummy-secret-é1',SignonRealm:host.origin+'/'};
  await writeFile(pending,dpapi(JSON.stringify([login]),'Protect'));
  sidebar=await host.launch();
  assert.equal(await exists(pending),false,'startup consumes the protected import queue');
  const tab=await sidebar.evaluate('window.__sidebarState.activeId');
  await sidebar.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify({type:'nav.go',id:tab,input:login.OriginUrl}))})`);
  const page=await host.connect(login.OriginUrl);
  await host.wait(()=>page.evaluate('!!document.querySelector("input[type=password]")'),'login form ready');
  await host.wait(()=>page.evaluate(`document.querySelector('[name=username]').value==='sol-import-test'&&document.querySelector('[name=password]').value==='dummy-secret-é1'`),'imported password fills a real form',10000);
  await sidebar.evaluate(`chrome.webview.postMessage('{"type":"welcome.open"}')`);
  const welcome=await host.connect('#welcome');
  await host.wait(()=>welcome.evaluate('!!document.querySelector(".welcome-list")'),'import screen ready');
  await welcome.evaluate(`(()=>{const channel=chrome.webview,post=channel.postMessage.bind(channel);window.__importCommands=[];channel.postMessage=raw=>{const c=JSON.parse(raw);if(c.type.startsWith('import.'))window.__importCommands.push(c);else post(raw)};window.__importFixture=data=>channel.dispatchEvent(new MessageEvent('message',{data}));window.__importFixture({welcome:'state',first:false,name:'Test',theme:0,font:0,language:1,engine:0,engines:['Google'],pip:false,browsers:[{id:'test',name:'Navigateur de test',profiles:[{folder:'Default',name:'Test'}]}]})})()`);
  await host.wait(()=>welcome.evaluate('!!document.querySelector(".welcome-row .pill-action")'),'source fixture ready');
  await welcome.evaluate(`document.querySelector('.welcome-row .pill-action').click()`);
  await host.wait(()=>welcome.evaluate('document.querySelector(".welcome-row .pill-action").disabled'),'import shows progress');
  await welcome.evaluate(`window.__importFixture({welcome:'imported',id:'test',folder:'Default',bookmarks:0,history:0,passwords:0,locked:0,error:'Import incomplet. Fermez le navigateur source et réessayez.'})`);
  await host.wait(()=>welcome.evaluate('!document.querySelector(".welcome-row .pill-action").disabled&&!!document.querySelector(".welcome-row [role=status]")'),'failed import releases progress and offers retry');
  await welcome.evaluate(`window.__importFixture({welcome:'imported',id:'test',folder:'Default',bookmarks:0,history:0,passwords:0,locked:2})`);
  await host.wait(()=>welcome.evaluate('document.querySelector(".welcome-row .pill-action").textContent.includes("CSV")'),'protected passwords offer CSV');
  await welcome.evaluate(`document.querySelector('.welcome-row .pill-action').click()`);
  assert.equal(await welcome.evaluate('window.__importCommands.at(-1).type'),'import.csv');
  await welcome.evaluate(`new Promise(r=>setTimeout(r,700))`);
  for(const [width,height] of [[1440,850],[1000,780],[900,600],[640,600],[390,600],[320,360]]){
    await welcome.call('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
    await welcome.evaluate('new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)))');
    const layout=await welcome.evaluate(`(()=>{const card=document.querySelector('.welcome-card').getBoundingClientRect(),foot=document.querySelector('.welcome-foot').getBoundingClientRect();const selectors=['.welcome h1','.welcome-lead','.welcome-row-text','.welcome-row-title','.welcome-row-sub','.welcome-row .pill-action'];return {fits:document.documentElement.scrollWidth<=innerWidth&&card.left>=0&&card.right<=innerWidth+1&&card.top>=0&&foot.bottom<=innerHeight+1&&document.querySelector('.welcome-step').scrollWidth<=document.querySelector('.welcome-step').clientWidth+1,clipped:selectors.filter(s=>{const el=document.querySelector(s);return el.scrollWidth>el.clientWidth+1||el.scrollHeight>el.clientHeight+1})}})()`);
    assert.equal(layout.fits,true,`import card and footer fit ${width}x${height}`);
    assert.deepEqual(layout.clipped,[],`import text stays readable at ${width}x${height}`);
    await welcome.call('Page.captureScreenshot',{format:'png'}).then(r=>writeFile(resolve(host.base,`password-import-${width}.png`),Buffer.from(r.data,'base64')));
  }
  for(const width of [1000,390,320]){
    await welcome.call('Emulation.setDeviceMetricsOverride',{width,height:360,deviceScaleFactor:1,mobile:false});
    await welcome.evaluate(`window.__importFixture({welcome:'state',first:true,name:'Test',theme:0,font:0,language:1,engine:0,engines:['Google','DuckDuckGo','Bing'],pip:false,browsers:[]})`);
    for(let step=0;step<5;step++){
      await welcome.evaluate('new Promise(r=>setTimeout(r,400))');
      assert.equal(await welcome.evaluate(`(()=>{const body=document.querySelector('.welcome-step'),foot=document.querySelector('.welcome-foot').getBoundingClientRect();return document.documentElement.scrollWidth<=innerWidth&&body.scrollWidth<=body.clientWidth+1&&foot.right<=innerWidth+1&&foot.bottom<=innerHeight+1})()`),true,`setup step ${step} fits ${width}x360`);
      if(step<4)await welcome.evaluate(`document.querySelector('.welcome-next').click()`);
    }
  }
  await welcome.call('Emulation.clearDeviceMetricsOverride');
  await host.close(sidebar);sidebar=null;
  const state=JSON.parse(await readFile(resolve(engine,'Local State'),'utf8'));
  const key=dpapi(Buffer.from(state.os_crypt.encrypted_key,'base64').subarray(5),'Unprotect');
  const db=new DatabaseSync(resolve(engine,'Default/Login Data'),{readOnly:true});
  try{
    const rows=db.prepare('SELECT username_value,password_value FROM logins WHERE signon_realm=?').all(login.SignonRealm);
    assert.equal(rows.length,1);assert.equal(rows[0].username_value,login.Username);
    const secret=Buffer.from(rows[0].password_value);assert.equal(secret.subarray(0,3).toString(),'v10');
    const decipher=createDecipheriv('aes-256-gcm',key,secret.subarray(3,15));decipher.setAuthTag(secret.subarray(-16));
    assert.equal(Buffer.concat([decipher.update(secret.subarray(15,-16)),decipher.final()]).toString(),login.Password);
  }finally{db.close();}
  console.log(JSON.stringify({passed:'protected import survives restart, fills a real login form; failed import releases progress; protected passwords offer CSV; import text and footer fit 320–1440px',evidence:host.base}));
}finally{
  if(sidebar)await host.close(sidebar).catch(()=>{});
  await host.dispose();assert.equal(await exists(resolve(host.base,'profiles')),false,'test profiles and caches removed');
}
