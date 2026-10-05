// Windows integration check: node tools/check-navigation.mjs [path/to/Sol.exe]
// Uses a disposable profile and a local server; never touches the user's session.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdir, writeFile, readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const exe = resolve(process.argv[2] ?? 'src/Browser.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Sol.exe');
const base = resolve(`bin/navigation-check-${Date.now()}`);
const profile = resolve(base, 'profiles/robustness');
await mkdir(profile, { recursive: true });
await mkdir(resolve(base, 'downloads'), { recursive: true });
const server = createServer((req, res) => {
  if (req.url === '/reset') return req.socket.resetAndDestroy();
  if (req.url === '/redirect') { res.writeHead(302, { Location: '/redirect' }); return res.end(); }
  if (req.url === '/download') {
    res.writeHead(200, { 'Content-Type': 'application/octet-stream', 'Content-Disposition': 'attachment; filename="sol-check.txt"' });
    return res.end('Sol navigation regression check');
  }
  res.writeHead(req.url === '/missing' ? 404 : 200, { 'Content-Type': 'text/html' });
  const respond = () => res.end(`<html><head><title>Check ${req.url}</title></head><body>Server page ${req.url}</body></html>`);
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
async function launch() {
  app = spawn(exe, ['--profile=robustness', '--bounds=-9000,-9000,1100,800'], { windowsHide: true, env: { ...process.env, NAVIGATEUR_DATA_DIR: base, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port} --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows`, NAVIGATEUR_DEBUG: '1', NAVIGATEUR_TEST_DOWNLOAD_DIR: resolve(base, 'downloads'), NAVIGATEUR_SAVER_TEST: '1' } });
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
