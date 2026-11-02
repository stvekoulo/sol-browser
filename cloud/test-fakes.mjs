// Doubles locaux : toute requête non prévue échoue ; aucun socket ni accès distant.
import assert from 'node:assert/strict';
export function fakeServices() {
  const uploads = new Map(), backups = new Map(), objects = new Map(), events = [];
  let tail = Promise.resolve(), failDelete = false;
  const result = rows => ({rows, rowCount: rows.length});
  async function query(sql, p = []) {
    sql = sql.replace(/\s+/g, ' ').trim(); events.push({sql, p});
    const [id, userId, bytes] = p;
    if (/^CREATE /.test(sql)) return result([]);
    if (sql.startsWith('SELECT count(*)')) {
      const rows = [...uploads.values()].filter(r => r.user_id === id);
      return result([{count: rows.length, bytes: rows.reduce((s, r) => s + r.bytes, 0)}]);
    }
    if (sql.startsWith('INSERT INTO backup_uploads')) {
      assert(!uploads.has(id)); uploads.set(id, {id, user_id: userId, bytes, expires_at: Date.now() + 3600000}); return result([]);
    }
    if (sql.startsWith('SELECT bytes FROM backup_uploads')) {
      const r = uploads.get(id); return result(r?.user_id === userId && r.expires_at > Date.now() ? [r] : []);
    }
    if (sql.startsWith('SELECT id, user_id FROM backup_uploads'))
      return result([...uploads.values()].filter(r => r.expires_at <= Date.now()).slice(0, 100));
    if (sql.startsWith('DELETE FROM backup_uploads')) {
      const r = uploads.get(id);
      const match = r && (sql.includes('RETURNING') ? r.user_id === userId && r.bytes === bytes && r.expires_at > Date.now() : r.expires_at <= Date.now());
      if (match) uploads.delete(id); return result(match ? [r] : []);
    }
    if (sql.startsWith('INSERT INTO backups')) {
      assert(!backups.has(id)); backups.set(id, {id, user_id: userId, device: p[2], bytes: p[3], created_at: Date.now()}); return result([]);
    }
    if (sql.startsWith('DELETE FROM backups') && sql.includes('NOT IN')) {
      const stale = [...backups.values()].filter(r => r.user_id === id).reverse().slice(5);
      for (const r of stale) backups.delete(r.id); return result(stale);
    }
    if (sql.startsWith('SELECT user_id ||')) {
      const keys = new Set(id);
      return result([...backups.values(), ...uploads.values()].map(r => ({key: `${r.user_id}/${r.id}.solbackup`})).filter(r => keys.has(r.key)));
    }
    if (/^SELECT (1|bytes) FROM backups/.test(sql) || sql.startsWith('DELETE FROM backups')) {
      const r = backups.get(id), found = r?.user_id === userId;
      if (found && sql.startsWith('DELETE')) backups.delete(id); return result(found ? [r] : []);
    }
    if (sql.startsWith('SELECT id, device, bytes::text')) return result([...backups.values()].filter(r => r.user_id === id));
    throw new Error(`unexpected SQL: ${sql}`);
  }
  const pool = {query, async connect() {
    let unlock, snapshot;
    return {async query(sql, p) {
      if (sql === 'BEGIN') return result([]);
      if (sql.startsWith('SELECT pg_advisory_xact_lock')) {
        const previous = tail; tail = new Promise(r => {unlock = r;}); await previous;
        snapshot = [structuredClone(uploads), structuredClone(backups)]; return result([]);
      }
      if (sql === 'ROLLBACK') {
        if (snapshot) {
          uploads.clear(); backups.clear();
          for (const [k, v] of snapshot[0]) uploads.set(k, v);
          for (const [k, v] of snapshot[1]) backups.set(k, v);
        }
        return result([]);
      }
      if (sql === 'COMMIT') return result([]);
      return query(sql, p);
    }, release() {unlock?.();}};
  }};
  const s3 = {async send(command) {
    const name = command.constructor.name, {Key, ContinuationToken, Body, Range} = command.input;
    events.push({name, Key, ContinuationToken});
    if (name === 'DeleteObjectCommand') {
      if (failDelete) throw new Error('storage unavailable');
      objects.delete(Key); return {};
    }
    if (name === 'ListObjectsV2Command') {
      const all = [...objects.entries()].sort(([a], [b]) => a.localeCompare(b)).filter(([k]) => !ContinuationToken || k > ContinuationToken);
      const page = all.slice(0, 1000), truncated = all.length > page.length;
      return {Contents: page.map(([Key, o]) => ({Key, LastModified: new Date(o.modified)})), IsTruncated: truncated,
        NextContinuationToken: truncated ? page.at(-1)[0] : undefined};
    }
    if (name === 'PutObjectCommand') {objects.set(Key, {body: Body, modified: Date.now()}); return {};}
    const obj = objects.get(Key); if (!obj) throw new Error('not found');
    if (name === 'HeadObjectCommand') return {ContentLength: obj.body.length};
    if (name === 'GetObjectCommand') return {Body: {async transformToByteArray() {return Range ? obj.body.subarray(0, 10) : obj.body;}}};
    throw new Error(`unexpected S3: ${name}`);
  }};
  return {pool, s3, uploads, backups, objects, events, setFailDelete(value) {failDelete = value;}};
}
