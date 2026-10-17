import assert from 'node:assert/strict';
import {DatabaseSync} from 'node:sqlite';
import {writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
const db=new DatabaseSync(resolve(host.profile,'browser.db'));
db.exec('CREATE TABLE bookmarks(url TEXT PRIMARY KEY,title TEXT NOT NULL,added INTEGER NOT NULL); CREATE TABLE visits(url TEXT PRIMARY KEY,title TEXT NOT NULL,count INTEGER NOT NULL,last INTEGER NOT NULL)');
const insert=db.prepare('INSERT INTO bookmarks VALUES(?,?,?)');
for(let i=0;i<48;i++)insert.run(host.origin+'/favorite-'+i,'Favori '+(i+1),Date.now()+i);
db.prepare('INSERT INTO visits VALUES(?,?,?,?)').run(host.origin+'/frequent-source','Previously frequent',50,Date.now());
db.close();
try{
  const ui=await host.launch();
  await ui.evaluate('chrome.webview.postMessage(JSON.stringify({type:"tab.open",url:"https://start.sol/index.html#start"}))');
  const page=await host.connect('https://start.sol/index.html#start');
  await host.wait(()=>page.evaluate('document.querySelectorAll(".start-site").length>0').catch(()=>false),'favorites rendered');
  assert(!await page.evaluate('document.body.textContent.includes("Fréquemment consultés")||!!document.querySelector(".start-frequent")'));
  const layout=()=>page.evaluate('({count:document.querySelectorAll(".start-site").length,columns:getComputedStyle(document.querySelector(".start-favorites")).gridTemplateColumns.split(" ").length})');
  let sizes=await layout();assert.equal(sizes.count,Math.min(48,sizes.columns*4),'four rows of favorites visible by default');
  await page.evaluate('document.querySelector(".start-more").click()');
  await host.wait(()=>page.evaluate('document.querySelectorAll(".start-site").length===48'),'show all favorites');
  await page.evaluate('document.querySelector(".start-more").click()');
  await host.wait(async()=>{sizes=await layout();return sizes.count===Math.min(48,sizes.columns*4);},'collapse to four rows');
  await writeFile(resolve(host.base,'home-favorites.png'),Buffer.from((await page.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
  await page.call('Emulation.setDeviceMetricsOverride',{width:360,height:720,deviceScaleFactor:1,mobile:false});
  await host.wait(async()=>{sizes=await layout();return sizes.count===sizes.columns*4;},'responsive favorite count');
  assert(await page.evaluate('document.documentElement.scrollWidth<=innerWidth'),'favorites fit a narrow pane');
  assert(!(await host.targets()).some(target=>target.url===host.origin+'/frequent-source'),'home does not create a hidden page preview');
  await writeFile(resolve(host.base,'home-favorites-narrow.png'),Buffer.from((await page.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
  console.log(JSON.stringify({passed:'home has four rows of favorites, show all/less, no frequent section or hidden preview, narrow layout fits',evidence:host.base}));
}finally{await host.dispose();}
