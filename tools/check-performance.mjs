import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {access,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const baseline=process.argv.includes('--baseline');
const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{performance:true});
const started=performance.now();
const sample=()=>JSON.parse(execFileSync('pwsh',['-NoProfile','-Command',`
  $tree=@(Get-CimInstance Win32_Process); $ids=[System.Collections.Generic.HashSet[int]]::new(); [void]$ids.Add(${host.pid});
  do {$added=$false; foreach($p in $tree){if($ids.Contains([int]$p.ParentProcessId)-and $ids.Add([int]$p.ProcessId)){$added=$true}}}while($added);
  $rows=@(Get-Process -Id @($ids) -ErrorAction SilentlyContinue); $sum=$rows|Measure-Object PrivateMemorySize64,WorkingSet64,CPU -Sum;
  @{privateBytes=$sum[0].Sum;workingSetBytes=$sum[1].Sum;cpuSeconds=$sum[2].Sum;processes=$rows.Count}|ConvertTo-Json -Compress
`],{encoding:'utf8'}));
let ui;
try{
  ui=await host.launch(760);
  const readyMs=Math.round(performance.now()-started);
  const page=await host.connect(host.origin+'/2');
  await host.wait(()=>page.evaluate('document.readyState==="complete"').catch(()=>false),'page loaded');
  await delay(5000);
  const targets=(await host.targets()).map(t=>t.url).filter(u=>u.startsWith('https://ui.navigateur/'));
  await ui.evaluate(`window.__themes=0;chrome.webview.addEventListener('message',e=>{if(e.data.event==='theme')window.__themes++})`);
  const before=sample();const idleStarted=performance.now();await delay(10000);const after=sample();
  const themeMessages=await ui.evaluate('window.__themes');
  const report={...after,readyMs,targets,themeMessages,idleSeconds:(performance.now()-idleStarted)/1000,
    cpuSeconds:Math.round((after.cpuSeconds-before.cpuSeconds)*100)/100};
  await writeFile(resolve(host.base,'performance.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({base:host.base,...report},null,2));
  if(!baseline){
    assert(!targets.some(u=>u.endsWith('#capture')),'capture renderer is not loaded before first use');
    assert(themeMessages<=1,'unchanged page does not broadcast its theme every second');
    await page.evaluate(`document.body.style.background='#132440'`);
    await host.wait(()=>ui.evaluate(`getComputedStyle(document.documentElement).getPropertyValue('--page-real').includes('19, 36, 64')`),'live theme follows a changed page');
    const normal=await ui.evaluate('window.__sidebarState.activeId');
    await ui.evaluate(`chrome.webview.postMessage('{"type":"window.private"}')`);
    await host.wait(()=>ui.evaluate('window.__sidebarState.privateMode'),'private tab ready');
    await ui.evaluate(`chrome.webview.postMessage('{"type":"capture.menu"}')`);
    const capture=await host.connect('#capture');
    await host.wait(()=>capture.evaluate('!!document.querySelector(".capture-modes")').catch(()=>false),'lazy capture receives first command');
    assert.equal(await capture.evaluate('document.documentElement.dataset.private'),'true','lazy capture keeps the active private theme');
    await ui.evaluate(`chrome.webview.postMessage('{"type":"capture.close"}')`);
    await ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify({type:'tab.activate',id:normal}))})`);
    await ui.evaluate(`chrome.webview.postMessage('{"type":"capture.menu"}')`);
    await host.wait(()=>capture.evaluate('!!document.querySelector(".capture-modes")').catch(()=>false),'capture reopens');
    await host.wait(()=>capture.evaluate('document.documentElement.dataset.private==="false"'),'capture follows normal tab theme');
    await ui.call('Emulation.setCPUThrottlingRate',{rate:4});
    for(const path of ['/3','/2']){
      const id=await ui.evaluate(`window.__sidebarState.tabs.find(t=>t.url===${JSON.stringify(host.origin+path)}).id`);
      await ui.evaluate(`document.querySelector('[data-kind="tab"][data-id="${id}"]').click()`);
      await host.wait(()=>ui.evaluate(`document.querySelector('[data-kind="tab"][data-id="${id}"]').getAttribute('aria-selected')==='true'`),'tab selection with 4x slower UI CPU',5000);
    }
    await ui.call('Emulation.setCPUThrottlingRate',{rate:1});
  }
  await host.close(ui);
}finally{
  await host.dispose();
  await assert.rejects(access(resolve(host.base,'profiles')),'test profiles and browser caches removed');
}
