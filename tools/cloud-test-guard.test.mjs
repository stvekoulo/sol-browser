import assert from 'node:assert/strict';
import {test} from 'node:test';
import {verifyCloudTestEnv} from './cloud-test-guard.mjs';

globalThis.fetch = () => {throw new Error('network forbidden in local tests');};
const branchId = 'br-test-disposable-123', now = Date.now();
const env = {NEON_BRANCH: 'sol-e2e', NEON_PROJECT_ID: 'project-test',
  NEON_AUTH_BASE_URL: 'https://ep-test.neonauth.c-14.us-east-1.aws.neon.tech/neondb/auth',
  NEON_FUNCTION_BACKUP_BASE_URL: `https://${branchId}-backup.compute.c-14.us-east-1.aws.neon.tech`,
  DATABASE_URL: 'postgresql://owner:secret@ep-test-pooler.c-14.us-east-1.aws.neon.tech/neondb'};
function management(overrides = {}) {
  return async path => {
    if (path.endsWith('/auth')) return {base_url: env.NEON_AUTH_BASE_URL, ...overrides.auth};
    if (path.endsWith('/functions')) return {functions: [{slug: 'backup', invocation_url: env.NEON_FUNCTION_BACKUP_BASE_URL, ...overrides.function}]};
    if (path.endsWith('/endpoints')) return {endpoints: [{id: 'ep-test', branch_id: branchId, ...overrides.endpoint}]};
    return {branch: {id: branchId, name: 'sol-e2e', parent_id: 'br-parent', default: false, protected: false,
      expires_at: new Date(now + 86400000).toISOString(), ...overrides.branch}};
  };
}
test('valid disposable identity is verified before allowing application traffic', async () => {
  assert.equal((await verifyCloudTestEnv(env, {read: management(), now})).branchId, branchId);
});
test('missing/ambiguous labels and known production URLs fail before any management call', async () => {
  const read = () => {throw new Error('management must not run');};
  for (const NEON_BRANCH of [undefined, '', 'production', 'Production', 'production ', 'prod', 'main'])
    await assert.rejects(verifyCloudTestEnv({...env, NEON_BRANCH}, {read}), /branch name/);
  for (const key of ['NEON_AUTH_BASE_URL', 'NEON_FUNCTION_BACKUP_BASE_URL', 'DATABASE_URL'])
    await assert.rejects(verifyCloudTestEnv({...env, [key]: ''}, {read}));
  await assert.rejects(verifyCloudTestEnv({...env, NEON_AUTH_BASE_URL: 'https://ep-plain-firefly-b84mlktb.neonauth.c-14.us-east-1.aws.neon.tech/neondb/auth'}, {read}), /production/);
  await assert.rejects(verifyCloudTestEnv({...env, NEON_FUNCTION_BACKUP_BASE_URL: 'https://br-wispy-night-b89mzp5p-backup.compute.c-14.us-east-1.aws.neon.tech'}, {read}), /production/);
});
test('actual default/protected/permanent/expired or misnamed branches fail closed', async () => {
  for (const branch of [{default: true}, {protected: true}, {name: 'production'}, {name: 'different'},
    {expires_at: undefined}, {expires_at: new Date(now - 1).toISOString()},
    {expires_at: new Date(now + 8 * 86400000).toISOString()}, {parent_id: undefined}, {id: 'br-other'}])
    await assert.rejects(verifyCloudTestEnv(env, {read: management({branch}), now}));
});
test('an otherwise valid branch cannot authorize mixed Auth/API/database configurations', async () => {
  for (const changes of [{auth: {base_url: 'https://ep-other.neon.tech/auth'}},
    {function: {invocation_url: 'https://br-other-backup.compute.neon.tech'}}, {endpoint: {branch_id: 'br-other'}}])
    await assert.rejects(verifyCloudTestEnv(env, {read: management(changes), now}));
  await assert.rejects(verifyCloudTestEnv({...env, DATABASE_URL: env.DATABASE_URL.replace('ep-test-pooler', 'ep-production')}, {read: management(), now}), /database belongs/);
  await assert.rejects(verifyCloudTestEnv(env, {read: () => {throw new Error('offline');}}), /offline/);
});
