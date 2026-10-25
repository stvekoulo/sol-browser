import assert from 'node:assert/strict';
import {readFile, writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const host = await sidebarHost(process.argv[2] ?? 'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
let ui;
const sessionFile = resolve(host.profile, 'session.json');
const session = JSON.parse(await readFile(sessionFile, 'utf8'));
for (let i = session.Urls.length; i < 40; i++) {
  session.Urls.push(`${host.origin}/${i}`);
  session.Titles.push(`Onglet ${i}`);
  session.Icons.push(session.Icons[0]);
}
await writeFile(sessionFile, JSON.stringify(session));
const state = () => ui.evaluate('window.__sidebarState');
const command = c => ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const row = id => `.sidebar-tab[data-id="${id}"]`;
const heading = id => `[data-kind="group"][data-id="${id}"] .tab-group-header`;
async function point(view, selector) {
  await view.evaluate(`document.querySelector(${JSON.stringify(selector)}).scrollIntoView({block:'nearest'})`);
  await delay(100);
  return view.evaluate(`(()=>{const r=document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2}})()`);
}
async function click(view, selector, {ctrl = false, right = false} = {}) {
  const p = await point(view, selector), button = right ? 'right' : 'left';
  const common = {...p, button, buttons: right ? 2 : 1, modifiers: ctrl ? 2 : 0, clickCount: 1};
  await view.call('Input.dispatchMouseEvent', {type:'mouseMoved', ...p});
  await view.call('Input.dispatchMouseEvent', {type:'mousePressed', ...common});
  await view.call('Input.dispatchMouseEvent', {type:'mouseReleased', ...common, buttons:0});
}
async function choose(popup, label) {
  await host.wait(() => popup.evaluate(`(()=>{const b=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()===${JSON.stringify(label)});if(!b)return false;b.click();return true})()`).catch(()=>false), label);
}
async function drag(source, target) {
  const a = await point(ui, source);
  await ui.call('Input.dispatchMouseEvent', {type:'mousePressed', ...a, button:'left', buttons:1, clickCount:1});
  await ui.call('Input.dispatchMouseEvent', {type:'mouseMoved', x:a.x+8, y:a.y+8, buttons:1});
  const b = await point(ui, target);
  await ui.call('Input.dispatchMouseEvent', {type:'mouseMoved', ...b, buttons:1});
  await delay(180);
  await ui.call('Input.dispatchMouseEvent', {type:'mouseReleased', ...b, button:'left', buttons:0, clickCount:1});
}
try {
  ui = await host.launch(720, 1200);
  await host.wait(async() => (await state()).tabs.length === 40, '40 restored tabs');
  // La molette ne déplace ni les raccourcis, ni les épinglés ; aucun ascenseur n'est dessiné.
  const pinnedTop = await ui.evaluate('document.querySelector(".pinned-strip").getBoundingClientRect().top');
  const wheel = await point(ui, '.sidebar-new-tab');
  await ui.call('Input.dispatchMouseEvent', {type:'mouseWheel', x:wheel.x, y:wheel.y-10, deltaX:0, deltaY:500});
  await host.wait(() => ui.evaluate('document.querySelector(".sidebar-scroll").scrollTop>30'), 'tab list scrolls');
  assert.equal(await ui.evaluate('document.querySelector(".pinned-strip").getBoundingClientRect().top'), pinnedTop, 'pins stay fixed');
  assert.equal(await ui.evaluate('getComputedStyle(document.querySelector(".sidebar-scroll")).scrollbarWidth'), 'none');
  assert.equal(await ui.evaluate('getComputedStyle(document.querySelector(".sidebar-scroll"),"::-webkit-scrollbar").display'), 'none');
  assert.ok(await ui.evaluate('document.querySelector(".sidebar-quick-actions").getBoundingClientRect().bottom<=innerHeight'), 'footer stays visible');
  const initial = await state(), work = initial.groups[0].id;
  // Le clic gauche sur le nom ou la flèche replie/déplie directement, sans passer par le menu.
  for (const part of [' .tab-group-name', ' .tab-group-chevron', '']) {
    await click(ui, heading(work) + part);
    await host.wait(async() => (await state()).groups.find(g=>g.id===work).collapsed, 'left click collapses group', 2000);
    await click(ui, heading(work) + part);
    await host.wait(async() => !(await state()).groups.find(g=>g.id===work).collapsed, 'left click expands group', 2000);
  }
  assert.equal((await state()).activeId, initial.activeId, 'folding keeps the active tab');
  // Un vrai clic droit ouvre le menu du groupe sans modifier son état.
  await click(ui, heading(work), {right:true});
  const popup = await host.connect('#popup');
  await choose(popup, 'Replier le groupe');
  await host.wait(async() => (await state()).groups.find(g=>g.id===work).collapsed, 'group collapsed from menu');
  await delay(300); // Laisser finir la fermeture du volet avant de rouvrir le menu.
  await click(ui, heading(work), {right:true});
  await choose(popup, 'Ouvrir le groupe');
  await host.wait(async() => !(await state()).groups.find(g=>g.id===work).collapsed, 'group opened from menu');
  // Ctrl sélectionne trois onglets répartis dans deux groupes et hors groupe, sans démarrer de glisser.
  const ids = [initial.tabs[6].id, initial.tabs[2].id, initial.tabs[4].id];
  await click(ui, row(ids[0]));
  await click(ui, row(ids[1]), {ctrl:true});
  await click(ui, row(ids[2]), {ctrl:true});
  await host.wait(async() => (await state()).selectedTabIds?.length===3, 'three selected tabs');
  assert.equal(await ui.evaluate('document.querySelectorAll(".sidebar-tab[data-selected=true]").length'), 3);
  assert.equal(await ui.evaluate('!!document.querySelector(".sidebar-drag-ghost")'), false, 'Ctrl never starts drag');
  // Retirer un onglet par Ctrl, puis le reprendre.
  await click(ui, row(ids[2]), {ctrl:true});
  await host.wait(async() => (await state()).selectedTabIds.length===2, 'Ctrl toggles off');
  await click(ui, row(ids[2]), {ctrl:true});
  await host.wait(async() => (await state()).selectedTabIds.length===3, 'Ctrl toggles on');
  await click(ui, row(ids[1]), {right:true});
  await choose(popup, 'Créer un groupe avec ces 3 onglets…');
  await host.wait(() => popup.evaluate('!!document.querySelector("#name-input")').catch(()=>false), 'group editor');
  assert.match(await popup.evaluate('document.querySelector(".name-form-title").textContent'), /3 onglets/);
  // Remplir le formulaire React puis enregistrer, comme depuis le clavier.
  await popup.evaluate('document.querySelector("#name-input").focus()');
  await popup.call('Input.insertText', {text:'Mon choix'});
  await popup.evaluate('document.querySelector(".name-form").requestSubmit()');
  const created = await host.wait(async() => (await state()).groups.find(g=>g.name==='Mon choix'), 'selected group created');
  const grouped = (await state()).tabs.filter(t=>t.groupId===created.id).map(t=>t.id);
  assert.deepEqual(grouped, [ids[1], ids[2], ids[0]], 'tab order preserved');
  assert.equal((await state()).tabs.length, 40, 'no tabs closed or duplicated');
  assert.deepEqual((await state()).selectedTabIds, [], 'selection cleared after grouping');
  await delay(700); // Attendre la fin du déplacement des lignes avant la capture.
  await writeFile(resolve(host.base, 'sidebar-selection.png'), Buffer.from((await ui.call('Page.captureScreenshot',{format:'png'})).data, 'base64'));
  // Le bouton visible permet aussi de créer le groupe ; Échap et un clic simple effacent la sélection.
  const free = (await state()).tabs.filter(t=>!t.pinned && !t.groupId).slice(0, 2);
  await click(ui, row(free[0].id));
  await click(ui, row(free[1].id), {ctrl:true});
  await host.wait(() => ui.evaluate('!!document.querySelector(".sidebar-selection")'), 'selection toolbar');
  await ui.evaluate('document.querySelector(".sidebar-selection button").click()');
  await host.wait(() => popup.evaluate('document.querySelector(".name-form-title")?.textContent.includes("2 onglets")').catch(()=>false), 'two-tab group editor from toolbar');
  await command({type:'popup.close'});
  await ui.call('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});
  await host.wait(async()=>!(await state()).selectedTabIds.length, 'Escape clears selection');
  await click(ui, row(free[0].id), {ctrl:true});
  await click(ui, row(free[1].id));
  await host.wait(async()=>!(await state()).selectedTabIds.length, 'plain click clears selection');
  // Fermer réellement un groupe ne touche qu'à ses membres.
  await click(ui, heading(created.id), {right:true});
  await choose(popup, 'Fermer le groupe');
  await host.wait(async() => !(await state()).groups.some(g=>g.id===created.id), 'group closed');
  assert.ok((await state()).tabs.every(t=>!grouped.includes(t.id)), 'all group members closed');
  assert.equal((await state()).tabs.length, 37);
  // Le glisser entre les deux zones continue à épingler puis détacher un onglet.
  await ui.evaluate('document.querySelector(".sidebar-scroll").scrollTop=0');
  await drag(row(free[0].id), '[data-pin-zone]');
  await host.wait(async() => (await state()).tabs.find(t=>t.id===free[0].id).pinned, 'drag into fixed pins');
  await drag(`[data-kind="tab"][data-id="${free[0].id}"]`, '.sidebar-new-tab');
  await host.wait(async() => !(await state()).tabs.find(t=>t.id===free[0].id).pinned, 'drag back from fixed pins');
  // L'onglet actif d'une paire scindée sélectionne les deux pages avant le regroupement.
  await command({type:'tab.activate', id:free[0].id});
  await command({type:'split.open', id:free[1].id});
  const extra = (await state()).tabs.find(t=>!t.pinned && !t.groupId && !free.some(f=>f.id===t.id));
  await command({type:'tab.select', id:extra.id});
  await host.wait(async() => (await state()).selectedTabIds.length===3, 'split pair kept in selection');
  const splitSelection = (await state()).selectedTabIds;
  assert.ok(free.every(f=>splitSelection.includes(f.id)), 'both split tabs selected');
  await command({type:'tabs.selection.clear'});
  await command({type:'split.close'});
  console.log(`PASS fixed pins, hidden scrollbars, group left/right-click, Ctrl grouping, split pairs, pin/unpin drag; ${host.base}`);
} finally {
  if (ui) await host.close(ui).catch(()=>{});
  await host.dispose();
}
