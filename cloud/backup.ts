// API de sauvegarde de Sol. Elle ne reçoit que des fichiers déjà chiffrés sur l'appareil : ni la clé de sauvegarde,
// ni aucune donnée lisible ne passent jamais par ici. Chaque route est limitée au compte du jeton vérifié.
import { DeleteObjectCommand, GetObjectCommand, HeadObjectCommand, ListObjectsV2Command, PutObjectCommand, S3Client } from "@aws-sdk/client-s3";
import { getSignedUrl } from "@aws-sdk/s3-request-presigner";
import { attachDatabasePool } from "@neon/functions";
import { Hono } from "hono";
import { createRemoteJWKSet, jwtVerify } from "jose";
import { Pool } from "pg";

const BUCKET = "backups";
const MAX_BYTES = 256 * 1024 * 1024;
const KEEP_PER_USER = 5;
const MAGIC = Buffer.from("SOLBACKUP1");

const pool = new Pool({ connectionString: process.env.DATABASE_URL, max: 5 });
attachDatabasePool(pool);
const s3 = new S3Client({ forcePathStyle: true });
const jwks = createRemoteJWKSet(new URL(process.env.NEON_AUTH_JWKS_URL!));
const issuer = new URL(process.env.NEON_AUTH_BASE_URL!).origin;

// ponytail: schéma créé au premier appel, un outil de migration le jour où il y a plus d'une table.
const ready = pool.query(`CREATE TABLE IF NOT EXISTS backups (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id text NOT NULL,
  device text NOT NULL,
  bytes bigint NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now())`)
  .then(() => pool.query("CREATE INDEX IF NOT EXISTS backups_user ON backups (user_id, created_at DESC)"));

const objectKey = (userId: string, id: string) => `${userId}/${id}.solbackup`;

const app = new Hono<{ Variables: { userId: string } }>();

app.get("/health", c => c.text("ok"));

app.use("/backups/*", authorize);
app.use("/backups", authorize);
async function authorize(c: any, next: () => Promise<void>) {
  const header = c.req.header("authorization") ?? "";
  if (!header.toLowerCase().startsWith("bearer ")) return c.json({ error: "unauthorized" }, 401);
  try {
    const { payload } = await jwtVerify(header.slice(7), jwks, { issuer });
    if (!payload.sub) return c.json({ error: "unauthorized" }, 401);
    c.set("userId", payload.sub);
  } catch { return c.json({ error: "unauthorized" }, 401); }
  await ready;
  await next();
}

app.get("/backups", async c => {
  const { rows } = await pool.query(
    "SELECT id, device, bytes::text AS bytes, created_at FROM backups WHERE user_id = $1 ORDER BY created_at DESC",
    [c.get("userId")]);
  return c.json({ backups: rows.map(r => ({ id: r.id, device: r.device, bytes: Number(r.bytes), createdAt: r.created_at })) });
});

app.post("/backups", async c => {
  const userId = c.get("userId");
  const declared = Number(c.req.header("content-length") ?? 0);
  if (declared > MAX_BYTES) return c.json({ error: "too-large" }, 413);
  const body = Buffer.from(await c.req.arrayBuffer());
  if (body.length > MAX_BYTES) return c.json({ error: "too-large" }, 413);
  // Refuse tout ce qui n'est pas une sauvegarde chiffrée de Sol : le service ne sert pas d'espace de stockage libre.
  if (body.length < MAGIC.length || !body.subarray(0, MAGIC.length).equals(MAGIC)) return c.json({ error: "not-a-backup" }, 400);
  const id = crypto.randomUUID();
  await s3.send(new PutObjectCommand({ Bucket: BUCKET, Key: objectKey(userId, id), Body: body, ContentType: "application/octet-stream" }));
  await register(userId, id, deviceOf(c.req.query("device")), body.length);
  return c.json({ id, bytes: body.length }, 201);
});

// La passerelle des Functions refuse les corps de plus de ~10 Mo : une sauvegarde passe donc directement de l'appareil au
// stockage, par une adresse signée valable 15 minutes pour ce seul fichier et cette seule taille, puis elle est validée ici.
app.post("/backups/uploads", async c => {
  const { bytes } = await c.req.json().catch(() => ({ bytes: 0 }));
  if (!Number.isSafeInteger(bytes) || bytes <= MAGIC.length) return c.json({ error: "not-a-backup" }, 400);
  if (bytes > MAX_BYTES) return c.json({ error: "too-large", max: MAX_BYTES }, 413);
  const id = crypto.randomUUID();
  const url = await getSignedUrl(s3, new PutObjectCommand({ Bucket: BUCKET, Key: objectKey(c.get("userId"), id), ContentLength: bytes, ContentType: "application/octet-stream" }), { expiresIn: 900 });
  return c.json({ id, url }, 201);
});

