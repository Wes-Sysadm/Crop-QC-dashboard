import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

export const ruleIds = text => [...text.matchAll(/^## ([A-Z]+-\d{3}) — /gm)].map(m => m[1]);
const fail = message => { throw new Error(message); };
export function validateCatalog(spec, matrix, exists) {
  const ids = ruleIds(spec);
  if (!ids.length || new Set(ids).size !== ids.length || (spec.match(/^## /gm) || []).length !== ids.length)
    fail('Rule IDs must exist, use PREFIX-000 format and be unique.');
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

// Review metadata only. The operator must verify the original owner instruction;
// PR prose and green CI cannot authenticate a human or authorize execution.
export function validateReleaseAuthorization(body, head) {
  const sections = [...body.matchAll(/^## Release authorization\s*\r?\n([\s\S]*?)(?=^## |$(?![\s\S]))/gm)];
  if (sections.length !== 1) fail('Exactly one Release authorization section is required.');
  const section = sections[0][1];
  const field = label => {
    const lines = section.split(/\r?\n/).filter(l => l.startsWith(label + ':'));
    const value = lines[0]?.slice(label.length + 1).trim();
    if (lines.length !== 1 || !value || /^(TBD|TODO|<.*>)$/i.test(value))
      fail('Complete release authorization: ' + label);
    return value;
  };
  const status = field('Status');
  if (['Not requested', 'Revoked'].includes(status)) return { status, declaredActions: [] };
  if (status !== 'Owner authorized') fail('Unknown release authorization status.');
  for (const label of ['Owner', 'Reference', 'Exclusions']) field(label);
  const date = field('Date');
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || !Number.isFinite(Date.parse(date))
      || new Date(date).toISOString().slice(0, 10) !== date) fail('Use a valid authorization date (YYYY-MM-DD).');
  const approvedHead = field('Head');
  if (!/^[a-f0-9]{40}$/.test(approvedHead) || !head || approvedHead !== head)
    fail('Release authorization must identify the current reviewed PR head.');
  const scope = field('Scope');
  if (!['Merge only', 'Merge and production release'].includes(scope)) fail('Unknown authorization scope.');
  return { status, declaredActions: scope === 'Merge only' ? ['merge'] : ['merge', 'release'] };
}

export function validateBody(body, ids, protectedChange, head) {
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
  validateReleaseAuthorization(body, head);
}

export function protectedPath(p) {
  return /(^|\/)AGENTS(?:\.override)?\.md$/.test(p) || p.startsWith('docs/governance/') || p.startsWith('docs/inventory-architecture/')
    || ['docs/change-scoped-testing-standard.md', 'docs/overnight-release-standard.md'].includes(p)
    || p.startsWith('.github/') || p.startsWith('scripts/governance/') || p === 'scripts/Sync-CropQcKnowledge.ps1'
    || p.startsWith('tests/CropQc.Api.Tests/');
}

export function validateEvolution(before, after, oldMatrix, matrix, decisionsChanged) {
  if (ruleIds(before).some(id => !ruleIds(after).includes(id))) fail('Stable rule IDs cannot be removed; record supersession instead.');
  const required = m => m.rules.flatMap(r => r.tests.filter(t => t.required).map(t => `${r.id}:${t.type}.${t.member}`));
  const removed = required(oldMatrix).some(t => !required(matrix).includes(t))
    || (oldMatrix.structuralSuites || []).some(s => !(matrix.structuralSuites || []).includes(s));
  const policyChanged = before.replace(/Specification version:.*$/m, '') !== after.replace(/Specification version:.*$/m, '');
  const parts = v => /^\d+\.\d+\.\d+$/.test(v) ? v.split('.').map(Number) : fail('Use a numeric major.minor.patch specification version.');
  const oldVersion = parts(oldMatrix.specificationVersion), version = parts(matrix.specificationVersion);
  const changedPart = version.findIndex((n, i) => n !== oldVersion[i]);
  if (changedPart >= 0 && version[changedPart] < oldVersion[changedPart]) fail('Specification versions cannot move backward.');
  if ((removed || policyChanged) && (changedPart < 0 || !decisionsChanged))
    fail('Policy changes or required-contract removals need a version bump and decision update for human review.');
}

export function validateLinks(text, directory, root) {
  for (const match of text.matchAll(/\[[^\]]*\]\(([^)]+)\)/g)) {
    const [target, fragment] = match[1].split('#');
    if (/^[a-z]+:/i.test(target)) continue;
    const resolved = target ? path.resolve(directory, target) : undefined;
    if (resolved && (!resolved.startsWith(path.resolve(root) + path.sep) || !fs.existsSync(resolved))) fail('Missing local reference ' + target);
    if (fragment && (!target || target.endsWith('.md'))) {
      const headings = [...(resolved ? fs.readFileSync(resolved, 'utf8') : text).matchAll(/^#{1,6}\s+(.+)$/gm)]
        .map(m => m[1].toLowerCase().replace(/[^\p{L}\p{N}_\-\s]/gu, '').replace(/\s/g, '-'));
      if (!headings.includes(decodeURIComponent(fragment))) fail('Missing heading reference ' + match[1]);
    }
  }
}

export function check(root, event) {
  const read = p => fs.readFileSync(path.join(root, p), 'utf8');
  const exists = p => !path.isAbsolute(p) && !p.split('/').includes('..') && fs.existsSync(path.join(root, p));
  const spec = read('docs/governance/CROP_QC_BUSINESS_RULES.md');
  const matrix = JSON.parse(read('docs/governance/traceability.json'));
  validateCatalog(spec, matrix, exists);
  if (!read('AGENTS.md').includes('docs/governance/CROP_QC_BUSINESS_RULES.md')) fail('Root instructions must reference the catalog.');
  validateLinks(read('AGENTS.md'), root, root);
  for (const name of fs.readdirSync(path.join(root, 'docs/governance')).filter(p => p.endsWith('.md')))
    validateLinks(read('docs/governance/' + name), path.join(root, 'docs/governance'), root);
  const decisions = read('docs/governance/ARCHITECTURAL_DECISIONS.md');
  const decisionIds = [...decisions.split('## Supporting evidence')[0].matchAll(/^\| (ADR-\d{3}) \|/gm)].map(m => m[1]);
  if (!decisionIds.length || new Set(decisionIds).size !== decisionIds.length) fail('Decision IDs must be unique.');
  for (const name of fs.readdirSync(path.join(root, 'docs/governance')).filter(p => p.endsWith('.md')))
    for (const id of read('docs/governance/' + name).match(/\b[A-Z]+-\d{3}\b/g) || [])
      if (![...ruleIds(spec), ...decisionIds].includes(id)) fail(name + ': unknown rule/decision reference ' + id);
  for (const id of decisions.match(/\b[A-Z]+-\d{3}\b/g) || [])
    if (!id.startsWith('ADR-') && !ruleIds(spec).includes(id)) fail('Unknown decision rule ' + id);
  for (const p of ['.github/CODEOWNERS', '.github/pull_request_template.md',
    '.github/workflows/governance.yml', 'tests/CropQc.Api.Tests/CanonicalInventoryArchitectureTests.cs',
    'docs/inventory-architecture/phase3-workflow-registry.json', 'docs/inventory-architecture/phase3-reviewed-write-candidates.json',
    'docs/governance/README.md', 'docs/governance/WINDOWS_SETUP.md', 'docs/governance/CHANGE_PROCEDURE.md',
    'docs/governance/RELEASE_AUTHORIZATION.md',
    'docs/governance/REPOSITORY_SETTINGS.md', 'docs/governance/VALIDATION.md', 'docs/governance/OUTSTANDING_PRS.md',
    'scripts/Sync-CropQcKnowledge.ps1', 'scripts/governance/sync.test.mjs',
    'docs/governance/PR274_CONSOLIDATION.md', 'docs/governance/POST_MERGE_ACTIVATION.md',
    'docs/governance/fixtures/enforcement-probe.patch', 'docs/governance/fixtures/enforcement-probe-body.txt'])
    if (!exists(p)) fail('Missing governance dependency ' + p);
  const requirements = [...spec.matchAll(/^\| (\d+) \|/gm)].map(m => Number(m[1]));
  if (JSON.stringify(requirements) !== JSON.stringify(Array.from({ length: 27 }, (_, i) => i + 1)))
    fail('The 27 foundational requirements must remain indexed. This checks structure, not their semantics.');
  if (event?.pull_request) {
    const base = event.pull_request.base.sha;
    if (!/^[a-f0-9]{40}$/.test(base)) fail('Invalid PR base.');
    const changed = execFileSync('git', ['diff', '--name-only', base, 'HEAD'], { cwd: root, encoding: 'utf8' }).trim().split(/\r?\n/);
    validateBody(event.pull_request.body || '', ruleIds(spec), changed.some(protectedPath), event.pull_request.head?.sha);
    const baseFiles = execFileSync('git', ['ls-tree', '-r', '--name-only', base, '--', 'docs/governance'], { cwd: root, encoding: 'utf8' });
    if (baseFiles.includes('docs/governance/CROP_QC_BUSINESS_RULES.md')) {
      const atBase = p => execFileSync('git', ['show', `${base}:${p}`], { cwd: root, encoding: 'utf8' });
      validateEvolution(atBase('docs/governance/CROP_QC_BUSINESS_RULES.md'), spec,
        JSON.parse(atBase('docs/governance/traceability.json')), matrix, changed.includes('docs/governance/ARCHITECTURAL_DECISIONS.md'));
    }
  }
  return matrix;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
  const event = process.env.GITHUB_EVENT_PATH ? JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8')) : undefined;
  const matrix = check(root, event);
  console.log('Governance metadata PASS: ' + matrix.rules.length + ' rules. Runtime proof and human approval are separate gates.');
}
