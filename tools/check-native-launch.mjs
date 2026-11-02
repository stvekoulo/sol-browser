import {readFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';
const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
try {
  const ui=await host.launch();
  console.log('PASS native launch');
  await host.close(ui);
} catch(error) {
  console.log('Targets:',JSON.stringify(await host.targets()));
  console.log((await readFile(resolve(host.profile,'debug.log'),'utf8').catch(()=>'' )).split('\n').slice(-40).join('\n'));
  throw error;
} finally { await host.dispose(); }
