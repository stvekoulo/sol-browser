// Le CLI authentifié lit uniquement le plan de gestion avant tout compte ou appel applicatif.
import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
import {readFileSync} from 'node:fs';

const PRODUCTION = new Set([
  'ep-plain-firefly-b84mlktb.neonauth.c-14.us-east-1.aws.neon.tech',
  'br-wispy-night-b89mzp5p-backup.compute.c-14.us-east-1.aws.neon.tech',
]);
export function readCloudEnv(path) {
  const env = {};
  for (const line of readFileSync(path, 'utf8').split(/\r?\n/)) {
    if (!line.trim() || line.trimStart().startsWith('#')) continue;
    const match = /^([A-Z_][A-Z0-9_]*)=(.*)$/.exec(line);
    assert(match, 'invalid cloud test env');
    assert(!(match[1] in env), 'duplicate cloud test env key');
    env[match[1]] = match[2].replace(/^"(.*)"$/, '$1');
  }
  return env;
}
function endpoint(value, name) {
  assert(value && value === value.trim(), `missing or ambiguous ${name}`);
  const url = new URL(value);
  assert(url.protocol === 'https:' && !url.username && !url.password && !url.search && !url.hash && !url.port,
    `invalid ${name}`);
  assert(url.hostname.endsWith('.neon.tech') && !PRODUCTION.has(url.hostname), `production or unknown ${name}`);
  return url.href.replace(/\/$/, '');
}
function managementRead(path) {
  assert(/^\/projects\/[a-z0-9/-]+$/.test(path), 'invalid Neon management path');
  const args = ['api', path, '--output', 'json', '--api-host', 'https://console.neon.tech/api/v2'];
  // Un chemin JS permet d'utiliser une installation locale du CLI sans wrapper shell Windows.
  const result = process.env.SOL_NEON_CLI_JS
    ? spawnSync(process.execPath, [process.env.SOL_NEON_CLI_JS, ...args], {encoding: 'utf8', timeout: 30000, windowsHide: true})
    : process.platform === 'win32'
      ? spawnSync('cmd.exe', ['/d', '/s', '/c', `neon ${args.join(' ')}`], {encoding: 'utf8', timeout: 30000, windowsHide: true})
      : spawnSync('neon', args, {encoding: 'utf8', timeout: 30000});
  // Ne jamais joindre stdout/stderr à l'erreur : le CLI peut y inclure des accès.
  assert(!result.error && result.status === 0, 'Neon management verification failed; authenticated Neon CLI required');
  try { return JSON.parse(result.stdout); } catch { throw new Error('invalid Neon management response'); }
}
export async function verifyCloudTestEnv(env, options = {}) {
  const branchName = env.NEON_BRANCH;
  assert(branchName && branchName === branchName.trim() && !/^(production|prod|main|master|default)$/i.test(branchName),
    'missing, production or ambiguous branch name');
  const auth = endpoint(env.NEON_AUTH_BASE_URL, 'Auth endpoint');
  const api = endpoint(env.NEON_FUNCTION_BACKUP_BASE_URL, 'API endpoint');
  const branchId = new URL(api).hostname.match(/^(br-[a-z0-9-]+)-backup\.compute\./)?.[1];
  assert(branchId && new URL(api).pathname === '/', 'invalid backup function endpoint');
  assert(env.DATABASE_URL, 'missing DATABASE_URL');
  const database = new URL(env.DATABASE_URL);
  assert(['postgres:', 'postgresql:'].includes(database.protocol) && database.hostname.endsWith('.neon.tech'), 'invalid DATABASE_URL');
  const projectId = env.NEON_PROJECT_ID ?? JSON.parse(readFileSync(new URL('../.neon', import.meta.url), 'utf8')).projectId;
  assert(/^[a-z0-9-]+$/.test(projectId ?? ''), 'missing Neon project identity');
  const read = options.read ?? managementRead, path = `/projects/${projectId}/branches/${branchId}`;
  const {branch} = await read(path);
  assert(branch && branch.id === branchId && branch.name === branchName && branch.default === false && branch.protected === false
    && branch.parent_id, 'the real branch must be disposable, unprotected and non-default');
  const now = options.now ?? Date.now(), expires = Date.parse(branch.expires_at);
  assert(expires > now && expires <= now + 7 * 86400000, 'disposable branch needs an expiry within seven days');
  const [identity, listed, endpoints] = await Promise.all([
    read(`${path}/auth`), read(`${path}/functions`), read(`${path}/endpoints`),
  ]);
  assert(identity.base_url && endpoint(identity.base_url, 'verified Auth') === auth, 'Auth belongs to another branch');
  assert(listed.functions?.some(f => f.slug === 'backup' && endpoint(f.invocation_url, 'verified API') === api),
    'API belongs to another branch');
  assert(endpoints.endpoints?.some(e => e.branch_id === branchId && (database.hostname.startsWith(`${e.id}.`)
    || database.hostname.startsWith(`${e.id}-pooler.`))), 'database belongs to another branch');
  return {auth, api, branchId};
}
