import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validateCatalog, validateBody, protectedPath, validateEvolution, validateLinks, validateReleaseAuthorization } from './check.mjs';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const spec = '**1.0.0**\n## INV-001 — Origin\n';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
for (const [label, connection, expected] of [
  ['missing fixture', '', /missing fixture is not PASS/],
  ['nonlocal fixture', 'Host=example.invalid;Database=governance_test', /require localhost and a test-marked database/],
  ['unmarked database', 'Host=127.0.0.1;Database=production', /require localhost and a test-marked database/]
]) test('contract runner rejects ' + label + ' before running tests', () => {
  // Inspect the actual exception, not host-dependent colored/wrapped rendering.
  const r = spawnSync('pwsh', ['-NoProfile', '-Command',
    'try { & $env:CROPQC_CONTRACT_SCRIPT -NoBuild } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }'],
    { encoding: 'utf8', env: { ...process.env, CANONICAL_INVENTORY_TEST_POSTGRES: connection,
      CROPQC_CONTRACT_SCRIPT: path.join(root, 'scripts/governance/test-contracts.ps1') } });
  assert.equal(r.status, 1, r.stdout + r.stderr);
  assert.match(r.stdout + r.stderr, expected);
  assert.doesNotMatch(r.stdout + r.stderr, /Mandatory governance contracts PASS|Test run for/);
});
const catalog = () => ({ specificationVersion: '1.0.0', rules: [
  { id: 'INV-001', code: ['src/example.cs'], tests: [], gap: 'Explicit pending runtime coverage.' }] });
test('valid catalog permits an explicit coverage gap, not a fabricated pass', () => validateCatalog(spec, catalog(), () => true));
test('duplicate rules fail', () => assert.throws(() => validateCatalog(spec + spec, catalog(), () => true)));
test('malformed rule IDs cannot hide outside the index', () => assert.throws(() => validateCatalog(spec + '\n## INV-01 — Bad ID', catalog(), () => true)));
test('missing traceability fails', () => assert.throws(() => validateCatalog(spec, { ...catalog(), rules: [] }, () => true)));
test('stale source references fail', () => assert.throws(() => validateCatalog(spec, catalog(), () => false)));
test('unexplained coverage fails', () => { const c = catalog(); c.rules[0].gap = ''; assert.throws(() => validateCatalog(spec, c, () => true)); });
test('blank PR template fails', () => assert.throws(() => validateBody('- Applicable rule IDs:', ['INV-001'], false)));
test('protected paths cover rules, workflows, registry and tests, not ordinary source', () => {
  for (const p of ['AGENTS.md', 'AGENTS.override.md', 'src/AGENTS.md', 'src/AGENTS.override.md', 'docs/governance/traceability.json', '.github/workflows/governance.yml',
    'docs/inventory-architecture/phase3-reviewed-write-candidates.json', 'tests/CropQc.Api.Tests/InventoryCommandTests.cs'])
    assert.equal(protectedPath(p), true);
  assert.equal(protectedPath('src/CropQc.Web/Services/Example.cs'), false);
});
const fields = ['Applicable rule IDs', 'Implementation paths and authoritative records versus projections',
  'Proposed behavior and potential conflicts', 'Authoritative inventory changes', 'Treatment lineage changes',
  'Historical records changed/preserved', 'New origins or writers', 'Exact tests/provider/results proving compliance',
  'Unverified rules, skipped bodies and unresolved assumptions', 'Production-data implications and authorization', 'Business-policy approval required/reference'];
const body = fields.map(f => '- ' + f + ': ' + (f === fields[0] ? 'INV-001' : 'Reviewed; no change.')).join('\n')
  + '\n## Release authorization\nStatus: Not requested\n';
test('unknown rule IDs fail', () => assert.throws(() => validateBody(body.replace('INV-001', 'INV-999'), ['INV-001'], false)));
test('ordinary assessment succeeds', () => validateBody(body, ['INV-001'], false));
test('protected change cannot claim not applicable', () => assert.throws(() => validateBody(body + '\n## Governance change\nNot applicable', ['INV-001'], true)));
test('protected change requires explicit old/new/impact/test/approval disclosure', () => validateBody(body +
  '\n## Governance change\nPrevious: convention\nNew: checks\nImpact: governance\nTests/history: no history writes\nApproval: owner review pending', ['INV-001'], true));
test('rule removal fails even with a decision update', () => assert.throws(() => validateEvolution(spec, '', catalog(), catalog(), true)));
test('changed policy needs a version and decision change', () => {
  assert.throws(() => validateEvolution(spec, spec + 'Changed policy', catalog(), catalog(), true));
  assert.throws(() => validateEvolution(spec, spec + 'Changed policy', catalog(), { ...catalog(), specificationVersion: '1.1.0' }, false));
});
test('reviewable evolution is allowed, without authenticating prose approval', () =>
  validateEvolution(spec, spec + 'Approved clarification', catalog(), { ...catalog(), specificationVersion: '1.1.0' }, true));
