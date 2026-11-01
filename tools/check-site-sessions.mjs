// Sessions d'onglets par site, historique des téléchargements et nouvel accueil, dans une vraie fenêtre de Sol.
import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const exe=process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe';
const shots=process.argv[3];
const ok=label=>console.log(`ok - ${label}`);
const host=await sidebarHost(exe,{downloads:true,serve(req,res){
  if(req.url==='/file.bin'){res.writeHead(200,{'Content-Type':'application/octet-stream','Content-Disposition':'attachment; filename="file.bin"','Content-Length':4});res.end('data');return true;}
  return false;
}});
try{
  const other=host.origin.replace('127.0.0.1','localhost');
  let ui=await host.launch(850,1200);
  const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
  const state=()=>ui.evaluate('window.__sidebarState');
  const popup=await (async()=>{await command({type:'settings.open',x:900,y:60});return host.connect('#popup');})();
  await popup.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup)window.__popup=e.data})`);
  const sessions=async id=>{
    await popup.evaluate('window.__popup=null');
    await command({type:'popup.close'});await delay(900);
    await command({type:'session.menu',id,x:400,y:80});
    return host.wait(()=>popup.evaluate(`window.__popup?.popup==='sessions'?window.__popup:null`),'sessions popup');
  };
  const tab=await (await state()).tabs.find(t=>t.url===host.origin+'/2');
  await command({type:'tab.activate',id:tab.id});
  await host.wait(async()=>(await state()).tabs.find(t=>t.id===tab.id).title,'tab loaded');

  // Aucune session proposée par défaut, ni sur ce site, ni sur l'accueil.
  assert.deepEqual((await sessions(tab.id)).items,[],'no seeded session');
  await command({type:'popup.close'});
  // Créée ici, elle appartient à ce site.
  await command({type:'session.create',id:tab.id,title:'Compte A'});
  const inSession=await host.wait(async()=>(await state()).tabs.find(t=>t.id===tab.id&&t.sessionId),'tab moved to session');
  const site=await sessions(tab.id);
  assert.equal(site.items.length,1);assert.equal(site.items[0].name,'Compte A');
  ok('session créée sur ce site et listée');
  // Autre site dans un autre onglet : seule la session actuelle.
  const stranger=(await state()).tabs.find(t=>t.url===host.origin+'/8');
  await command({type:'tab.activate',id:stranger.id});
  await command({type:'nav.go',id:stranger.id,input:other+'/elsewhere'});
  await host.wait(async()=>(await state()).tabs.find(t=>t.id===stranger.id).url===other+'/elsewhere','other site loaded');
  assert.deepEqual((await sessions(stranger.id)).items,[],'other site sees no session');
  ok('autre site : session actuelle seulement');
  // Un onglet du site la retrouve.
  const sibling=(await state()).tabs.find(t=>t.url===host.origin+'/9');
  assert.equal((await sessions(sibling.id)).items.length,1,'same site offers it');
  await command({type:'popup.close'});
  // Saisir un autre site depuis la session la quitte ; ouvrir depuis elle un autre site part de la session actuelle.
  await command({type:'tab.activate',id:inSession.id});
  await command({type:'nav.go',id:inSession.id,input:other+'/typed'});
  await host.wait(async()=>{const t=(await state()).tabs.find(t=>t.id===inSession.id);return t.url===other+'/typed'&&!t.sessionId;},'typed other site leaves the session');
  ok('adresse d’un autre site : quitte la session');

  // Téléchargement : conservé après redémarrage, retirable.
  const carrier=(await state()).tabs.find(t=>t.url===host.origin+'/10');
  await command({type:'tab.activate',id:carrier.id});
  await command({type:'nav.go',id:carrier.id,input:host.origin+'/file.bin'});
  const file=resolve(host.profile,'downloads.json');
  await host.wait(async()=>{try{return (await readFile(file,'utf8')).includes('file.bin');}catch{return false;}},'download saved',15000);
  await host.close(ui);
  ui=await host.launch(850,1200);
  const after=await host.connect('#popup').catch(()=>null);
  await command({type:'downloads.open',x:900,y:600});
  const reopened=after??await host.connect('#popup');
  await host.wait(()=>reopened.evaluate(`document.body.innerText.includes('file.bin')`),'download listed after restart');
  ok('historique des téléchargements conservé');

  if(shots){
    await command({type:'popup.close'});
    await command({type:'welcome.open'});
    const welcome=await host.connect('#welcome');
    await host.wait(()=>welcome.evaluate('!!document.querySelector(".welcome-next")'),'welcome');
    await delay(700);
    for(const [i,name] of ['import','look'].entries()){
      const shot=await welcome.call('Page.captureScreenshot',{format:'png'});
      await writeFile(`${shots}/welcome-${name}.png`,Buffer.from(shot.data,'base64'));
      if(i===0)break;
    }
  }
  await host.close(ui);
  console.log('PASS site sessions');
}finally{await host.dispose();}
