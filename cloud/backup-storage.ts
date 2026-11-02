import {DeleteObjectCommand, ListObjectsV2Command} from "@aws-sdk/client-s3";

export const BUCKET = "backups", MAX_BYTES = 256 * 1024 * 1024, MAX_PENDING = 5;
export const MAGIC = Buffer.from("SOLBACKUP1");
export const objectKey = (userId: string, id: string) => `${userId}/${id}.solbackup`;

// Les réservations expirées comptent dans le quota jusqu'à l'effacement effectif de l'objet.
export function backupStorage(pool: any, s3: any) {
  async function initialize() {
    await pool.query(`CREATE TABLE IF NOT EXISTS backups (
      id uuid PRIMARY KEY DEFAULT gen_random_uuid(), user_id text NOT NULL, device text NOT NULL,
      bytes bigint NOT NULL, created_at timestamptz NOT NULL DEFAULT now())`);
    await pool.query("CREATE INDEX IF NOT EXISTS backups_user ON backups (user_id, created_at DESC)");
    await pool.query(`CREATE TABLE IF NOT EXISTS backup_uploads (
      id uuid PRIMARY KEY, user_id text NOT NULL, bytes bigint NOT NULL,
      expires_at timestamptz NOT NULL DEFAULT now() + interval '1 hour')`);
    await pool.query("CREATE INDEX IF NOT EXISTS backup_uploads_user ON backup_uploads (user_id)");
  }
  async function locked(userId: string, action: (client: any) => Promise<any>) {
    const db = await pool.connect();
    try {
      await db.query("BEGIN");
      await db.query("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", [userId]);
      const result = await action(db);
      await db.query("COMMIT");
      return result;
    } catch (error) { await db.query("ROLLBACK"); throw error; }
    finally { db.release(); }
  }
  async function reserve(userId: string, id: string, bytes: number) {
    return locked(userId, async db => {
      const {rows} = await db.query("SELECT count(*)::int AS count, coalesce(sum(bytes), 0)::text AS bytes FROM backup_uploads WHERE user_id = $1", [userId]);
      if (rows[0].count >= MAX_PENDING || Number(rows[0].bytes) + bytes > MAX_BYTES) return false;
      await db.query("INSERT INTO backup_uploads (id, user_id, bytes) VALUES ($1, $2, $3)", [id, userId, bytes]);
      return true;
    });
  }
  async function pending(userId: string, id: string) {
    const {rows} = await pool.query("SELECT bytes FROM backup_uploads WHERE id = $1 AND user_id = $2 AND expires_at > now()", [id, userId]);
    return rows[0];
  }
  async function register(userId: string, id: string, device: string, bytes: number) {
    const removed = await locked(userId, async db => {
      const {rowCount} = await db.query("DELETE FROM backup_uploads WHERE id = $1 AND user_id = $2 AND bytes = $3 AND expires_at > now() RETURNING id", [id, userId, bytes]);
      if (!rowCount) throw new Error("upload reservation expired");
      await db.query("INSERT INTO backups (id, user_id, device, bytes) VALUES ($1, $2, $3, $4)", [id, userId, device, bytes]);
      return (await db.query(`DELETE FROM backups WHERE user_id = $1 AND id NOT IN
        (SELECT id FROM backups WHERE user_id = $1 ORDER BY created_at DESC, id DESC LIMIT 5) RETURNING id`, [userId])).rows;
    });
    for (const row of removed) await s3.send(new DeleteObjectCommand({Bucket: BUCKET, Key: objectKey(userId, row.id)}));
  }
  async function cleanup() {
    // L'expiration dépasse largement les quinze minutes des adresses signées.
    while (true) {
      const {rows} = await pool.query("SELECT id, user_id FROM backup_uploads WHERE expires_at <= now() LIMIT 100");
      if (!rows.length) break;
      for (const row of rows) {
        await s3.send(new DeleteObjectCommand({Bucket: BUCKET, Key: objectKey(row.user_id, row.id)}));
        await pool.query("DELETE FROM backup_uploads WHERE id = $1 AND expires_at <= now()", [row.id]);
      }
    }
    // Inclut les anciens envois sans réservation et les objets dont l'effacement avait échoué.
    let token: string | undefined;
    do {
      const page = await s3.send(new ListObjectsV2Command({Bucket: BUCKET, ContinuationToken: token}));
      const keys = (page.Contents ?? []).filter((o: any) => o.Key && o.LastModified
        && o.LastModified.getTime() < Date.now() - 3600000).map((o: any) => o.Key);
      if (keys.length) {
        const {rows} = await pool.query(`SELECT user_id || '/' || id || '.solbackup' AS key FROM backups
          WHERE user_id || '/' || id || '.solbackup' = ANY($1::text[])
          UNION ALL SELECT user_id || '/' || id || '.solbackup' AS key FROM backup_uploads
          WHERE user_id || '/' || id || '.solbackup' = ANY($1::text[])`, [keys]);
        const kept = new Set(rows.map((r: any) => r.key));
        for (const key of keys) if (!kept.has(key)) await s3.send(new DeleteObjectCommand({Bucket: BUCKET, Key: key}));
      }
      token = page.IsTruncated ? page.NextContinuationToken : undefined;
      if (page.IsTruncated && !token) throw new Error("storage listing missing continuation token");
    } while (token);
  }
  return {initialize, reserve, pending, register, cleanup};
}