test('removing a required contract cannot silently weaken coverage', () => {
  const before = catalog(); before.rules[0].tests = [{ type: 'Tests', member: 'Contract', required: true }];
  assert.throws(() => validateEvolution(spec, spec, before, catalog(), false));
});
test('required architecture suites cannot silently disappear', () => {
  const before = { ...catalog(), structuralSuites: ['RequiredArchitecture'] };
  assert.throws(() => validateEvolution(spec, spec, before, catalog(), false));
});
test('a backward or malformed version cannot masquerade as a policy bump', () => {
  for (const version of ['0.9.9', 'approved'])
    assert.throws(() => validateEvolution(spec, spec + 'Changed policy', catalog(), { ...catalog(), specificationVersion: version }, true));
});
test('broken files and heading anchors fail reference validation', () => {
  const parent = fs.realpathSync(os.tmpdir()); const dir = fs.mkdtempSync(path.join(parent, 'cropqc-links-'));
  try {
    fs.writeFileSync(path.join(dir, 'rules.md'), '# Rules\n## Exact custody\n');
    validateLinks('[ok](rules.md#exact-custody)', dir, dir);
    assert.throws(() => validateLinks('[bad](rules.md#invented)', dir, dir));
    assert.throws(() => validateLinks('[bad](missing.md)', dir, dir));
  } finally {
    assert.equal(path.dirname(fs.realpathSync(dir)), parent);
    fs.rmSync(dir, { recursive: true });
  }
});

const reviewedHead = 'a'.repeat(40);
const ownerRecord = `## Release authorization
Status: Owner authorized
Owner: project owner (fixture)
Reference: retained task instruction: deploy this PR
Date: 2026-10-09
Head: ${reviewedHead}
Scope: Merge and production release
Exclusions: No historical projection repair
`;
test('one owner instruction declares merge and release without another GitHub approval', () => {
  assert.deepEqual(validateReleaseAuthorization(ownerRecord, reviewedHead).declaredActions, ['merge', 'release']);
  validateBody(body.replace('## Release authorization\nStatus: Not requested\n', ownerRecord), ['INV-001'], false, reviewedHead);
});
test('merge-only approval does not declare deployment authority', () => {
  assert.deepEqual(validateReleaseAuthorization(ownerRecord.replace('Merge and production release', 'Merge only'), reviewedHead).declaredActions, ['merge']);
});
test('missing, revoked and unrequested authority never imply a release', () => {
  assert.throws(() => validateReleaseAuthorization('CI passed; owner approval assumed', reviewedHead));
  for (const status of ['Not requested', 'Revoked'])
    assert.deepEqual(validateReleaseAuthorization('## Release authorization\nStatus: ' + status, reviewedHead).declaredActions, []);
});
test('authorization cannot silently follow an unreviewed head', () => {
  assert.throws(() => validateReleaseAuthorization(ownerRecord, 'b'.repeat(40)), /current reviewed PR head/);
  assert.throws(() => validateReleaseAuthorization(ownerRecord), /current reviewed PR head/);
});
test('owner evidence requires a reference, identity, date, scope and exclusions', () => {
  for (const label of ['Owner', 'Reference', 'Date', 'Head', 'Scope', 'Exclusions'])
    assert.throws(() => validateReleaseAuthorization(ownerRecord.replace(new RegExp('^' + label + ':.*$', 'm'), label + ': TODO'), reviewedHead));
  assert.throws(() => validateReleaseAuthorization(ownerRecord.replace('2026-10-09', '2026-02-30'), reviewedHead));
  assert.throws(() => validateReleaseAuthorization(ownerRecord.replace('Merge and production release', 'Bypass failed safeguards'), reviewedHead));
});
test('duplicate or contradictory authorization records are rejected', () => {
  assert.throws(() => validateReleaseAuthorization(ownerRecord + ownerRecord, reviewedHead));
  assert.throws(() => validateReleaseAuthorization(ownerRecord + 'Status: Revoked\n', reviewedHead));
});
test('recorded release authority cannot waive a failed governance check', () => {
  assert.throws(() => validateBody(body.replace('INV-001', 'INV-999').replace('## Release authorization\nStatus: Not requested\n', ownerRecord), ['INV-001'], false, reviewedHead));
  assert.throws(() => validateBody(body.replace('## Release authorization\nStatus: Not requested\n', ownerRecord), ['INV-001'], true, reviewedHead), /Governance change/);
});
