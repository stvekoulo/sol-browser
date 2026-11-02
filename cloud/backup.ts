// Seuls des fichiers scellés sur l'appareil passent par cette API ; la clé reste dans Sol.
import {S3Client} from "@aws-sdk/client-s3";
import {getSignedUrl} from "@aws-sdk/s3-request-presigner";
import {attachDatabasePool} from "@neon/functions";
import {createRemoteJWKSet, jwtVerify} from "jose";
import {Pool} from "pg";
import {createBackupApp} from "./backup-api.ts";

const pool = new Pool({connectionString: process.env.DATABASE_URL, max: 5});
attachDatabasePool(pool);
const s3 = new S3Client({forcePathStyle: true});
const jwks = createRemoteJWKSet(new URL(process.env.NEON_AUTH_JWKS_URL!));
const issuer = new URL(process.env.NEON_AUTH_BASE_URL!).origin;
export default createBackupApp({
  pool, s3,
  verify: async (token: string) => (await jwtVerify(token, jwks, {issuer})).payload,
  sign: (command: any) => getSignedUrl(s3, command, {expiresIn: 900}),
});
