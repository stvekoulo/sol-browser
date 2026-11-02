// Routes anciennes conservées ; les mêmes réservations bornent les deux protocoles d'envoi.
import {DeleteObjectCommand, GetObjectCommand, HeadObjectCommand, PutObjectCommand} from "@aws-sdk/client-s3";
import {parseTriggerDelivery} from "@neon/functions/triggers";
import {Hono} from "hono";
import {backupStorage, BUCKET, MAGIC, MAX_BYTES, objectKey} from "./backup-storage.ts";

export function createBackupApp({pool, s3, verify, sign}: any) {
  const storage = backupStorage(pool, s3);
  const ready = storage.initialize();
  const app = new Hono<{Variables: {userId: string}}>();
  const validId = (id: string) => /^[0-9a-f-]{36}$/.test(id);
  const deviceOf = (value: unknown) => String(value ?? "").replace(/[\p{Cc}]/gu, "").slice(0, 60) || "Sol";
  app.get("/health", c => c.text("ok"));
  app.post("/internal/cleanup", async c => {
    const delivery = await parseTriggerDelivery(c.req.raw);
    if (!delivery.ok || delivery.invocation.type !== "schedule" || delivery.invocation.trigger.name !== "backup-cleanup")
      return c.json({error: "unauthorized"}, 401);
    await ready;
    await storage.cleanup();
    return c.body(null, 204);
  });
  async function authorize(c: any, next: () => Promise<void>) {
    const header = c.req.header("authorization") ?? "";
    if (!header.toLowerCase().startsWith("bearer ")) return c.json({error: "unauthorized"}, 401);
    try {
      const payload = await verify(header.slice(7));
      if (!payload.sub) return c.json({error: "unauthorized"}, 401);
      c.set("userId", payload.sub);
    } catch { return c.json({error: "unauthorized"}, 401); }
    await ready;
    await next();
  }
  app.use("/backups/*", authorize);
  app.use("/backups", authorize);
  app.get("/backups", async c => {
    const {rows} = await pool.query("SELECT id, device, bytes::text AS bytes, created_at FROM backups WHERE user_id = $1 ORDER BY created_at DESC", [c.get("userId")]);
    return c.json({backups: rows.map((r: any) => ({id: r.id, device: r.device, bytes: Number(r.bytes), createdAt: r.created_at}))});
  });
  app.post("/backups", async c => {
    const userId = c.get("userId");
    if (Number(c.req.header("content-length") ?? 0) > MAX_BYTES) return c.json({error: "too-large"}, 413);
    const body = Buffer.from(await c.req.arrayBuffer());
    if (body.length > MAX_BYTES) return c.json({error: "too-large"}, 413);
    if (body.length < MAGIC.length || !body.subarray(0, MAGIC.length).equals(MAGIC)) return c.json({error: "not-a-backup"}, 400);
    const id = crypto.randomUUID();
    if (!await storage.reserve(userId, id, body.length)) return c.json({error: "upload-quota"}, 429);
    await s3.send(new PutObjectCommand({Bucket: BUCKET, Key: objectKey(userId, id), Body: body, ContentType: "application/octet-stream"}));
    await storage.register(userId, id, deviceOf(c.req.query("device")), body.length);
    return c.json({id, bytes: body.length}, 201);
  });
  app.post("/backups/uploads", async c => {
    const {bytes} = await c.req.json().catch(() => ({bytes: 0}));
    if (!Number.isSafeInteger(bytes) || bytes <= MAGIC.length) return c.json({error: "not-a-backup"}, 400);
    if (bytes > MAX_BYTES) return c.json({error: "too-large", max: MAX_BYTES}, 413);
    const id = crypto.randomUUID();
    if (!await storage.reserve(c.get("userId"), id, bytes)) return c.json({error: "upload-quota"}, 429);
    const url = await sign(new PutObjectCommand({Bucket: BUCKET, Key: objectKey(c.get("userId"), id), ContentLength: bytes, ContentType: "application/octet-stream"}));
    return c.json({id, url}, 201);
  });
  app.post("/backups/uploads/:id", async c => {
    const userId = c.get("userId"), id = c.req.param("id");
    if (!validId(id)) return c.json({error: "not-found"}, 404);
    const reservation = await storage.pending(userId, id);
    if (!reservation) {
      const {rows} = await pool.query("SELECT bytes FROM backups WHERE id = $1 AND user_id = $2", [id, userId]);
      return rows[0] ? c.json({id, bytes: Number(rows[0].bytes)}, 201) : c.json({error: "not-found"}, 404);
    }
    const key = objectKey(userId, id);
    const head = await s3.send(new HeadObjectCommand({Bucket: BUCKET, Key: key})).catch(() => null);
    if (!head?.ContentLength) return c.json({error: "not-found"}, 404);
    const start = await s3.send(new GetObjectCommand({Bucket: BUCKET, Key: key, Range: `bytes=0-${MAGIC.length - 1}`}));
    if (head.ContentLength !== Number(reservation.bytes) || head.ContentLength > MAX_BYTES
      || !Buffer.from(await start.Body!.transformToByteArray()).equals(MAGIC)) {
      await s3.send(new DeleteObjectCommand({Bucket: BUCKET, Key: key}));
      return c.json({error: "not-a-backup"}, 400);
    }
    const {device} = await c.req.json().catch(() => ({device: ""}));
    await storage.register(userId, id, deviceOf(device), head.ContentLength);
    return c.json({id, bytes: head.ContentLength}, 201);
  });
  app.get("/backups/:id/url", async c => {
    const userId = c.get("userId"), id = c.req.param("id");
    if (!validId(id)) return c.json({error: "not-found"}, 404);
    const {rowCount} = await pool.query("SELECT 1 FROM backups WHERE id = $1 AND user_id = $2", [id, userId]);
    if (!rowCount) return c.json({error: "not-found"}, 404);
    return c.json({url: await sign(new GetObjectCommand({Bucket: BUCKET, Key: objectKey(userId, id)}))});
  });
  app.get("/backups/:id", async c => {
    const userId = c.get("userId"), id = c.req.param("id");
    if (!validId(id)) return c.json({error: "not-found"}, 404);
    const {rowCount} = await pool.query("SELECT 1 FROM backups WHERE id = $1 AND user_id = $2", [id, userId]);
    if (!rowCount) return c.json({error: "not-found"}, 404);
    const object = await s3.send(new GetObjectCommand({Bucket: BUCKET, Key: objectKey(userId, id)}));
    return new Response(await object.Body!.transformToByteArray(), {headers: {"Content-Type": "application/octet-stream"}});
  });
  app.delete("/backups/:id", async c => {
    const userId = c.get("userId"), id = c.req.param("id");
    if (!validId(id)) return c.json({error: "not-found"}, 404);
    const {rowCount} = await pool.query("DELETE FROM backups WHERE id = $1 AND user_id = $2", [id, userId]);
    if (!rowCount) return c.json({error: "not-found"}, 404);
    await s3.send(new DeleteObjectCommand({Bucket: BUCKET, Key: objectKey(userId, id)}));
    return c.body(null, 204);
  });
  return app;
}
