// Captures directes de la fenêtre native : aucune composition ni retouche des images.
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {execFileSync} from 'node:child_process';
import {sidebarHost} from './sidebar-check-host.mjs';

const output=resolve('bin/store-screenshots-1.2.6');
await mkdir(output,{recursive:true});
for(const language of ['fr','en']) {
  const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
  try {
    const settings=resolve(host.profile,'settings.json');
    await writeFile(settings,JSON.stringify({...JSON.parse(await readFile(settings,'utf8')),Language:language,Shortcuts:[],StartWallpaper:true,HomePage:'https://start.sol/index.html#start'}));
    const ui=await host.launch(900,1440);
    const command=c=>ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
    await command({type:'tab.open',url:'https://start.sol/index.html#start'});
    const start=await host.connect('https://start.sol/index.html#start');
    await host.wait(()=>start.evaluate('document.querySelector(".start-brand")&&document.documentElement.classList.contains("painted")'),'start painted');
    const capture=name=>execFileSync('pwsh',['-NoProfile','-File',resolve('tools/capture-store-window.ps1'),'-ProcessId',String(host.pid),'-Output',resolve(output,`${language}-${name}.png`)],{stdio:'pipe'});
    capture('home');
    await command({type:'settings.open',x:900,y:60});
    const popup=await host.connect('#popup');
    await host.wait(()=>popup.evaluate('!!document.querySelector(".settings-nav")'),'settings ready');
    capture('settings');
    await host.close(ui);
  } finally { await host.dispose(); }
}
console.log(`PASS direct native screenshots: ${output}`);
