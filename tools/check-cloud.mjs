// Sauvegarde en ligne dans une vraie fenêtre de Sol, contre une branche Neon jetable :
// node tools/check-cloud.mjs <Sol.exe> <fichier .env de la branche>. Refuse la production.
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';
import {readFileSync} from 'node:fs';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const exe=process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe';
const env=Object.fromEntries(readFileSync(process.argv[3]??'.env.local','utf8').split(/\r?\n/).filter(l=>l.includes('=')).map(l=>{const at=l.indexOf('=');return [l.slice(0,at),l.slice(at+1).replace(/^"|"$/g,'')];}));
assert.notEqual(env.NEON_BRANCH,'production','test accounts never go on production');
process.env.NAVIGATEUR_CLOUD_AUTH=env.NEON_AUTH_BASE_URL;
process.env.NAVIGATEUR_CLOUD_API=env.NEON_FUNCTION_BACKUP_BASE_URL.replace(/\/$/,'');
const ok=label=>console.log(`ok - ${label}`);
const host=await sidebarHost(exe);
try{
  const ui=await host.launch(850,1200);
  const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
  await command({type:'settings.open',x:900,y:60});
  const popup=await host.connect('#popup');
  await popup.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup)window.__popup=e.data})`);
  const openSettings=async()=>{for(let i=0;i<6;i++){await command({type:'popup.close'});await delay(900);await popup.evaluate('window.__popup=null');await command({type:'settings.open',x:900,y:60});await delay(1200);if(await popup.evaluate(`window.__popup?.popup==='settings'`))return;}throw new Error('settings not shown');};
  await openSettings();
  const cloud=()=>popup.evaluate('window.__popup?.cloud');
  const email=`sol-e2e-app-${Date.now()}@example.com`,password=randomBytes(18).toString('base64url'),key=randomBytes(18).toString('base64url');
  await command({type:'cloud.sign',id:'in',query:email,input:password});
  await host.wait(async()=>(await cloud())?.error==='credentials','wrong account reported',30000);ok('compte inconnu : erreur claire');
  await command({type:'cloud.sign',id:'up',query:email,input:password});
  await host.wait(async()=>(await cloud())?.email===email,'account created',30000);ok('compte créé depuis Sol');
  assert.deepEqual((await cloud()).backups,[]);
  // Le mot de passe du compte est refusé comme clé.
  await command({type:'cloud.backup'});await delay(300);
  await command({type:'backup.export.key',title:password});
  await host.wait(()=>popup.evaluate(`window.__popup?.popup==='notice'&&/différente|differ/.test(window.__popup.hint)`),'account password refused as key');ok('mot de passe du compte refusé comme clé');
  await command({type:'cloud.backup'});await delay(300);
  await command({type:'backup.export.key',title:key});
  await host.wait(()=>popup.evaluate(`window.__popup?.popup==='notice'&&/envoyée|sent/.test(window.__popup.title)`),'backup sent',40000);ok('profil scellé et envoyé');
  await openSettings();
  await command({type:'cloud.refresh'});
  const list=await host.wait(async()=>{const c=await cloud();return c?.backups?.length===1&&c.backups;},'backup listed',30000);
  assert(list[0].bytes>10);ok(`sauvegarde listée (${list[0].bytes} octets)`);
  await delay(3000);await host.wait(async()=>(await cloud())?.busy===false,'cloud idle');
  await command({type:'cloud.restore',id:list[0].id});
  await delay(4000);
  await command({type:'backup.restore',title:'mauvaise-cle-de-test'});
  await host.wait(()=>popup.evaluate(`window.__popup?.popup==='notice'&&/incorrecte|wrong/.test(window.__popup.hint)`),'wrong key refused',25000);ok('mauvaise clé refusée');
  await command({type:'backup.restore',title:key});
  await host.wait(()=>popup.evaluate(`window.__popup?.popup==='notice'&&/prête|ready/.test(window.__popup.title)`),'restore staged',60000);ok('restauration prête au prochain démarrage');
  await host.close(ui);
  console.log('PASS cloud in Sol');
}finally{await host.dispose();}
