import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {once} from 'node:events';
import {DatabaseSync} from 'node:sqlite';
import {access,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const servers=[],urls=[];let requests=0,active=0,peak=0;
const host=await sidebarHost('src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
let ui;
try {
  for(let index=1;index<=8;index++){
    const server=createServer(async(req,res)=>{
      requests++;active++;peak=Math.max(peak,active);await delay(100);
      if(req.url==='/icon.svg'){res.writeHead(200,{'Content-Type':'image/svg+xml'});res.end('<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64"><rect width="64" height="64" rx="12" fill="#73a485"/></svg>');}
      else {res.writeHead(200,{'Content-Type':'text/html'});res.end('<!doctype html><title>Site de test</title><link rel="icon" href="/icon.svg">');}
      active--;
    }).listen(0,`127.0.0.${index}`);await once(server,'listening');servers.push(server);urls.push(`http://127.0.0.${index}:${server.address().port}/`);
  }
  const db=new DatabaseSync(resolve(host.profile,'browser.db'));
  db.exec('CREATE TABLE bookmarks(url TEXT PRIMARY KEY,title TEXT NOT NULL,added INTEGER NOT NULL)');
  const add=db.prepare('INSERT INTO bookmarks VALUES(?,?,?)');
  for(let index=0;index<300;index++)add.run(urls[index%urls.length]+'favorite-'+index,'Favori '+index,index);
  db.close();
  await writeFile(resolve(host.profile,'session.json'),JSON.stringify({Urls:['https://start.sol/index.html#start'],ActiveIndex:0}));
  const results=[];
  for(const phase of ['cold','restart']){
    const before=requests;
    ui=await host.launch(800,1200);
    const page=await host.connect('https://start.sol/index.html#start');
    await host.wait(()=>page.evaluate('document.querySelectorAll(".start-monogram img").length>=24'),'favorite icons rendered',45000);
    await page.evaluate(`window.__startUpdates=0;chrome.webview.addEventListener('message',e=>{if(e.data.event==='start')__startUpdates++});chrome.webview.postMessage('{"type":"start.ready"}')`);
    await delay(500);
    assert.equal(await page.evaluate('document.querySelector(".start-more").textContent.includes("300")'),true);
    assert(await page.evaluate('document.querySelectorAll(".start-site").length<=48'));
    if(phase==='restart')assert.equal(requests,before,'restart uses stored icons without refetching sites');
    const state={phase,requests:requests-before,peak,updates:await page.evaluate('__startUpdates'),icons:await page.evaluate('document.querySelectorAll(".start-monogram img").length')};results.push(state);
    await host.close(ui);ui=null;
  }
  assert(peak<=4,'favicon fetch concurrency is bounded');
  const check=new DatabaseSync(resolve(host.profile,'browser.db'));assert.equal(check.prepare('SELECT COUNT(*) n FROM site_icons').get().n,8);check.close();
  await writeFile(resolve(host.base,'start-restart.json'),JSON.stringify(results,null,2));
  console.log(JSON.stringify({base:host.base,results}));console.log('PASS 300 persisted favorites, durable icons, zero refetch on restart, and maximum four concurrent requests');
}finally{await host.dispose();for(const server of servers)await new Promise(r=>server.close(r));await assert.rejects(access(host.profile));}
