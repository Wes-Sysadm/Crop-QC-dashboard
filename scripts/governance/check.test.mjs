import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validateCatalog, validateBody, protectedPath } from './check.mjs';
const spec = '**1.0.0**\n## INV-001 — Origin\n';
const catalog = () => ({ specificationVersion: '1.0.0', rules: [
  { id: 'INV-001', code: ['src/example.cs'], tests: [], gap: 'Explicit pending runtime coverage.' }] });
test('valid catalog permits an explicit coverage gap, not a fabricated pass', () => validateCatalog(spec, catalog(), () => true));
test('duplicate rules fail', () => assert.throws(() => validateCatalog(spec + spec, catalog(), () => true)));
test('missing traceability fails', () => assert.throws(() => validateCatalog(spec, { ...catalog(), rules: [] }, () => true)));
test('stale source references fail', () => assert.throws(() => validateCatalog(spec, catalog(), () => false)));
test('unexplained coverage fails', () => { const c = catalog(); c.rules[0].gap = ''; assert.throws(() => validateCatalog(spec, c, () => true)); });
test('blank PR template fails', () => assert.throws(() => validateBody('- Applicable rule IDs:', ['INV-001'], false)));
test('protected paths cover rules, workflows, registry and tests, not ordinary source', () => {
  for (const p of ['AGENTS.md', 'docs/governance/traceability.json', '.github/workflows/governance.yml',
    'docs/inventory-architecture/phase3-reviewed-write-candidates.json', 'tests/CropQc.Api.Tests/InventoryCommandTests.cs'])
    assert.equal(protectedPath(p), true);
  assert.equal(protectedPath('src/CropQc.Web/Services/Example.cs'), false);
});
const fields = ['Applicable rule IDs', 'Implementation paths and authoritative records versus projections',
  'Proposed behavior and potential conflicts', 'Authoritative inventory changes', 'Treatment lineage changes',
  'Historical records changed/preserved', 'New origins or writers', 'Exact tests/provider/results proving compliance',
  'Unverified rules, skipped bodies and unresolved assumptions', 'Production-data implications and authorization', 'Business-policy approval required/reference'];
const body = fields.map(f => '- ' + f + ': ' + (f === fields[0] ? 'INV-001' : 'Reviewed; no change.')).join('\n');
test('unknown rule IDs fail', () => assert.throws(() => validateBody(body.replace('INV-001', 'INV-999'), ['INV-001'], false)));
test('ordinary assessment succeeds', () => validateBody(body, ['INV-001'], false));
test('protected change cannot claim not applicable', () => assert.throws(() => validateBody(body + '\n## Governance change\nNot applicable', ['INV-001'], true)));
test('protected change requires explicit old/new/impact/test/approval disclosure', () => validateBody(body +
  '\n## Governance change\nPrevious: convention\nNew: checks\nImpact: governance\nTests/history: no history writes\nApproval: owner review pending', ['INV-001'], true));
