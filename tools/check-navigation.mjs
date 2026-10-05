// Windows integration check: node tools/check-navigation.mjs [path/to/Sol.exe]
// Uses a disposable profile and a local server; never touches the user's session.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdir, writeFile, readFile, readdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const exe = resolve(process.argv[2] ?? 'src/Browser.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
const base = resolve(`bin/navigation-check-${Date.now()}`);
const profile = resolve(base, 'profiles/robustness');
await mkdir(profile, { recursive: true });
await mkdir(resolve(base, 'downloads'), { recursive: true });
const server = createServer((req, res) => {
  if (req.url === '/large-icon.svg') {
    res.writeHead(200, { 'Content-Type': 'image/svg+xml' });
    return res.end('<svg xmlns="http://www.w3.org/2000/svg" width="192" height="192"><rect width="192" height="192" fill="blue"/></svg>');
  }
  if (req.url === '/favicon.ico') {
    res.writeHead(200, { 'Content-Type': 'image/svg+xml' });
    return res.end('<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32"><rect width="32" height="32" fill="green"/></svg>');
  }
  if (req.url === '/reset') return req.socket.resetAndDestroy();
  if (req.url === '/redirect') { res.writeHead(302, { Location: '/redirect' }); return res.end(); }
  if (req.url === '/download') {
    res.writeHead(200, { 'Content-Type': 'application/octet-stream', 'Content-Disposition': 'attachment; filename="sol-check.txt"' });
    return res.end('Sol navigation regression check');
  }
  res.writeHead(req.url === '/missing' ? 404 : 200, { 'Content-Type': 'text/html' });
  const respond = () => res.end(`<html><head><title>Check ${req.url}</title><link href='/favicon.ico' rel='icon' sizes='16x16'><link sizes='any' href='/large-icon.svg' rel='icon'></head><body style="background:#f5f6fa;color:#172233;font:20px system-ui;margin:0"><div style="background:#2769d4;height:100px"></div><main style="padding:40px"><h1>Server page ${req.url}</h1></main></body></html>`);
  if (req.url === '/slow') setTimeout(respond, 1500); else respond();
});
server.listen(0, '127.0.0.1');
await once(server, 'listening');
const origin = `http://127.0.0.1:${server.address().port}`;
const debugPort = createServer();
debugPort.listen(0, '127.0.0.1');
await once(debugPort, 'listening');
const port = debugPort.address().port;
await new Promise(r => debugPort.close(r));
await writeFile(resolve(profile, 'settings.json'), JSON.stringify({ Onboarded: true, Theme: 'dark', HomePage: `${origin}/a`, BlockAds: false, OnlineSuggestions: false, LiveColor: false }));
let app, ui;
const connections = [];
const waitFor = async (get, description, timeout = 20000) => {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    try { const value = await get(); if (value) return value; }
    catch (e) { if (!/Cannot find default execution context|Execution context was destroyed/.test(e.message)) throw e; }
    await delay(100);
  }
  throw new Error(`Timed out: ${description}`);
};
async function connect(target) {
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await once(ws, 'open');
  let seq = 0;
  const pending = new Map();
  ws.addEventListener('message', e => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) {
      const { resolve, reject, timer } = pending.get(m.id);
      clearTimeout(timer); pending.delete(m.id);
      if (m.error) reject(new Error(m.error.message)); else resolve(m.result);
    }
  });
  const call = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++seq;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 10000);
    pending.set(id, { resolve, reject, timer });
    ws.send(JSON.stringify({ id, method, params }));
  });
  const evaluate = async expression => {
    const result = await call('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
  };
  connections.push(ws);
  return { call, evaluate };
}
const targets = () => fetch(`http://127.0.0.1:${port}/json/list`).then(r => r.json()).catch(() => []);
const command = c => ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(c))})`);
const state = () => ui.evaluate('window.__checkState');
async function launch(extra = []) {
  app = spawn(exe, ['--profile=robustness', '--bounds=-9000,-9000,1100,800', ...extra], { windowsHide: true, env: { ...process.env, NAVIGATEUR_DATA_DIR: base, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port} --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows`, NAVIGATEUR_DEBUG: '1', NAVIGATEUR_TEST_DOWNLOAD_DIR: resolve(base, 'downloads'), NAVIGATEUR_SAVER_TEST: '1' } });
  const target = await waitFor(async () => (await targets()).find(t => t.url === 'https://ui.navigateur/index.html'), 'toolbar');
  ui = await connect(target);
  await waitFor(async () => {
    await ui.evaluate(`if (!window.__checkListening) {
      window.__checkListening = true;
      chrome.webview.addEventListener('message', e => { window.__checkLastMessage = e.data; if (e.data.tabs) window.__checkState = e.data; });
    } chrome.webview.postMessage(JSON.stringify({type:'ui.ready'}));`);
    return (await state())?.tabs?.length;
  }, 'browser state', 40000);
}
async function pageAt(path) {
  const target = await waitFor(async () => (await targets()).find(t => t.url === `${origin}${path}`), `page ${path}`);
  const page = await connect(target);
  await waitFor(() => page.evaluate('document.readyState === "complete"'), 'document ready');
  return page;
}
async function stop() {
  if (!app || app.exitCode !== null) return;
  const exited = once(app, 'exit');
  await command({ type: 'window.close' });
  await exited;
}
try {
  await launch();
  let s = await state();
  const first = s.activeId;
  const page = await pageAt('/a');
  const iconSidebar = await connect((await targets()).find(t=>t.url.endsWith('#sidebar')));
  await waitFor(()=>iconSidebar.evaluate('Array.from(document.querySelectorAll("img.favicon")).some(i=>i.naturalWidth === 192)'), 'high resolution icon also used in sidebar');
  const initialWidth = await page.evaluate('innerWidth');
  await command({ type: 'zoom.out' });
  await waitFor(async () => (await state()).tabs.find(t => t.id === first)?.zoom === 90, '90% zoom');
  await waitFor(async () => (await page.evaluate('innerWidth')) > initialWidth * 1.08, 'zoom changes layout');
  await command({ type: 'tab.open' });
  await waitFor(async () => (await state()).tabs.length === 2, 'new tab');
  s = await state();
  assert.equal(s.tabs.find(t => t.id === s.activeId).zoom, 90, 'new pages must inherit the profile zoom');
  await waitFor(() => ui.evaluate('document.activeElement?.matches(".address-pill input")'), 'new tab search focus');
  await waitFor(() => ui.evaluate('document.documentElement.classList.contains("painted")'), 'first painted toolbar');
  assert.equal(await ui.evaluate('document.documentElement.dataset.theme'), 'dark', 'startup uses configured theme');
  await waitFor(() => ui.evaluate('document.activeElement.selectionEnd === document.activeElement.value.length && document.activeElement.selectionStart === 0'), 'typing replaces the new tab address');
  console.log('PASS zoom applies to existing and new tabs');
  const second = s.activeId;
  const sidebarTarget = await waitFor(async () => (await targets()).find(t => t.url.endsWith('#sidebar')), 'sidebar');
  const sidebar = await connect(sidebarTarget);
  for (const index of [1,2,0]) {
    await command({type:'settings.set',id:'uiScale',index});
    await waitFor(()=>sidebar.evaluate(`document.body.style.zoom === '${[1,.9,.8][index]}'`), 'sidebar interface scale');
    await delay(150);
    assert.ok(await sidebar.evaluate('Array.from(document.querySelectorAll(".sidebar-quick-actions button")).every(b => b.getBoundingClientRect().top >= 0 && b.getBoundingClientRect().bottom <= innerHeight)'), 'bottom shortcuts remain visible at every interface scale');
  }
  await sidebar.evaluate('document.querySelector(".sidebar-url-pill").click()');
  assert.equal(await sidebar.evaluate('document.activeElement?.matches(".sidebar-url-form input")'), true, 'sidebar pill focuses search');
  await sidebar.evaluate(`const field = document.querySelector('.sidebar-url-form input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(field, ${JSON.stringify(`${origin}/b`)}); field.dispatchEvent(new Event('input', {bubbles:true}));`);
  await sidebar.evaluate('document.querySelector(".sidebar-url-form").requestSubmit()');
  const other = await pageAt('/b');
  console.log('PASS new tab focus and sidebar search');
  await ui.evaluate('chrome.webview.postMessage(JSON.stringify({type:"sidebar.toggle"}))');
  await waitFor(() => sidebar.evaluate('document.body.classList.contains("floating") && getComputedStyle(document.body).backgroundColor === "rgba(0, 0, 0, 0)"'), 'transparent sidebar transition');
  await delay(200);
  await ui.evaluate('chrome.webview.postMessage(JSON.stringify({type:"sidebar.toggle"}))');
  await waitFor(() => sidebar.evaluate('!document.body.classList.contains("floating")'), 'sidebar docked');
  await waitFor(async () => (await other.evaluate('innerWidth')) > initialWidth * 1.08, 'zoom after navigation');
  await command({ type: 'nav.reload', id: second });
  await waitFor(async () => !(await state()).tabs.find(t => t.id === second).loading, 'reload');
  assert.ok((await other.evaluate('innerWidth')) > initialWidth * 1.08, 'reload must preserve zoom');
  await command({ type: 'split.open', id: first });
  await command({type:'split.resize',ratio:0.2});
  await waitFor(() => ui.evaluate('document.querySelectorAll(".split-addresses .address-pill").length === 2'), 'two independent split addresses');
  await ui.call('Emulation.setDeviceMetricsOverride',{width:850,height:52,deviceScaleFactor:1,mobile:false});
  await ui.evaluate(`document.querySelector('[data-tab-id="${first}"] input').focus()`);
  await waitFor(async () => (await state()).activeId === first, 'editing the second address activates its own page');
  assert.ok(await ui.evaluate(`(() => {const pill=document.querySelector('[data-tab-id="${first}"]');return pill.querySelector('input').getBoundingClientRect().right <= pill.querySelector('.address-trail').getBoundingClientRect().left + 1;})()`), 'address text does not overlap action buttons');
  assert.ok(await ui.evaluate('document.querySelector(".topbar-center").getBoundingClientRect().right <= document.querySelector(".topbar-right").getBoundingClientRect().left'), 'split addresses stay inside the toolbar');
  await ui.evaluate('document.activeElement.blur()');
  await ui.call('Emulation.clearDeviceMetricsOverride');
  await command({type:'tab.activate',id:second});
  console.log('PASS split address focus, compact toolbar and text/button separation');
  await command({ type: 'split.close' });
  await command({ type: 'nav.go', id: second, input: `${origin}/missing` });
  await pageAt('/missing');
  assert.match(await other.evaluate('document.body.innerText'), /Server page \/missing/, 'keep the server 404 page');
  await command({ type: 'nav.go', id: second, input: `${origin}/slow` });
  await command({ type: 'nav.go', id: second, input: `${origin}/b` });
  await pageAt('/b');
  assert.equal(await other.evaluate('!!document.querySelector("[data-nav-error]")'), false, 'replaced navigation must not show an error');
  await command({ type: 'nav.go', id: second, input: `${origin}/download` });
  await waitFor(async () => { try { return (await readFile(resolve(base, 'downloads/sol-check.txt'), 'utf8')).includes('regression'); } catch { return false; } }, 'download');
  assert.equal(await other.evaluate('!!document.querySelector("[data-nav-error]")'), false, 'downloads must not become error pages');
  console.log('PASS navigation replacement, HTTP 404 and downloads');
  await command({ type: 'nav.go', id: second, input: `${origin}/reset` });
  await waitFor(() => other.evaluate('!!document.querySelector("[data-nav-error]")'), 'connection reset error');
  assert.equal(await other.evaluate('document.querySelector("h1").textContent'), 'Connexion interrompue');
  const errorShot = await other.call('Page.captureScreenshot', { format: 'png' });
  await writeFile(resolve(base, 'error.png'), Buffer.from(errorShot.data, 'base64'));
  await command({ type: 'nav.go', id: second, input: `${origin}/b` });
  await pageAt('/b');
  console.log('PASS download does not suppress the following real error');
  await command({type:'settings.set',id:'theme',index:0});
  await other.evaluate(`document.body.style.cssText='background:#121212;min-height:100vh;color:white';`);
  await waitFor(() => ui.evaluate('document.documentElement.dataset.theme === "dark" && getComputedStyle(document.documentElement).getPropertyValue("--page-bg").trim() === "rgb(18, 18, 18)"'), 'dark page theme');
  await other.evaluate('document.body.style.backgroundColor = "#fafafa"');
  await waitFor(() => ui.evaluate('document.documentElement.dataset.theme === "light"'), 'site switches to light without navigation');
  await other.evaluate('document.body.style.backgroundColor = "#121212"');
  await waitFor(() => ui.evaluate('document.documentElement.dataset.theme === "dark"'), 'site switches back to dark');
  console.log('PASS default theme follows dark content and live site theme changes');
  await other.evaluate(`document.body.style.backgroundColor='oklch(0.2 0 0)'; const accent=document.createElement('button');accent.textContent='Accent';accent.style.cssText='background:oklch(0.65 0.2 140);color:white;width:160px;height:48px';document.body.append(accent);`);
  await waitFor(() => ui.evaluate('document.documentElement.dataset.theme === "dark" && document.documentElement.style.getPropertyValue("--site-accent").startsWith("rgb(")'), 'modern CSS background and accent');
  let forcedDarkBackground;
  for (const [index, theme] of [[1,'light'],[2,'dark'],[3,'dark'],[0,'dark']]) {
    await command({type:'settings.set',id:'theme',index});
    await waitFor(() => ui.evaluate(`document.documentElement.dataset.theme === ${JSON.stringify(theme)} && document.documentElement.style.getPropertyValue('--site-accent').startsWith('rgb(') && document.documentElement.style.getPropertyValue('--site-chrome-accent').startsWith('rgb(')`), `dynamic accent in theme ${index}`);
    if (index !== 3) {
      await delay(300);
      const background = await ui.evaluate('document.documentElement.style.getPropertyValue("--page-bg")');
      if (index === 2) forcedDarkBackground = background;
      if (index === 0) assert.equal(background, forcedDarkBackground, 'default and dark themes share the dark site background');
      const shot = await sidebar.call('Page.captureScreenshot', {format:'png'});
      await writeFile(resolve(base, `colors-${index === 0 ? 'default' : theme}.png`), Buffer.from(shot.data, 'base64'));
    }
  }
  await other.evaluate('document.body.style.backgroundColor = "color(display-p3 0.035 0.035 0.035)"');
  await waitFor(() => ui.evaluate('document.documentElement.style.getPropertyValue("--page-bg") === "rgb(9, 9, 9)"'), 'display-p3 dark background');
  console.log('PASS modern CSS colors and dynamic accents in all browser themes');
  if (process.env.SOL_CHECK_DEVBENIN === '1') {
    await command({type:'nav.go',id:second,input:'https://www.devbenin.bj'});
    await waitFor(() => other.evaluate('location.hostname.endsWith("devbenin.bj") && document.readyState === "complete"'), 'DevBenin loaded', 40000);
    await delay(1500);
    await other.evaluate('localStorage.setItem("theme","dark");location.reload()');
    await waitFor(() => other.evaluate('document.documentElement?.classList.contains("dark") && document.readyState === "complete"'), 'DevBenin dark mode', 40000);
    await delay(2000);
    const colors = await other.evaluate(`JSON.stringify({html:getComputedStyle(document.documentElement).backgroundColor,body:getComputedStyle(document.body).backgroundColor,classes:document.documentElement.className,stack:document.elementsFromPoint(innerWidth/2,12).map(el=>({tag:el.tagName,class:el.className,bg:getComputedStyle(el).backgroundColor,image:getComputedStyle(el).backgroundImage}))})`);
    console.log('DevBenin colors: ' + colors);
    console.log('Sol theme: ' + await ui.evaluate('JSON.stringify({theme:document.documentElement.dataset.theme,bg:document.documentElement.style.getPropertyValue("--page-bg")})'));
    await waitFor(() => ui.evaluate('document.documentElement.dataset.theme === "dark"'), 'DevBenin drives dark browser theme');
    console.log('PASS DevBenin dark mode drives the default browser theme');
    const screenshot = await sidebar.call('Page.captureScreenshot',{format:'png'});
    await writeFile(resolve(base,'devbenin-sidebar.png'),Buffer.from(screenshot.data,'base64'));
    await command({type:'nav.go',id:second,input:`${origin}/b`});
    await pageAt('/b');
  }
  await other.evaluate(`document.body.style.cursor = 'none'; window.__pointer = null; document.addEventListener('pointermove', e => window.__pointer = {x:e.clientX,y:e.clientY});`);
  await other.call('Input.dispatchMouseEvent', {type:'mouseMoved',x:100,y:100});
  await waitFor(() => other.evaluate('window.__pointer !== null'), 'custom pointer movement at zoom');
  assert.equal(await other.evaluate('getComputedStyle(document.body).cursor'), 'none', 'site controls its own cursor');
  await other.evaluate(`document.body.style.cursor = 'url("data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 width=%2224%22 height=%2224%22%3E%3Ccircle cx=%2212%22 cy=%2212%22 r=%228%22 fill=%22red%22/%3E%3C/svg%3E") 12 12, auto';`);
  assert.match(await other.evaluate('getComputedStyle(document.body).cursor'), /url\(/, 'CSS image cursor preserved');
  console.log('PASS site cursor styles and pointer events at zoom');
  await other.evaluate(`(async () => {
    const canvas = document.createElement('canvas'); canvas.width = 160; canvas.height = 280;
    const ctx = canvas.getContext('2d'); function draw(){ctx.fillStyle='green';ctx.fillRect(0,0,160,280);window.__videoDraw=requestAnimationFrame(draw)} draw();
    const video = document.createElement('video'); video.muted=true; video.style.cssText='width:160px;height:280px';
    video.srcObject=canvas.captureStream(20); document.body.append(video); await video.play();
  })()`);
  await command({ type: 'tab.activate', id: first });
  await waitFor(async () => (await readFile(resolve(profile, 'debug.log'), 'utf8')).includes(`pip.enter ${second}`), 'portrait video PiP');
  await command({ type: 'tab.activate', id: second });
  await waitFor(async () => (await readFile(resolve(profile, 'debug.log'), 'utf8')).includes(`pip.exit ${second}`), 'PiP returns to tab');
  await other.evaluate('document.querySelector("video").pause(); cancelAnimationFrame(window.__videoDraw)');
  const pipEntries = (await readFile(resolve(profile, 'debug.log'), 'utf8')).split(`pip.enter ${second}`).length;
  await command({ type: 'tab.activate', id: first });
  await delay(500);
  assert.equal((await readFile(resolve(profile, 'debug.log'), 'utf8')).split(`pip.enter ${second}`).length, pipEntries, 'paused video must not reopen stale PiP');
  console.log('PASS portrait PiP, return to tab and paused video');
  await command({ type: 'network.toggle' });
  const panelTarget = await waitFor(async () => (await targets()).find(t => t.url.endsWith('#panel')), 'network panel');
  const panel = await connect(panelTarget);
  await page.evaluate(`fetch(${JSON.stringify(`${origin}/before-sleep`)})`);
  await waitFor(() => panel.evaluate('document.body.innerText.includes("before-sleep")'), 'network events before discard');
  await command({ type: 'tab.activate', id: second });
  await waitFor(async () => (await state()).tabs.find(t => t.id === first)?.asleep, 'inactive tab discarded', 25000);
  await command({ type: 'tab.activate', id: first });
  const awake = await pageAt('/a');
  assert.ok((await awake.evaluate('innerWidth')) > initialWidth * 1.08, 'waking a discarded tab must preserve zoom');
  await awake.evaluate(`fetch(${JSON.stringify(`${origin}/after-sleep`)})`);
  await waitFor(() => panel.evaluate('document.body.innerText.includes("after-sleep")'), 'network events after discard');
  await command({ type: 'network.toggle' });
  console.log('PASS zoom survives memory discard and wake');
  console.log('PASS network inspector reconnects after memory discard');
  await waitFor(async () => {
    const saved = JSON.parse(await readFile(resolve(profile, 'session.json'), 'utf8').catch(() => '{}'));
    return saved.Urls?.[saved.ActiveIndex] === `${origin}/a`;
  }, 'session autosaved before closing');
  console.log('PASS session autosaved while the browser is running');
  await stop();
  const saved = JSON.parse(await readFile(resolve(profile, 'session.json'), 'utf8'));
  const expectedUrl = saved.Urls[saved.ActiveIndex];
  await launch();
  await waitFor(async () => { const s = await state(); return s.tabs.find(t => t.id === s.activeId)?.url === expectedUrl; }, 'last active tab restored');
  assert.equal((await state()).tabs.filter(t => !t.asleep).length, 1, 'restart loads only the last active tab');
  await command({ type:'tab.close', id:(await state()).activeId });
  await waitFor(async () => { const s = await state(); return s.tabs.find(t => t.id === s.activeId)?.url === `${origin}/a`; }, 'new tab after closing last loaded tab');
  assert.equal((await state()).tabs.filter(t => t.asleep).length, 1, 'closing does not wake the remaining inactive tab');
  console.log('PASS restore and close never wake inactive tabs');
  await waitFor(async () => (await state()).tabs.every(t => t.zoom === 90), 'zoom after restart');
  await command({ type: 'zoom.reset' });
  await waitFor(async () => (await state()).tabs.every(t => t.zoom === 100), 'reset all pages');
  await command({ type: 'settings.open', x: 900, y: 58 });
  const popupTarget = await waitFor(async () => (await targets()).find(t => t.url.endsWith('#popup')), 'settings popup');
  const popup = await connect(popupTarget);
  await waitFor(() => popup.evaluate('document.body.innerText.includes("Conservé pour toutes les pages")'), 'profile zoom label');
  assert.equal(await popup.evaluate('document.querySelectorAll(".settings-nav button").length'),5);
  await popup.evaluate('Array.from(document.querySelectorAll(".settings-nav button")).find(b=>b.textContent === "Apparence").click()');
  assert.ok(await popup.evaluate('Array.from(document.querySelectorAll("[role=radio]")).some(b=>b.textContent === "80 %" && b.getBoundingClientRect().height > 0)'), 'interface size controls visible');
  const cookiePage = await pageAt('/a');
  await command({type:'popup.close'});
  await cookiePage.evaluate('window.__geoDenied = false; navigator.geolocation.getCurrentPosition(() => {}, e => window.__geoDenied = e.code === 1)');
  await waitFor(() => popup.evaluate('document.body.innerText.includes("connaître votre position")'), 'native site permission prompt');
  await command({type:'permission.answer',index:0});
  await waitFor(() => cookiePage.evaluate('window.__geoDenied'), 'geolocation denied');
  const permissions = JSON.parse(await readFile(resolve(profile,'permissions.json'),'utf8'));
  assert.equal(permissions[`${origin} Geolocation`],false);
  console.log('PASS native permission prompt and persisted denial');
  await delay(650); // fermeture sur blur (150 ms), puis protection contre le recliquage (350 ms)
  await command({type:'settings.open',x:900,y:58});
  await waitFor(() => popup.evaluate('!!document.querySelector(".settings-nav")'), 'settings sections');
  await popup.evaluate('Array.from(document.querySelectorAll(".settings-nav button")).find(b => b.textContent === "Sécurité").click()');
  await waitFor(() => popup.evaluate('document.body.innerText.includes("Cookies et données des sites")'), 'cookie settings reopened');
  await cookiePage.call('Network.setCookie', {name:'sol-check',value:'test',url:origin,httpOnly:true,sameSite:'Lax'});
  await cookiePage.evaluate('localStorage.setItem("sol-check", "test")');
  assert.ok((await cookiePage.call('Network.getCookies',{urls:[origin]})).cookies.some(c => c.name === 'sol-check'));
  await command({type:'site-data.clear'});
  await waitFor(() => popup.evaluate('document.body.innerText.includes("Données effacées")'), 'site data cleared');
  assert.equal((await cookiePage.call('Network.getCookies',{urls:[origin]})).cookies.some(c => c.name === 'sol-check'),false);
  assert.equal(await cookiePage.evaluate('localStorage.getItem("sol-check")'),null);
  console.log('PASS profile cookie and site-storage deletion, including HttpOnly cookies');
  const settingsShot = await popup.call('Page.captureScreenshot', { format: 'png' });
  await writeFile(resolve(base, 'settings.png'), Buffer.from(settingsShot.data, 'base64'));
  await command({ type: 'popup.close' });
  console.log('PASS zoom survives restart and resets across all tabs');
  await delay(650);
  const designSidebar = await connect((await targets()).find(t => t.url.endsWith('#sidebar')));
  await waitFor(() => designSidebar.evaluate('document.querySelectorAll(".sidebar-quick-actions button").length === 3 && document.querySelector(".sidebar-brand img").naturalWidth > 0'), 'sidebar branding and quick actions');
  assert.equal(await ui.evaluate('!!document.querySelector("button[aria-label=Téléchargements],button[aria-label=\\"Favoris et historique\\"]")'), false, 'no duplicate toolbar shortcuts');
  await designSidebar.evaluate('document.querySelector("button[aria-label=Historique]").click()');
  await waitFor(() => popup.evaluate('document.querySelector("[role=tab][aria-selected=true]")?.textContent.includes("Historique")'), 'history shortcut selects history directly');
  await command({type:'popup.close'});
  const sidebarShot = await designSidebar.call('Page.captureScreenshot',{format:'png'});
  await writeFile(resolve(base,'sidebar-design.png'),Buffer.from(sidebarShot.data,'base64'));
  await ui.call('Emulation.setDeviceMetricsOverride',{width:850,height:52,deviceScaleFactor:1,mobile:false});
  assert.ok(await ui.evaluate('document.querySelector(".navigation-actions").getBoundingClientRect().right < document.querySelector(".topbar-center").getBoundingClientRect().left && document.querySelector(".topbar-center").getBoundingClientRect().right <= document.querySelector(".topbar-right").getBoundingClientRect().left'), 'toolbar groups do not overlap at 850px');
  await ui.call('Emulation.clearDeviceMetricsOverride');
  console.log('PASS sidebar branding, quick history action and toolbar layout');
  await command({type:'tab.open',url:`${origin}/preview-check`});
  await pageAt('/preview-check');
  const previewTab=(await state()).activeId;
  await command({type:'tab.close',id:previewTab});
  await waitFor(async()=>!(await state()).tabs.some(t=>t.id===previewTab),'preview source tab closed');
  await command({type:'bookmark.add',url:`${origin}/b`,title:'Favorite check'});
  await command({type:'group.create',id:(await state()).activeId,title:'Start group'});
  await command({type:'settings.text',id:'home',input:''});
  await command({type:'tab.open'});
  const startUrl = 'https://start.sol/index.html#start';
  const startTarget = await waitFor(async () => (await targets()).find(t=>t.url===startUrl), 'local start target');
  const startPage = await connect(startTarget);
  await waitFor(()=>startPage.evaluate('document.querySelector(".start-favorites")?.textContent.includes("Favorite check") && document.querySelector(".start-groups")?.textContent.includes("Start group") && document.querySelectorAll(".start-history button").length > 0'), 'real profile data on start page');
  await waitFor(()=>startPage.evaluate('document.querySelector(".start-favorites img")?.naturalWidth > 0 && document.querySelector(".start-preview-image > img")?.naturalWidth > 0'),'real favicon and page preview');
  await waitFor(()=>startPage.evaluate('document.querySelector(".start-favorites img")?.naturalWidth === 192'), 'high resolution declared icon preferred');
  assert.equal(await startPage.evaluate('document.documentElement.dataset.wallpaper'),'false');
  assert.equal(await startPage.evaluate('getComputedStyle(document.body).backgroundImage'),'none');
  await command({type:'settings.set',id:'startWallpaper',index:1});
  await waitFor(()=>startPage.evaluate('document.documentElement.dataset.wallpaper === "true" && getComputedStyle(document.body).backgroundImage.includes("start-background-")'),'optional start wallpaper enabled');
  assert.equal(await startPage.evaluate('getComputedStyle(document.body).backgroundSize'),'cover');
  assert.equal(await startPage.evaluate('document.querySelectorAll(".start-preview-placeholder img").length'),0);
  assert.equal(await startPage.evaluate('document.documentElement.dataset.theme'),await ui.evaluate('document.documentElement.dataset.theme'));
  const startWidth = await startPage.evaluate('innerWidth');
  await command({type:'zoom.out'});
  await delay(200);
  assert.equal(await startPage.evaluate('innerWidth'), startWidth, 'start ignores page zoom');
  const startState = await state();
  assert.equal(startState.tabs.find(t=>t.id===startState.activeId)?.zoom,100);
  await command({type:'settings.set',id:'uiScale',index:2});
  await waitFor(()=>ui.evaluate('document.body.style.zoom === "0.8"'),'80% interface scale');
  assert.ok(await designSidebar.evaluate('document.body.style.zoom === "0.8"'));
  assert.ok(await startPage.evaluate('!document.body.style.zoom || document.body.style.zoom === "1"'), 'start remains at 100%');
  assert.equal(JSON.parse(await readFile(resolve(profile,'settings.json'),'utf8')).UiScale,0.8);
  await command({type:'settings.set',id:'uiScale',index:0});
  await waitFor(()=>ui.evaluate('document.body.style.zoom === "1"'),'restore interface scale');
  const tabCount = (await state()).tabs.length;
  await command({type:'split.open'});
  await waitFor(async()=> (await state()).tabs.length === tabCount + 1,'new split start tab');
  await waitFor(async()=> (await targets()).filter(t=>t.url===startUrl).length === 2,'both split panes show start');
  await command({type:'split.close'});
  await waitFor(async()=> (await state()).tabs.length === tabCount,'untouched split start cleaned up');
  console.log('PASS start zoom isolation, interface scaling and new split start page');
  const savedPreviews = await readdir(resolve(profile,'start-previews'));
  assert.ok(savedPreviews.some(file=>file.endsWith('.png')), 'URL previews persist in the profile');
  assert.equal(await ui.evaluate('document.querySelector(".address-pill input").value'), '');
  assert.equal(await startPage.evaluate('document.querySelector(".start-history").textContent.includes("start.sol")'), false);
  const startShot=await startPage.call('Page.captureScreenshot',{format:'png'});
  await writeFile(resolve(base,'start-page.png'),Buffer.from(startShot.data,'base64'));
  await command({type:'settings.set',id:'theme',index:1});
  await waitFor(()=>startPage.evaluate('document.documentElement.dataset.theme === "light"'),'start page follows light browser theme');
  await startPage.call('Emulation.setDeviceMetricsOverride',{width:360,height:650,deviceScaleFactor:1,mobile:false});
  assert.ok(await startPage.evaluate('document.documentElement.scrollWidth <= innerWidth'),'start page fits a narrow split pane');
  assert.equal(await startPage.evaluate('getComputedStyle(document.body).backgroundSize'),'cover');
  await startPage.call('Emulation.clearDeviceMetricsOverride');
  await command({type:'settings.set',id:'startWallpaper',index:0});
  await command({type:'settings.set',id:'theme',index:2});
  await startPage.evaluate('Array.from(document.querySelectorAll(".start-favorites button")).find(b=>b.textContent.includes("Favorite check")).click()');
  const navigated = await connect(await waitFor(async ()=>(await targets()).find(t=>t.id===startTarget.id && t.url===`${origin}/b`),'favorite navigation'));
  await waitFor(()=>navigated.evaluate('document.readyState === "complete" && document.body.textContent.includes("Server page")'),'favorite page loaded');
  await navigated.evaluate('try { window.chrome?.webview?.postMessage(JSON.stringify({type:"start.navigate",input:"https://example.com"})); } catch {}');
  await delay(300);
  assert.equal(await navigated.evaluate('location.href'), `${origin}/b`, 'websites cannot control the start bridge');
  console.log('PASS custom start page, real profile data, favorite navigation and bridge isolation');
  await stop();
  await launch(['--private']);
  await waitFor(() => ui.evaluate('document.documentElement.dataset.private === "true" && document.documentElement.style.getPropertyValue("--tab-accent") === "#8b5cf6"'), 'native private window purple identity');
  const privateSidebar = await connect((await targets()).find(t => t.url.endsWith('#sidebar')));
  await waitFor(() => privateSidebar.evaluate('document.querySelector(".sidebar-profile")?.textContent.includes("Privé")'), 'private profile label');
  const privateShot = await privateSidebar.call('Page.captureScreenshot',{format:'png'});
  await writeFile(resolve(base,'sidebar-private.png'),Buffer.from(privateShot.data,'base64'));
  await command({type:'tab.open'});
  const privateStart = await connect(await waitFor(async ()=>(await targets()).find(t=>t.url==='https://start.sol/index.html#start'),'private start page'));
  await waitFor(()=>privateStart.evaluate('document.documentElement?.dataset.private === "true"'),'private start data');
  assert.equal(await privateStart.evaluate('document.querySelectorAll(".start-history button,.start-frequent button").length'),0);
  console.log('PASS private window purple theme and no visits on private start page');
  await stop();
} catch (error) {
  if (ui) console.error(await ui.evaluate('JSON.stringify({ready:document.readyState,listening:window.__checkListening,lastEvent:window.__checkLastMessage?.event,text:document.body?.innerText.slice(0,300)})').catch(() => 'UI unavailable'));
  console.error(await readFile(resolve(profile, 'debug.log'), 'utf8').catch(() => 'No diagnostic log'));
  throw error;
} finally {
  for (const ws of connections) ws.close();
  if (app && app.exitCode === null) app.kill();
  server.close();
}
