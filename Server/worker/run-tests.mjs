// Runs the shared scenario tests in ../test against this worker served by `wrangler dev --local`.
// One server per test file (the harness starts it), one file at a time so ports and timing stay calm.
import { spawnSync } from 'node:child_process';
import { readdirSync } from 'node:fs';

const files = readdirSync('../test').filter((f) => f.endsWith('.test.ts') && f !== 'rooms.test.ts').map((f) => `../test/${f}`);
files.push('hibernate.test.ts');
const run = spawnSync(process.execPath, ['--test', '--test-concurrency=1', ...files], {
  stdio: 'inherit', env: { ...process.env, TEST_TARGET: 'worker' },
});
process.exit(run.status ?? 1);
