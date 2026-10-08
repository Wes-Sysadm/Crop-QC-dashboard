import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

export const ruleIds = text => [...text.matchAll(/^## ([A-Z]+-\d{3}) — /gm)].map(m => m[1]);
const fail = message => { throw new Error(message); };
export function validateCatalog(spec, matrix, exists) {
  const ids = ruleIds(spec);
  if (!ids.length || new Set(ids).size !== ids.length) fail('Rule IDs must exist and be unique.');
  const mapped = matrix.rules.map(r => r.id);
  if (new Set(mapped).size !== mapped.length || JSON.stringify([...mapped].sort()) !== JSON.stringify([...ids].sort()))
    fail('Every rule needs exactly one traceability entry.');
  if (!spec.includes('**' + matrix.specificationVersion + '**')) fail('Specification version mismatch.');
  for (const rule of matrix.rules) {
    if (!rule.code.length || !rule.gap?.trim()) fail(rule.id + ': code and coverage limitation are required.');
    for (const p of rule.code) if (!exists(p)) fail(rule.id + ': missing source ' + p);
    for (const test of rule.tests) {
      if (!/^CropQc\.Api\.Tests\.[A-Za-z0-9_]+$/.test(test.type) || !/^[A-Za-z0-9_]+$/.test(test.member)
        || !['unit', 'workflow', 'postgres', 'structural'].includes(test.kind) || typeof test.required !== 'boolean')
        fail(rule.id + ': invalid test metadata.');
      if (!exists('tests/CropQc.Api.Tests/' + test.type.split('.').at(-1) + '.cs')) fail(rule.id + ': missing test file.');
    }
  }
}

export function validateBody(body, ids, protectedChange) {
  const fields = ['Applicable rule IDs', 'Implementation paths and authoritative records versus projections',
    'Proposed behavior and potential conflicts', 'Authoritative inventory changes', 'Treatment lineage changes',
    'Historical records changed/preserved', 'New origins or writers', 'Exact tests/provider/results proving compliance',
    'Unverified rules, skipped bodies and unresolved assumptions', 'Production-data implications and authorization',
    'Business-policy approval required/reference'];
  for (const field of fields) {
    const line = body.split(/\r?\n/).find(l => l.startsWith('- ' + field));
    if (!line || !line.includes(':') || !line.slice(line.indexOf(':') + 1).trim()
      || /^(TBD|TODO|<.*>)$/i.test(line.slice(line.indexOf(':') + 1).trim()))
      fail('Complete PR assessment: ' + field);
  }
  const applicable = body.split(/\r?\n/).find(l => l.startsWith('- Applicable rule IDs:'));
  const supplied = applicable.match(/\b[A-Z]+-\d{3}\b/g) || [];
  if (!supplied.length || supplied.some(id => !ids.includes(id))) fail('PR must cite known rule IDs.');
  if (protectedChange) {
    const section = body.match(/## Governance change\s*([\s\S]*?)(?=\n## |$)/)?.[1]?.trim();
    if (!section || /^(Not applicable|N\/?A)[.!]?$/i.test(section))
      fail('Protected governance/test changes require a Governance change disclosure.');
    for (const label of ['Previous:', 'New:', 'Impact:', 'Tests/history:', 'Approval:'])
      if (!section.split(/\r?\n/).some(l => l.startsWith(label) && l.slice(label.length).trim()))
        fail('Governance change must include ' + label);
  }
}

export function protectedPath(p) {
  return p === 'AGENTS.md' || p.startsWith('docs/governance/') || p.startsWith('docs/inventory-architecture/')
    || ['docs/change-scoped-testing-standard.md', 'docs/overnight-release-standard.md'].includes(p)
    || p.startsWith('.github/') || p.startsWith('scripts/governance/') || p.startsWith('tests/CropQc.Api.Tests/');
}

export function check(root, event) {
  const read = p => fs.readFileSync(path.join(root, p), 'utf8');
  const exists = p => !path.isAbsolute(p) && !p.split('/').includes('..') && fs.existsSync(path.join(root, p));
  const spec = read('docs/governance/CROP_QC_BUSINESS_RULES.md');
  const matrix = JSON.parse(read('docs/governance/traceability.json'));
  validateCatalog(spec, matrix, exists);
  if (!read('AGENTS.md').includes('docs/governance/CROP_QC_BUSINESS_RULES.md')) fail('Root instructions must reference the catalog.');
  for (const name of fs.readdirSync(path.join(root, 'docs/governance')).filter(p => p.endsWith('.md'))) {
    for (const match of read('docs/governance/' + name).matchAll(/\[[^\]]*\]\(([^)]+)\)/g)) {
      const target = match[1].split('#')[0];
      if (!target || /^[a-z]+:/i.test(target)) continue;
      const resolved = path.resolve(root, 'docs/governance', target);
      if (!resolved.startsWith(path.resolve(root) + path.sep) || !fs.existsSync(resolved)) fail(name + ': missing local reference ' + target);
    }
  }
  const decisions = read('docs/governance/DECISIONS.md');
  const decisionIds = [...decisions.matchAll(/^\| (ADR-\d{3}) \|/gm)].map(m => m[1]);
  if (!decisionIds.length || new Set(decisionIds).size !== decisionIds.length) fail('Decision IDs must be unique.');
  for (const id of decisions.match(/\b[A-Z]+-\d{3}\b/g) || [])
    if (!id.startsWith('ADR-') && !ruleIds(spec).includes(id)) fail('Unknown decision rule ' + id);
  for (const p of ['.github/CODEOWNERS', '.github/pull_request_template.md',
    '.github/workflows/governance.yml', 'tests/CropQc.Api.Tests/CanonicalInventoryArchitectureTests.cs',
    'docs/inventory-architecture/phase3-workflow-registry.json', 'docs/inventory-architecture/phase3-reviewed-write-candidates.json'])
    if (!exists(p)) fail('Missing governance dependency ' + p);
  if (event?.pull_request) {
    const base = event.pull_request.base.sha;
    if (!/^[a-f0-9]{40}$/.test(base)) fail('Invalid PR base.');
    const changed = execFileSync('git', ['diff', '--name-only', base, 'HEAD'], { cwd: root, encoding: 'utf8' }).trim().split(/\r?\n/);
    validateBody(event.pull_request.body || '', ruleIds(spec), changed.some(protectedPath));
  }
  return matrix;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
  const event = process.env.GITHUB_EVENT_PATH ? JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8')) : undefined;
  const matrix = check(root, event);
  console.log('Governance metadata PASS: ' + matrix.rules.length + ' rules. Runtime proof and human approval are separate gates.');
}
