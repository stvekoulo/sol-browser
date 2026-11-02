import assert from 'node:assert/strict';
import {test} from 'node:test';
import {randomUUID} from 'node:crypto';
import {backupStorage, MAX_BYTES, objectKey} from './backup-storage.ts';
import {createBackupApp} from './backup-api.ts';
import {fakeServices} from './test-fakes.mjs';

globalThis.fetch = () => {throw new Error('network forbidden in local tests');};
const sealed = Buffer.from('SOLBACKUP1encrypted');
const fixture = () => {
  const f = fakeServices();
  f.app = createBackupApp({...f, verify: async token => ({sub: token}), sign: async command => `https://signed.invalid/${command.input.Key}`});
  f.call = (path, body, method = 'POST', user = 'alice') => f.app.request(path, {
    method, headers: {authorization: `Bearer ${user}`, 'content-type': 'application/json'},
    ...(method === 'GET' ? {} : {body: Buffer.isBuffer(body) ? body : JSON.stringify(body ?? {})}),
  });
  return f;
};
test('quota is reserved before signing; concurrent abandoned uploads cannot exceed five', async () => {
  const f = fixture();
  const responses = await Promise.all(Array.from({length: 12}, () => f.call('/backups/uploads', {bytes: 4096})));
  assert.equal(responses.filter(r => r.status === 201).length, 5);
  assert.equal(responses.filter(r => r.status === 429).length, 7);
  assert.equal(f.uploads.size, 5); assert.equal(f.objects.size, 0);
});
test('pending byte quota also blocks legacy route and isolates accounts', async () => {
  const f = fixture();
  assert.equal((await f.call('/backups/uploads', {bytes: MAX_BYTES})).status, 201);
  assert.equal((await f.call('/backups/uploads', {bytes: 20})).status, 429);
  assert.equal((await f.call('/backups', sealed)).status, 429);
  assert.equal((await f.call('/backups/uploads', {bytes: 20}, 'POST', 'bob')).status, 201);
  assert.equal(f.events.filter(e => e.name === 'PutObjectCommand').length, 0);
});
test('legacy uploads/download/delete and retention remain compatible', async () => {
  const f = fixture(); let first;
  for (let i = 0; i < 7; i++) {
    const response = await f.call('/backups?device=OldSol', sealed);
    assert.equal(response.status, 201); first ??= (await response.json()).id;
  }
  assert.equal(f.backups.size, 5); assert.equal(f.objects.size, 5); assert.equal(f.uploads.size, 0);
  assert.equal((await f.call(`/backups/${first}`, null, 'GET')).status, 404);
  const id = [...f.backups.keys()][0];
  assert.equal((await f.call(`/backups/${id}`, null, 'GET', 'bob')).status, 404);
  assert(Buffer.from(await (await f.call(`/backups/${id}`, null, 'GET')).arrayBuffer()).equals(sealed));
  assert.equal((await f.call(`/backups/${id}`, {}, 'DELETE')).status, 204);
});
test('finalization requires an owned, live reservation and the exact declared size', async () => {
  const f = fixture();
  const slot = await (await f.call('/backups/uploads', {bytes: sealed.length})).json();
  f.objects.set(objectKey('alice', slot.id), {body: sealed, modified: Date.now()});
  assert.equal((await f.call(`/backups/uploads/${slot.id}`, {}, 'POST', 'bob')).status, 404);
  assert.equal((await f.call(`/backups/uploads/${slot.id}`, {device: 'PC'})).status, 201);
  assert.equal(f.uploads.size, 0); assert.equal(f.backups.size, 1);
  assert.equal((await f.call(`/backups/uploads/${slot.id}`, {})).status, 201);
  const fake = await (await f.call('/backups/uploads', {bytes: 4096})).json();
  f.objects.set(objectKey('alice', fake.id), {body: sealed, modified: Date.now()});
  assert.equal((await f.call(`/backups/uploads/${fake.id}`, {})).status, 400);
  assert(!f.objects.has(objectKey('alice', fake.id)));
  assert.equal((await f.call(`/backups/uploads/${randomUUID()}`, {})).status, 404);
});
test('scheduled cleanup reclaims abandoned uploads without any successful finalization', async () => {
  const f = fixture();
  const {id} = await (await f.call('/backups/uploads', {bytes: sealed.length})).json();
  f.uploads.get(id).expires_at = Date.now() - 1;
  f.objects.set(objectKey('alice', id), {body: sealed, modified: Date.now() - 7200000});
  assert.equal((await f.app.request('/internal/cleanup', {method: 'POST', body: '{}'})).status, 401);
  const delivery = {version: 1, invocation_id: 'local', trigger: {type: 'schedule', id: 'trigger-local', name: 'backup-cleanup'}, data: {scheduled_at: new Date().toISOString()}};
  const response = await f.app.request('/internal/cleanup', {method: 'POST', headers: {'x-neon-trigger-invocation-id': 'local'}, body: JSON.stringify(delivery)});
  assert.equal(response.status, 204); assert.equal(f.uploads.size, 0); assert.equal(f.objects.size, 0);
});
test('cleanup walks every storage page and preserves registered/current uploads', async () => {
  const f = fakeServices(), storage = backupStorage(f.pool, f.s3);
  const old = Date.now() - 7200000;
  for (let i = 0; i < 2101; i++) f.objects.set(`orphan/${String(i).padStart(4, '0')}`, {modified: old});
  f.backups.set('kept', {id: 'kept', user_id: 'alice'});
  f.uploads.set('active', {id: 'active', user_id: 'alice', expires_at: Date.now() + 3600000});
  f.objects.set('alice/kept.solbackup', {modified: old});
  f.objects.set('alice/active.solbackup', {modified: old});
  f.objects.set('recent', {modified: Date.now()});
  await storage.cleanup();
  assert.deepEqual([...f.objects.keys()].sort(), ['alice/active.solbackup', 'alice/kept.solbackup', 'recent']);
  assert.equal(f.events.filter(e => e.name === 'ListObjectsV2Command').length, 3);
});
test('storage failure never releases quota for an expired upload', async () => {
  const f = fakeServices(), storage = backupStorage(f.pool, f.s3), id = randomUUID();
  await storage.reserve('alice', id, MAX_BYTES);
  f.uploads.get(id).expires_at = Date.now() - 1;
  f.setFailDelete(true);
  await assert.rejects(storage.cleanup(), /storage unavailable/);
  assert.equal(await storage.reserve('alice', randomUUID(), 20), false);
  f.setFailDelete(false); await storage.cleanup();
  assert.equal(await storage.reserve('alice', randomUUID(), 20), true);
});
