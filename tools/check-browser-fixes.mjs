// Vérifie dans une vraie fenêtre de Sol : correcteur en ligne, cadres d'autres sites, onglet planté en arrière-plan,
// téléchargement qui survit à la fermeture de son onglet, pièces jointes de Sol AI et relance après perte du moteur.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {writeFile} from 'node:fs/promises';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const exe=process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe';
const capture=process.argv[3];
const payload=Buffer.alloc(3*1024*1024,7);
const ok=label=>console.log(`ok - ${label}`);
let port=0;
const host=await sidebarHost(exe,{downloads:true,serve(req,res){
  if(req.url==='/editor'){res.writeHead(200,{'Content-Type':'text/html'});res.end('<title>Editor</title><textarea id=t style="width:600px;height:120px;font:18px system-ui"></textarea><input id=user name=login_user value="Les enfant mange des pomme">');return true;}
  if(req.url==='/framed'){const inner=host.origin.replace('127.0.0.1','localhost')+'/editor';res.writeHead(200,{'Content-Type':'text/html'});res.end(`<title>Framed</title><iframe id=f src="${inner}" width=700 height=300></iframe>`);return true;}
  if(req.url.startsWith('/slow/')){
    res.writeHead(200,{'Content-Type':'application/octet-stream','Content-Length':payload.length});
    let offset=0;const timer=setInterval(()=>{if(offset>=payload.length){clearInterval(timer);res.end();return;}res.write(payload.subarray(offset,offset+=200*1024));},200);
    req.on('close',()=>clearInterval(timer));return true;
  }
  return false;
}});
const killTree=pid=>{try{execFileSync('taskkill',['/PID',String(pid),'/T','/F'],{stdio:'ignore'});}catch{/* déjà terminé */}};
const processes=filter=>JSON.parse(execFileSync('pwsh',['-NoProfile','-Command',`@(Get-CimInstance Win32_Process -Filter "${filter}" | Select-Object ProcessId,CommandLine) | ConvertTo-Json -Compress -AsArray`],{encoding:'utf8',maxBuffer:1<<26})||'[]');
try{
  const ui=await host.launch(850,1200);
  const profileName=host.profile.split(/[\\/]/).pop();
  const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
  const state=()=>ui.evaluate('window.__sidebarState');
  const tabOf=async url=>(await state()).tabs.find(t=>t.url===url);

  // 1. Correcteur : « enfant »/« pomme » existent, seule la grammaire en ligne peut les signaler.
  const source=await tabOf(host.origin+'/2');
  await command({type:'tab.activate',id:source.id});
  await command({type:'nav.go',id:source.id,input:host.origin+'/editor'});
  const editor=await host.connect(host.origin+'/editor');
  await editor.call('Emulation.setFocusEmulationEnabled',{enabled:true});
  await editor.evaluate(`(()=>{const t=document.getElementById('t');t.focus();document.execCommand('insertText',false,'Les enfant mange des pomme tous les matin. ');})()`);
  const marks=()=>editor.evaluate(`Number(document.documentElement.querySelector(':scope>[data-sol-spelling]')?.dataset.solSpelling??-1)`);
  await host.wait(async()=>(await marks())>=2,'online grammar marks',25000);
  ok(`grammaire en ligne : ${await marks()} fautes soulignées`);
  // Un identifiant n'est jamais lu, donc jamais envoyé.
  await editor.evaluate(`document.getElementById('user').focus()`);
  await delay(900);
  assert.equal(await editor.evaluate(`document.getElementById('user').getAttribute('spellcheck')`),null,'username field untouched');
  assert.equal(await marks(),-1,'marks removed outside eligible field');
  ok('champ identifiant ignoré');

  // 2. Cadre d'un autre site : le champ est corrigé dans son propre document.
  await command({type:'nav.go',id:source.id,input:host.origin+'/framed'});
  await host.wait(async()=>(await tabOf(host.origin+'/framed')),'framed tab');
  const inner=host.origin.replace('127.0.0.1','localhost')+'/editor';
  const framed=await host.connect(inner);
  await framed.call('Emulation.setFocusEmulationEnabled',{enabled:true});
  const top=await host.connect(host.origin+'/framed');
  await top.call('Emulation.setFocusEmulationEnabled',{enabled:true});
  await top.evaluate(`document.getElementById('f').focus()`);
  await framed.evaluate(`(()=>{const t=document.getElementById('t');window.focus();t.focus();document.execCommand('insertText',false,'Les enfant mange des pomme tous les matin. ');})()`);
  await host.wait(()=>framed.evaluate(`Number(document.documentElement.querySelector(':scope>[data-sol-spelling]')?.dataset.solSpelling??-1)>=0`),'spelling inside cross-origin frame',25000);
  ok('cadre d\'un autre site corrigé');

  // 3. Onglet planté en arrière-plan : il se recrée quand on y revient au lieu de rester mort.
  const victim=await tabOf(host.origin+'/3');
  await command({type:'tab.activate',id:victim.id});
  const crashed=await host.connect(host.origin+'/3');
  await crashed.evaluate('window.__before=1');
  await command({type:'tab.activate',id:source.id});
  await delay(300);
  crashed.call('Page.crash').catch(()=>{});
  await delay(5000); // le moteur met quelques secondes à annoncer la mort du processus
  await command({type:'tab.activate',id:victim.id});
  await host.wait(async()=>{
    try{const fresh=await host.connect(host.origin+'/3');return await fresh.evaluate('document.readyState==="complete"&&!window.__before');}catch{return false;}
  },'crashed background tab reloads on return',20000);
  ok('onglet planté rechargé au retour');

  // 4. Fermer un onglet pendant son téléchargement ne l'interrompt plus.
  const carrier=await tabOf(host.origin+'/8');
  await command({type:'tab.activate',id:carrier.id});
  const page=await host.connect(host.origin+'/8');
  await host.wait(()=>page.evaluate('document.readyState==="complete"&&!!document.body').catch(()=>false),'carrier page ready');
  await page.evaluate(`(()=>{const a=document.createElement('a');a.href=${JSON.stringify(host.origin+'/slow/survivor.bin')};a.download='survivor.bin';document.body.append(a);a.click();})()`);
  const dock=await host.connect('#dock');
  const items=()=>dock.evaluate('window.__downloads?.items??[]').catch(()=>[]);
  await dock.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.items&&e.data.items.length)window.__downloads=e.data})`);
  await host.wait(async()=>(await items()).length>0,'download listed',15000);
  await command({type:'tab.close',id:carrier.id});
  await host.wait(async()=>(await items()).some(d=>d.name==='survivor.bin'&&d.state==='Completed'),'download survives its tab',30000);
  assert(!(await state()).tabs.some(t=>t.id===carrier.id),'tab really closed');
  ok('téléchargement terminé après fermeture de son onglet');

  // 5. Interface des pièces jointes (jeu de données : pas de compte ChatGPT en test).
  await command({type:'chat.toggle'});
  const chat=await host.connect('#chat');
  await host.wait(()=>chat.evaluate('!!document.querySelector(".sol-chat")'),'chat ready');
  await delay(400);
  const pixel='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';
  const fixture={chat:true,ready:true,account:'test@example.invalid',busy:false,error:null,messages:[],attachments:[{id:'a',name:'Capture de la page.jpg',image:pixel},{id:'b',name:'rapport.pdf · p.1/3',image:pixel},{id:'c',name:'notes.docx',image:null}]};
  await chat.evaluate(`window.__cmds=[];const post=chrome.webview.postMessage.bind(chrome.webview);chrome.webview.postMessage=v=>{window.__cmds.push(typeof v==='string'?JSON.parse(v):v);post(v)};chrome.webview.dispatchEvent(new MessageEvent('message',{data:${JSON.stringify(fixture)}}))`);
  await host.wait(()=>chat.evaluate(`document.querySelectorAll('[aria-label="Pièces jointes"] li,[aria-label="Attachments"] li').length===3`),'attachments shown');
  // Une image collée part vers l'hôte avec son nom et ses données.
  await chat.evaluate(`(()=>{const bytes=Uint8Array.from(atob(${JSON.stringify(pixel.split(',')[1])}),c=>c.charCodeAt(0));const data=new DataTransfer();data.items.add(new File([bytes],'',{type:'image/png'}));document.querySelector('.sol-chat').dispatchEvent(new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true}));})()`);
  await host.wait(()=>chat.evaluate(`window.__cmds.some(c=>c.type==='chat.file'&&c.id==='capture.png'&&c.input.startsWith('data:image/png;base64,'))`),'pasted image sent to host');
  ok('collage d\'une capture transmis');
  if(capture){
    const shot=await chat.call('Page.captureScreenshot',{format:'png'});
    await writeFile(capture,Buffer.from(shot.data,'base64'));
  }

  // 6. Moteur entier tué : Sol se relance sur le même profil. Filtré sur ce profil de test uniquement.
  await delay(Math.max(0,31000-process.uptime()*1000));
  const engines=processes("Name='msedgewebview2.exe'").filter(p=>p.CommandLine?.includes(profileName)&&!p.CommandLine.includes('--type='));
  assert(engines.length>=1,'test engine found');
  const before=host.pid;
  killTree(engines[0].ProcessId);
  const relaunched=await host.wait(async()=>processes("Name='Sol.exe'").find(p=>p.CommandLine?.includes(`--profile=${profileName}`)&&p.ProcessId!==before),'Sol restarts after engine loss',30000).catch(error=>{if(/Sol exited/.test(error.message))return null;throw error;});
  let again=relaunched;
  for(let i=0;!again&&i<150;i++){await delay(100);again=processes("Name='Sol.exe'").find(p=>p.CommandLine?.includes(`--profile=${profileName}`)&&p.ProcessId!==before);}
  assert(again,'Sol restarts after engine loss');
  ok('relance après perte du moteur');
  await delay(3000);
  killTree(again.ProcessId);
  console.log('PASS browser fixes');
}finally{await host.dispose();}
