import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { sidebarHost } from './sidebar-check-host.mjs';

const favicon = '<svg xmlns="http://www.w3.org/2000/svg" width="96" height="96" viewBox="0 0 32 32"><rect width="32" height="32" rx="7" fill="#286a4f"/><path d="M9 17l5 5 10-12" fill="none" stroke="white" stroke-width="3"/></svg>';
const tinyPng = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY1DqVvoPAANxAc/MfZsgAAAAAElFTkSuQmCC', 'base64');
const startUrl = 'https://start.sol/index.html#start';
let servesIcon = true;
let brokenRefreshes = 0;
let smallerIcons = 0;
const host = await sidebarHost(process.argv[2], {
  saverTest: true,
  performance: true,
  serve(req,res) {
    if (!['/pin', '/tab', '/no-icon', '/brand.svg', '/favicon.ico', '/apple-touch-icon.png'].includes(req.url)) return false;
    if (req.url === '/brand.svg') {
      if (servesIcon) {
        res.writeHead(200, { 'Content-Type': 'image/svg+xml' });
        res.end(favicon);
      } else { res.writeHead(404); res.end(); }
      return true;
    }
    if (req.url.endsWith('.ico') || req.url.endsWith('.png')) {
      if (!servesIcon && req.headers.host?.startsWith('127.0.0.1:')) {
        res.writeHead(200, { 'Content-Type': 'image/png' });
        if (req.url === '/apple-touch-icon.png') {
          brokenRefreshes++;
          res.end('This is not a PNG');
        } else {
          smallerIcons++;
          res.end(tinyPng);
        }
      } else { res.writeHead(404); res.end(); }
      return true;
    }
    res.writeHead(200, { 'Content-Type': 'text/html', 'Cache-Control': 'no-store' });
    const icon = servesIcon && req.url !== '/no-icon' ? '<link rel="icon" href="/brand.svg" sizes="any">' : '';
    res.end(`<!doctype html><meta charset="utf-8"><title>${req.url === '/pin' ? 'Épinglé' : 'Mon onglet'}</title>${icon}`);
    return true;
  },
});

let ui;
const send = command => ui.evaluate(`chrome.webview.postMessage(${JSON.stringify(JSON.stringify(command))})`);
const tab = url => ui.evaluate(`window.__sidebarState.tabs.find(t=>t.url===${JSON.stringify(url)})`);
const icon = (id, scope = '') => ui.evaluate(`document.querySelector(${JSON.stringify(scope + `[data-kind="tab"][data-id="${id}"] img.favicon`)})?.naturalWidth > 0`);
const favorite = async page => page.evaluate('document.querySelector(".start-favorites img")?.naturalWidth === 96');
const pinUrl = host.origin + '/pin';
const tabUrl = host.origin + '/tab';
const dbPath = resolve(host.profile, 'browser.db');
const savedIcon = () => {
  const db = new DatabaseSync(dbPath, { readOnly: true });
  try { return db.prepare('SELECT icon, checked FROM site_icons WHERE domain=?').get('127.0.0.1'); }
  finally { db.close(); }
};
const settingsPath = resolve(host.profile, 'settings.json');
const shortcutIcon = () => ui.evaluate('document.querySelector(".shortcuts-row img.favicon")?.naturalWidth > 0');
const prepareShortcuts = async () => {
  const settings = JSON.parse(await readFile(settingsPath, 'utf8'));
  await writeFile(settingsPath, JSON.stringify({ ...settings, Shortcuts: [{ Url: pinUrl, Name: 'Accès rapide', NameLocked: true }] }));
};
const prepareDatabase = () => {
  const db = new DatabaseSync(dbPath);
  db.exec('CREATE TABLE site_icons(domain TEXT PRIMARY KEY,icon TEXT,checked INTEGER NOT NULL); CREATE TABLE bookmarks(url TEXT PRIMARY KEY,title TEXT NOT NULL,added INTEGER NOT NULL)');
  db.prepare('INSERT INTO bookmarks VALUES (?,?,?)').run(pinUrl, 'Le site épinglé', Date.now());
  db.close();
};

