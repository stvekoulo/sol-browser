import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';

assert(process.argv[2], 'Passer le chemin de Sol.exe compilé avec SolStoreBuild=true.');
const host = await sidebarHost(process.argv[2]);
try {
  const ui = await host.launch(900, 1250);
  const command = value => ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(value))})`);
  await command({type:'settings.open',x:500,y:58});
  const popup = await host.connect('#popup');
  await host.wait(() => popup.evaluate('!!document.querySelector(".settings-nav")'), 'settings ready');
  await popup.evaluate(`[...document.querySelectorAll('.settings-nav [role=tab]')].at(-1).click()`);
  await host.wait(() => popup.evaluate('document.body.textContent.includes("Microsoft Store")'), 'Store update state');
  assert.equal(await popup.evaluate(`[...document.querySelectorAll('button')].some(b=>/^(Vérifier|Check)$/.test(b.textContent.trim()))`), false);
  await command({type:'update.check'});
  await host.wait(() => popup.evaluate('document.body.textContent.includes("Microsoft Store")'), 'manual check remains Store managed');
  await writeFile(resolve(host.base,'store-distribution.json'), JSON.stringify({storeManagedUpdates:true,manualCheckHidden:true,packagedActivationTested:false},null,2));
  await host.close(ui);
  console.log(`PASS Store update state and hidden manual updater. Unpackaged launch only: ${host.base}`);
} finally { await host.dispose(); }