app.post("/backups/uploads/:id", async c => {
  const userId = c.get("userId"), id = c.req.param("id");
  if (!/^[0-9a-f-]{36}$/.test(id)) return c.json({ error: "not-found" }, 404);
  const key = objectKey(userId, id);
  const head = await s3.send(new HeadObjectCommand({ Bucket: BUCKET, Key: key })).catch(() => null);
  if (!head?.ContentLength) return c.json({ error: "not-found" }, 404);
  const start = await s3.send(new GetObjectCommand({ Bucket: BUCKET, Key: key, Range: `bytes=0-${MAGIC.length - 1}` }));
  // Refuse tout ce qui n'est pas une sauvegarde chiffrée de Sol, même arrivé par l'adresse signée.
  if (head.ContentLength > MAX_BYTES || !Buffer.from(await start.Body!.transformToByteArray()).equals(MAGIC)) {
    await s3.send(new DeleteObjectCommand({ Bucket: BUCKET, Key: key }));
    return c.json({ error: "not-a-backup" }, 400);
  }
  const { device } = await c.req.json().catch(() => ({ device: "" }));
  const { rowCount } = await pool.query("SELECT 1 FROM backups WHERE id = $1", [id]);
  if (!rowCount) await register(userId, id, deviceOf(device), head.ContentLength);
  return c.json({ id, bytes: head.ContentLength }, 201);
});

app.get("/backups/:id/url", async c => {
  const userId = c.get("userId"), id = c.req.param("id");
  if (!/^[0-9a-f-]{36}$/.test(id)) return c.json({ error: "not-found" }, 404);
  const { rowCount } = await pool.query("SELECT 1 FROM backups WHERE id = $1 AND user_id = $2", [id, userId]);
  if (!rowCount) return c.json({ error: "not-found" }, 404);
  return c.json({ url: await getSignedUrl(s3, new GetObjectCommand({ Bucket: BUCKET, Key: objectKey(userId, id) }), { expiresIn: 900 }) });
});

const deviceOf = (value: unknown) => String(value ?? "").replace(/[\p{Cc}]/gu, "").slice(0, 60) || "Sol";

async function register(userId: string, id: string, device: string, bytes: number) {
  await pool.query("INSERT INTO backups (id, user_id, device, bytes) VALUES ($1, $2, $3, $4)", [id, userId, device, bytes]);
  // Les plus anciennes sortent pour borner l'espace utilisé par compte, avec les envois jamais validés depuis une heure
  // (un envoi en cours sur un autre appareil n'est pas touché).
  await pool.query(
    "DELETE FROM backups WHERE user_id = $1 AND id NOT IN (SELECT id FROM backups WHERE user_id = $1 ORDER BY created_at DESC LIMIT $2)",
    [userId, KEEP_PER_USER]);
  const { rows } = await pool.query("SELECT id FROM backups WHERE user_id = $1", [userId]);
  const kept = new Set(rows.map(r => objectKey(userId, r.id)));
  const listed = await s3.send(new ListObjectsV2Command({ Bucket: BUCKET, Prefix: `${userId}/` }));
  for (const object of listed.Contents ?? [])
    if (object.Key && !kept.has(object.Key) && (object.LastModified?.getTime() ?? 0) < Date.now() - 3_600_000) await s3.send(new DeleteObjectCommand({ Bucket: BUCKET, Key: object.Key }));
}

app.get("/backups/:id", async c => {
  const userId = c.get("userId"), id = c.req.param("id");
  if (!/^[0-9a-f-]{36}$/.test(id)) return c.json({ error: "not-found" }, 404);
  const { rowCount } = await pool.query("SELECT 1 FROM backups WHERE id = $1 AND user_id = $2", [id, userId]);
  if (!rowCount) return c.json({ error: "not-found" }, 404);
  const object = await s3.send(new GetObjectCommand({ Bucket: BUCKET, Key: objectKey(userId, id) }));
  return new Response(await object.Body!.transformToByteArray(), { headers: { "Content-Type": "application/octet-stream" } });
});

app.delete("/backups/:id", async c => {
  const userId = c.get("userId"), id = c.req.param("id");
  if (!/^[0-9a-f-]{36}$/.test(id)) return c.json({ error: "not-found" }, 404);
  const { rowCount } = await pool.query("DELETE FROM backups WHERE id = $1 AND user_id = $2", [id, userId]);
  if (!rowCount) return c.json({ error: "not-found" }, 404);
  await s3.send(new DeleteObjectCommand({ Bucket: BUCKET, Key: objectKey(userId, id) }));
  return c.body(null, 204);
});

export default app;