try {
  prepareDatabase();
  await prepareShortcuts();
  await writeFile(resolve(host.profile, 'session.json'), JSON.stringify({ Urls: [pinUrl, tabUrl, startUrl], PinnedCount: 1, ActiveIndex: 0 }));
  ui = await host.launch(720, 1060);
  const pinned = await tab(pinUrl);
  const regular = await tab(tabUrl);
  assert(pinned && regular);
  await host.wait(() => icon(pinned.id, '.pinned-row '), 'native favicon displayed on a pinned tab');
  await send({ type: 'tab.activate', id: regular.id });
  await host.wait(() => icon(regular.id), 'native favicon displayed on a regular tab');
  await send({ type: 'tab.activate', id: (await tab(startUrl)).id });
  const start = await host.connect(startUrl);
  await host.wait(() => favorite(start), 'declared scalable icon reaches the favorite at full quality');
  await host.wait(async () => (await tab(pinUrl)).asleep && (await tab(tabUrl)).asleep, 'both tabs discarded by the memory saver', 45000);
  assert(await icon(pinned.id, '.pinned-row '), 'pinned favicon survives discard');
  assert(await icon(regular.id), 'regular favicon survives discard');
  assert(await favorite(start), 'bookmark has its favicon while its tab is asleep');
  assert(await shortcutIcon(), 'shortcut has its favicon while its site is asleep');

  // Simulate an old positive cache and a restored session carrying no favicon payload.
  servesIcon = false;
  await host.close(ui); ui = null;
  const known = savedIcon().icon;
  assert(known, 'a successful icon is stored for the bookmark');
  const expired = Date.now() - 8 * 86400000;
  const db = new DatabaseSync(dbPath);
  try { db.prepare('UPDATE site_icons SET checked=?').run(expired); }
  finally { db.close(); }
  await prepareShortcuts();
  await writeFile(resolve(host.profile, 'session.json'), JSON.stringify({ Urls: [pinUrl, tabUrl, startUrl], PinnedCount: 1, ActiveIndex: 2 }));
  ui = await host.launch(720, 1060);
  const restoredPin = await tab(pinUrl);
  const restoredTab = await tab(tabUrl);
  const restoredStart = await host.connect(startUrl);
  await host.wait(() => favorite(restoredStart), 'bookmark restores its favicon from the old cache');
  await host.wait(() => icon(restoredPin.id, '.pinned-row '), 'sleeping pin restores its favicon from the site cache');
  await host.wait(() => icon(restoredTab.id), 'sleeping regular tab restores its favicon from the site cache');
  await host.wait(shortcutIcon, 'shortcut restores its favicon from the site cache');
  assert((await tab(pinUrl)).asleep && (await tab(tabUrl)).asleep);
  await host.wait(
    () => brokenRefreshes > 0 && smallerIcons > 0 && savedIcon().checked > expired,
    'malformed and smaller remote icons completed their refresh attempt',
  );
  assert.equal(savedIcon().icon, known, 'malformed and smaller replacements preserve the last good favicon on disk');
  assert(await favorite(restoredStart), 'the favorite still renders after the failed refresh');
  assert((await ui.evaluate(`document.querySelector('.pinned-row [data-id="${restoredPin.id}"] img.favicon').naturalWidth`)) === 96,
    'the inactive pinned tab retains the full-resolution icon');
  const sleepIconStyle = await ui.evaluate(`(() => {
    const image = document.querySelector('.pinned-row [data-id="${restoredPin.id}"] img.favicon');
    const style = getComputedStyle(image);
    return { opacity: style.opacity, filter: style.filter };
  })()`);
  assert.equal(sleepIconStyle.opacity, '1', 'sleep does not fade the favicon');
  assert(!sleepIconStyle.filter.includes('grayscale'), 'sleep preserves the favicon colors');

  const evidence = resolve('bin', 'sleeping-favicons');
  await mkdir(evidence, { recursive: true });
  for (const [name, page] of [['sidebar.png', ui], ['favorites.png', restoredStart]]) {
    const shot = await page.call('Page.captureScreenshot', { format: 'png' });
    await writeFile(resolve(evidence, name), Buffer.from(shot.data, 'base64'));
  }
  await send({ type: 'tab.activate', id: restoredPin.id });
  await host.wait(async () => !(await tab(pinUrl)).asleep && !(await tab(pinUrl)).loading, 'pinned tab wakes');
  assert(await icon(restoredPin.id, '.pinned-row '), 'empty favicon on wake preserves the stored icon');
  const anotherSite = pinUrl.replace('127.0.0.1', 'localhost').replace('/pin', '/no-icon');
  await send({ type: 'nav.go', id: restoredPin.id, input: anotherSite });
  await host.wait(async () => (await tab(anotherSite))?.loading === false && !(await icon(restoredPin.id, '.pinned-row ')), 'an unrelated site does not take the previous icon');
  console.log('PASS sleeping pinned and regular tabs, bookmark and shortcut, expired cache, wake and site change');
} finally {
  try { if (ui) await host.close(ui); }
  finally { await host.dispose(); }
}
