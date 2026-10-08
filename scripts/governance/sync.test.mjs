import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const shell = process.env.CROPQC_TEST_POWERSHELL || 'pwsh';

test('two computers synchronize through disposable Git without losing local work', async t => {
  const tempParent = fs.realpathSync(os.tmpdir());
  const fixture = fs.mkdtempSync(path.join(tempParent, 'cropqc-sync-'));
  const remote = path.join(fixture, 'github.git');
  const globalConfig = path.join(fixture, 'global.gitconfig');
  const credentialSentinel = 'fixture credentials must stay unchanged';
  fs.writeFileSync(globalConfig, '[credential]\n\thelper =\n');
  fs.writeFileSync(path.join(fixture, 'credentials.sentinel'), credentialSentinel);
  const env = { ...process.env, GIT_CONFIG_GLOBAL: globalConfig, GIT_CONFIG_NOSYSTEM: '1',
    GIT_TERMINAL_PROMPT: '0', GCM_INTERACTIVE: 'Never' };
  // No inherited -c configuration, network rewrite or production settings in this fixture.
  delete env.GIT_CONFIG_COUNT; delete env.GIT_CONFIG_PARAMETERS;
  const git = (cwd, args, expected = 0) => {
    const r = spawnSync('git', ['-C', cwd, ...args], { encoding: 'utf8', env });
    assert.equal(r.status, expected, `${args.join(' ')}: ${r.stderr}`);
    return r.stdout.trim();
  };
  const write = (cwd, p, value) => { fs.mkdirSync(path.dirname(path.join(cwd, p)), { recursive: true }); fs.writeFileSync(path.join(cwd, p), value); };
  const commit = (cwd, message) => {
    git(cwd, ['add', '.']);
    git(cwd, ['-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-m', message]);
    return git(cwd, ['rev-parse', 'HEAD']);
  };
  const clone = name => {
    const destination = path.join(fixture, name);
    git(fixture, ['clone', remote, destination]);
    return destination;
  };
  const sync = (cwd, expected, update = true, offline = true) => {
    const args = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(root, 'scripts/Sync-CropQcKnowledge.ps1'), '-RepositoryPath', cwd];
    if (offline) args.push('-OfflineTestRemote', remote);
    if (update) args.push('-Update');
    const r = spawnSync(shell, args, { encoding: 'utf8', env });
    assert.equal(r.status, expected, `${r.stdout}\n${r.stderr}`);
    return r.stdout;
  };
  try {
    git(fixture, ['init', '--bare', '--initial-branch=main', remote]);
    fs.writeFileSync(path.join(remote, 'cropqc-sync-fixture'), 'offline fixture');
    const a = clone('computer-a');
    for (const p of ['AGENTS.md', 'docs/governance/CROP_QC_BUSINESS_RULES.md', 'docs/governance/ARCHITECTURAL_DECISIONS.md'])
      write(a, p, fs.readFileSync(path.join(root, p), 'utf8'));
    write(a, 'conflict.txt', 'original\n');
    commit(a, 'Initial approved fixture knowledge'); git(a, ['push', 'origin', 'main']);
    const b = clone('computer-b');
    await t.test('fresh clone receives actual entrypoint and referenced canonical files', () => {
      assert.equal(fs.readFileSync(path.join(b, 'AGENTS.md'), 'utf8'), fs.readFileSync(path.join(root, 'AGENTS.md'), 'utf8'));
      for (const p of ['CROP_QC_BUSINESS_RULES.md', 'ARCHITECTURAL_DECISIONS.md'])
        assert.ok(fs.readFileSync(path.join(b, 'AGENTS.md'), 'utf8').includes(`docs/governance/${p}`));
      assert.match(sync(b, 0), /SUCCESS/);
    });
    await t.test('report-only leaves clean main behind; approved rule then reaches second computer', () => {
      const p = 'docs/governance/CROP_QC_BUSINESS_RULES.md';
      write(a, p, fs.readFileSync(path.join(a, p), 'utf8').replace('**1.0.0**', '**1.0.1**')
        + '\n## FIXTURE-001 — Simulated approved rule\nEvery fixture event retains its actor.\n');
      const next = commit(a, 'Simulated human-approved rule revision'); git(a, ['push', 'origin', 'main']);
      const old = git(b, ['rev-parse', 'HEAD']);
      assert.match(sync(b, 2, false), /clean main can fast-forward/);
      assert.equal(git(b, ['rev-parse', 'HEAD']), old);
      assert.match(sync(b, 0), /1\.0\.1/);
      assert.equal(git(b, ['rev-parse', 'HEAD']), next);
      assert.match(fs.readFileSync(path.join(b, p), 'utf8'), /\*\*1\.0\.1\*\*/);
      assert.match(fs.readFileSync(path.join(b, p), 'utf8'), /Every fixture event retains its actor/);
    });
    await t.test('repeated synchronization is idempotent', () => {
      const before = git(b, ['rev-parse', 'HEAD']); sync(b, 0); sync(b, 0);
      assert.equal(git(b, ['rev-parse', 'HEAD']), before); assert.equal(git(b, ['status', '--porcelain']), '');
    });
    await t.test('dirty instruction, staged and untracked files remain byte-for-byte intact', () => {
      const c = clone('dirty');
      write(c, 'AGENTS.md', 'Local instructions\n'); write(c, 'draft.txt', 'staged\n'); git(c, ['add', 'draft.txt']);
      write(c, 'untracked.txt', 'unsaved work\n');
      write(a, 'approved.txt', 'remote update\n'); commit(a, 'Incoming fixture update'); git(a, ['push', 'origin', 'main']);
      const before = git(c, ['status', '--porcelain']); const head = git(c, ['rev-parse', 'HEAD']);
      assert.match(sync(c, 2), /Locally edited/);
      assert.equal(git(c, ['status', '--porcelain']), before); assert.equal(git(c, ['rev-parse', 'HEAD']), head);
      assert.equal(fs.readFileSync(path.join(c, 'AGENTS.md'), 'utf8'), 'Local instructions\n');
      assert.equal(fs.readFileSync(path.join(c, 'untracked.txt'), 'utf8'), 'unsaved work\n');
    });
    await t.test('diverged main is never overwritten', () => {
      const c = clone('diverged'); write(c, 'local.txt', 'local\n'); const head = commit(c, 'Local main work');
      write(a, 'remote.txt', 'remote\n'); commit(a, 'Remote main work'); git(a, ['push', 'origin', 'main']);
      assert.match(sync(c, 2), /histories diverged/); assert.equal(git(c, ['rev-parse', 'HEAD']), head);
    });
    await t.test('feature branches remain unchanged even with Update', () => {
      const c = clone('feature'); git(c, ['switch', '-c', 'codex/feature']);
      const head = git(c, ['rev-parse', 'HEAD']);
      write(a, 'docs/new-rule-detail.md', 'Approved fixture clarification\n'); commit(a, 'More incoming knowledge'); git(a, ['push', 'origin', 'main']);
      assert.match(sync(c, 2), /feature branch preserved/); assert.equal(git(c, ['rev-parse', 'HEAD']), head);
      assert.equal(git(c, ['branch', '--show-current']), 'codex/feature');
    });
    await t.test('feature with matching knowledge may report success without moving HEAD', () => {
      const c = clone('feature-current'); git(c, ['switch', '-c', 'codex/current']);
      write(c, 'feature-code.txt', 'local code\n'); const head = commit(c, 'Unpublished code only');
      sync(c, 0); assert.equal(git(c, ['rev-parse', 'HEAD']), head);
    });
    await t.test('locally committed feature instructions remain proposals', () => {
      const c = clone('feature-rules'); git(c, ['switch', '-c', 'codex/proposed-rules']);
      write(c, 'AGENTS.md', 'Proposed local instructions\n'); const head = commit(c, 'Unapproved local proposal');
      assert.match(sync(c, 2), /feature branch preserved/); assert.equal(git(c, ['rev-parse', 'HEAD']), head);
      assert.equal(fs.readFileSync(path.join(c, 'AGENTS.md'), 'utf8'), 'Proposed local instructions\n');
    });
    await t.test('unresolved merge conflicts stop and preserve index and conflict markers', () => {
      const c = clone('conflict'); git(c, ['switch', '-c', 'codex/conflict']);
      write(c, 'conflict.txt', 'local choice\n'); commit(c, 'Local conflict');
      write(a, 'conflict.txt', 'remote choice\n'); commit(a, 'Remote conflict'); git(a, ['push', 'origin', 'main']);
      git(c, ['fetch', 'origin']); git(c, ['-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'merge', 'origin/main'], 1);
      const contents = fs.readFileSync(path.join(c, 'conflict.txt'), 'utf8'); const index = git(c, ['ls-files', '-u']);
      assert.match(sync(c, 2), /unfinished Git operation/);
      assert.equal(fs.readFileSync(path.join(c, 'conflict.txt'), 'utf8'), contents); assert.equal(git(c, ['ls-files', '-u']), index);
    });
    await t.test('local-only main and detached HEAD require manual action', () => {
      const c = clone('ahead'); write(c, 'local.txt', 'ahead\n'); const head = commit(c, 'Local-only main');
      assert.match(sync(c, 2), /local-only commits/); assert.equal(git(c, ['rev-parse', 'HEAD']), head);
      git(c, ['checkout', '--detach']); assert.match(sync(c, 2), /detached HEAD/);
    });
    await t.test('explicit main fetch works with a feature-only configured refspec', () => {
      const c = clone('narrow'); git(c, ['config', 'remote.origin.fetch', '+refs/heads/absent:refs/remotes/origin/absent']);
      sync(c, 0);
    });
    await t.test('merge hooks cannot run during synchronization', () => {
      const c = clone('hooks'); const marker = path.join(c, 'hook-ran');
      write(c, '.git/hooks/post-merge', '#!/bin/sh\ntouch hook-ran\n'); fs.chmodSync(path.join(c, '.git/hooks/post-merge'), 0o755);
      write(a, 'new.txt', 'new\n'); commit(a, 'Fast-forward for hook safety'); git(a, ['push', 'origin', 'main']);
      sync(c, 0); assert.equal(fs.existsSync(marker), false);
    });
    await t.test('wrong/credential-bearing remote is rejected without exposing it', () => {
      const c = clone('wrong'); git(c, ['remote', 'set-url', 'origin', 'https://secret-fixture@example.invalid/wrong.git']);
      const r = spawnSync(shell, ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(root, 'scripts/Sync-CropQcKnowledge.ps1'), '-RepositoryPath', c], { env, encoding: 'utf8' });
      assert.equal(r.status, 1); assert.doesNotMatch(r.stdout + r.stderr, /secret-fixture/);
    });
    await t.test('failed fetch never reports success or changes HEAD', () => {
      const c = clone('fetch-failure'); const head = git(c, ['rev-parse', 'HEAD']);
      // Valid marked bare repo with its branch temporarily absent: refspec fetch fails.
      git(remote, ['update-ref', '-d', 'refs/heads/main']);
      assert.doesNotMatch(sync(c, 1), /SUCCESS/); assert.equal(git(c, ['rev-parse', 'HEAD']), head);
      git(a, ['push', 'origin', 'main']);
    });
    await t.test('credentials/global config untouched; only local fixture remotes used', () => {
      assert.equal(fs.readFileSync(globalConfig, 'utf8'), '[credential]\n\thelper =\n');
      assert.equal(fs.readFileSync(path.join(fixture, 'credentials.sentinel'), 'utf8'), credentialSentinel);
      assert.equal(git(a, ['remote', 'get-url', 'origin']), remote);
    });
  } finally {
    const resolved = fs.realpathSync(fixture);
    assert.equal(path.dirname(resolved), tempParent);
    assert.ok(path.basename(resolved).startsWith('cropqc-sync-'));
    fs.rmSync(resolved, { recursive: true, force: true });
  }
});
