// Sauvegarde automatique et restauration complète dans une vraie fenêtre de Sol, contre une branche Neon jetable :
// node tools/check-cloud-restore.mjs <Sol.exe> <fichier .env de la branche>. Refuse la production.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {randomBytes} from 'node:crypto';
import {existsSync,mkdirSync,readFileSync,rmSync,writeFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const exe=process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe';
const env=Object.fromEntries(readFileSync(process.argv[3]??'.env.local','utf8').split(/\r?\n/).filter(l=>l.includes('=')).map(l=>{const at=l.indexOf('=');return [l.slice(0,at),l.slice(at+1).replace(/^"|"$/g,'')];}));
assert.notEqual(env.NEON_BRANCH,'production','test accounts never go on production');
process.env.NAVIGATEUR_CLOUD_AUTH=env.NEON_AUTH_BASE_URL;
process.env.NAVIGATEUR_CLOUD_API=env.NEON_FUNCTION_BACKUP_BASE_URL.replace(/\/$/,'');
const ok=label=>console.log(`ok - ${label}`);
const host=await sidebarHost(exe);
const profileName=host.profile.split(/[\\/]/).pop();
const alive=pid=>{try{process.kill(pid,0);return true;}catch{return false;}};
const solFor=()=>JSON.parse(execFileSync('pwsh',['-NoProfile','-Command',`@(Get-CimInstance Win32_Process -Filter "Name='Sol.exe'" | Where-Object { $_.CommandLine -like '*--profile=${profileName}*' } | Select-Object -ExpandProperty ProcessId) | ConvertTo-Json -Compress -AsArray`],{encoding:'utf8'})||'[]');
try{
  // Un profil avec une extension et des données hors de la base : tout doit revenir après la restauration.
  mkdirSync(resolve(host.profile,'extensions','testextension','js'),{recursive:true});
  writeFileSync(resolve(host.profile,'extensions','testextension','js','main.js'),'extension-marker');
  writeFileSync(resolve(host.profile,'workspaces.json'),'"workspace-marker"');
  writeFileSync(resolve(host.profile,'extension-state.json'),'{"testextension":true}');
  const ui=await host.launch(850,1200);
  const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
  await command({type:'settings.open',x:900,y:60});
  const popup=await host.connect('#popup');
  await popup.evaluate(`chrome.webview.addEventListener('message',e=>{if(e.data.popup)window.__popup=e.data})`);
  const openSettings=async()=>{for(let i=0;i<6;i++){await command({type:'popup.close'});await delay(900);await popup.evaluate('window.__popup=null');await command({type:'settings.open',x:900,y:60});await delay(1200);if(await popup.evaluate(`window.__popup?.popup==='settings'`))return;}throw new Error('settings not shown');};
  await openSettings();
  const cloud=()=>popup.evaluate('window.__popup?.cloud');
  const email=`sol-e2e-restore-${Date.now()}@example.com`,password=randomBytes(18).toString('base64url'),key=randomBytes(18).toString('base64url');
  await command({type:'cloud.sign',id:'up',query:email,input:password});
  await host.wait(async()=>(await cloud())?.email===email,'account created',30000);
  // Activer la sauvegarde automatique envoie tout de suite une première sauvegarde.
  await command({type:'cloud.auto.key',title:key});
  // Sol ferme les paramètres pendant l'envoi : ils sont rouverts pour lire le résultat.
  await host.wait(async()=>{await openSettings();const c=await cloud();return !c?.busy&&c?.auto&&c.last&&c.backups.length===1;},'automatic backup sent',150000);
  const backup=(await cloud()).backups[0];
  ok(`sauvegarde automatique activée et envoyée (${backup.bytes} octets)`);
  // Les données disparaissent du PC : seule la sauvegarde peut les rendre.
  rmSync(resolve(host.profile,'extensions'),{recursive:true,force:true});
  rmSync(resolve(host.profile,'workspaces.json'),{force:true});
  await command({type:'cloud.restore.key',id:backup.id,title:'mauvaise-cle-de-test'});
  await host.wait(async()=>(await cloud())?.error==='key','wrong key refused',60000);
  ok('mauvaise clé refusée, rien n\'est restauré');
  const first=host.pid;
  await command({type:'cloud.restore.key',id:backup.id,title:key});
  await host.wait(async()=>!alive(first),'Sol closes to restore',90000).catch(error=>{if(!/Sol exited/.test(error.message))throw error;});
  ok('Sol se ferme pour appliquer la restauration');
  let relaunched=[];
  for(let i=0;i<300&&!relaunched.length;i++){await delay(200);relaunched=solFor();}
  assert(relaunched.length,'Sol relaunches on its own');
  ok('Sol redémarre tout seul');
  for(let i=0;i<150&&!existsSync(resolve(host.profile,'workspaces.json'));i++)await delay(200);
  assert.equal(readFileSync(resolve(host.profile,'extensions','testextension','js','main.js'),'utf8'),'extension-marker');
  assert.equal(readFileSync(resolve(host.profile,'workspaces.json'),'utf8'),'"workspace-marker"');
  assert(!existsSync(resolve(host.profile,'restore')),'staging folder consumed');
  ok('extension et données restaurées au redémarrage');
  await delay(3000);
  for(const pid of solFor())try{execFileSync('taskkill',['/PID',String(pid),'/T','/F'],{stdio:'ignore'});}catch{}
  console.log('PASS cloud auto backup and full restore');
}finally{
  for(const pid of solFor())try{execFileSync('taskkill',['/PID',String(pid),'/T','/F'],{stdio:'ignore'});}catch{}
  await host.dispose();
}
