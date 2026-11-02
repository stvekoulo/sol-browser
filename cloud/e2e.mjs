// Preuve de bout en bout de la sauvegarde cloud : compte, jeton, envoi, isolation entre comptes, restauration, suppression.
// À lancer sur une branche jetable uniquement : `node cloud/e2e.mjs <fichier .env>`. Les comptes créés partent avec la branche.
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import {readCloudEnv, verifyCloudTestEnv} from '../tools/cloud-test-guard.mjs';
import pg from 'pg';

const env = readCloudEnv(process.argv[2] ?? '.env.local');
const {auth, api} = await verifyCloudTestEnv(env);
const ORIGIN = 'https://ui.navigateur';
const ok = label => console.log(`ok - ${label}`);

async function account(tag) {
  const email = `sol-e2e-${tag}-${Date.now()}@example.com`, password = randomBytes(18).toString('base64url');
  const post = (path, body, cookie) => fetch(`${auth}${path}`, { method: 'POST', headers: { 'Content-Type': 'application/json', Origin: ORIGIN, ...(cookie ? { Cookie: cookie } : {}) }, body: JSON.stringify(body) });
  const up = await post('/sign-up/email', { email, password, name: tag });
  assert.equal(up.status, 200, `sign-up ${await up.clone().text()}`);
  const inn = await post('/sign-in/email', { email, password });
  assert.equal(inn.status, 200, `sign-in ${await inn.clone().text()}`);
  const cookie = inn.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
  const token = await fetch(`${auth}/token`, { headers: { Cookie: cookie, Origin: ORIGIN } });
  assert.equal(token.status, 200, `token ${await token.clone().text()}`);
  return { jwt: (await token.json()).token, cookie, email };
}
const call = (user, path, init = {}) => fetch(`${api}${path}`, { ...init, headers: { ...(init.headers ?? {}), ...(user ? { Authorization: `Bearer ${user.jwt}` } : {}) } });

assert.equal(await (await call(null, '/health')).text(), 'ok'); ok('Functions répond');
assert.equal((await call(null, '/backups')).status, 401); ok('sans jeton : 401');
assert.equal((await call({ jwt: 'faux.jeton.signe' }, '/backups')).status, 401); ok('jeton forgé : 401');

const a = await account('a'), b = await account('b'); ok('Auth : inscription, connexion et jeton pour deux comptes');
assert.equal((await call(a, '/backups', { method: 'POST', body: 'pas une sauvegarde' })).status, 400); ok('fichier non chiffré refusé');

const sealed = Buffer.concat([Buffer.from('SOLBACKUP1'), randomBytes(200 * 1024)]);
const created = await call(a, '/backups?device=Test', { method: 'POST', body: sealed });
assert.equal(created.status, 201, await created.clone().text());
const { id } = await created.json(); ok('Object Storage + Postgres : sauvegarde envoyée');

const list = await (await call(a, '/backups')).json();
assert.equal(list.backups.length, 1); assert.equal(list.backups[0].bytes, sealed.length); ok('liste du compte');
const back = Buffer.from(await (await call(a, `/backups/${id}`)).arrayBuffer());
assert(back.equals(sealed)); ok('restauration identique à l’octet près');

assert.deepEqual((await (await call(b, '/backups')).json()).backups, []);
assert.equal((await call(b, `/backups/${id}`)).status, 404);
assert.equal((await call(b, `/backups/${id}`, { method: 'DELETE' })).status, 404); ok('un autre compte ne voit ni ne supprime rien');

const db = new pg.Client({ connectionString: env.DATABASE_URL });
await db.connect();
const row = await db.query('SELECT user_id, bytes FROM backups WHERE id = $1', [id]);
assert.equal(row.rowCount, 1); assert.equal(Number(row.rows[0].bytes), sealed.length);
assert.deepEqual(Object.keys((await db.query('SELECT * FROM backups LIMIT 1')).rows[0]).sort(), ['bytes', 'created_at', 'device', 'id', 'user_id']);
await db.end(); ok('Postgres : seules des métadonnées, aucun contenu');

for (let i = 0; i < 6; i++) assert.equal((await call(a, '/backups', { method: 'POST', body: sealed })).status, 201);
assert.equal((await (await call(a, '/backups')).json()).backups.length, 5); ok('cinq sauvegardes conservées par compte');
assert.equal((await call(a, `/backups/${id}`)).status, 404); ok('la plus ancienne est bien retirée');
const latest = (await (await call(a, '/backups')).json()).backups[0].id;
assert.equal((await call(a, `/backups/${latest}`, { method: 'DELETE' })).status, 204); ok('suppression');
// Au-delà de ~10 Mo, la passerelle refuse le corps : la sauvegarde passe par une adresse signée vers le stockage.
const json = body => ({ method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
const big = Buffer.concat([Buffer.from('SOLBACKUP1'), randomBytes(24 * 1024 * 1024)]);
assert.equal((await call(a, '/backups', { method: 'POST', body: big })).status, 413); ok('24 Mo en direct : refusé par la passerelle');
assert.equal((await call(a, '/backups/uploads', json({ bytes: 300 * 1024 * 1024 }))).status, 413); ok('au-delà de 256 Mo : refusé avant envoi');
const slot = await (await call(a, '/backups/uploads', json({ bytes: big.length }))).json();
assert.equal((await fetch(slot.url, { method: 'PUT', body: big.subarray(0, 1000), headers: { 'Content-Type': 'application/octet-stream' } })).status >= 400, true); ok('adresse signée : une autre taille est refusée');
const put = await fetch(slot.url, { method: 'PUT', body: big, headers: { 'Content-Type': 'application/octet-stream' } });
assert.equal(put.status, 200, await put.clone().text());
assert.equal((await call(b, `/backups/uploads/${slot.id}`, json({ device: 'Autre' }))).status, 404); ok('un autre compte ne peut pas valider cet envoi');
const done = await call(a, `/backups/uploads/${slot.id}`, json({ device: 'Grand PC' }));
assert.equal(done.status, 201, await done.clone().text()); ok('24 Mo envoyés directement au stockage et validés');
const listed = (await (await call(a, '/backups')).json()).backups.find(x => x.id === slot.id);
assert.equal(listed.bytes, big.length); assert.equal(listed.device, 'Grand PC');
const { url } = await (await call(a, `/backups/${slot.id}/url`)).json();
assert(Buffer.from(await (await fetch(url)).arrayBuffer()).equals(big)); ok('restauration de 24 Mo identique à l’octet près');
assert.equal((await call(b, `/backups/${slot.id}/url`)).status, 404); ok('un autre compte n’obtient pas d’adresse de lecture');
const fake = await (await call(a, '/backups/uploads', json({ bytes: 4096 }))).json();
await fetch(fake.url, { method: 'PUT', body: randomBytes(4096), headers: { 'Content-Type': 'application/octet-stream' } });
assert.equal((await call(a, `/backups/uploads/${fake.id}`, json({}))).status, 400); ok('un fichier qui n’est pas une sauvegarde est refusé et effacé');
assert.equal((await call(a, `/backups/uploads/${fake.id}`, json({}))).status, 404);
// Un compte peut abandonner ses envois, mais ne peut jamais obtenir un nombre illimité d'adresses.
const abandoned = await Promise.all(Array.from({length: 8}, () => call(b, '/backups/uploads', json({bytes: sealed.length}))));
assert.equal(abandoned.filter(r => r.status === 201).length, 5);
assert.equal(abandoned.filter(r => r.status === 429).length, 3);
ok('envois abandonnés simultanés : cinq réservations maximum, surplus refusé');
console.log('PASS cloud backup');
