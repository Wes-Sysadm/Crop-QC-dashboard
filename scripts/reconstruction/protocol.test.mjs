import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const here = path.dirname(fileURLToPath(import.meta.url));
const shell = process.env.CROPQC_TEST_POWERSHELL || 'pwsh';
const fixtures = [
  ['UTC and fractional seconds', '{"at":"2026-10-09T01:06:05.9708969+00:00","utc":"2026-10-09T01:06:05Z"}'],
  ['offsets and date-like strings', '{"at":"2026-09-04T08:07:31.459524-07:00","east":"2026-10-09T10:06:05+09:00","date":"2026-09-04"}'],
  ['null and booleans', '{"missing":null,"yes":true,"no":false}'],
  ['numeric lexemes', '{"large":9007199254740993,"decimal":1.0000,"exponent":1e+009,"minusZero":-0}'],
  ['strings remain strings', '{"n":"001","b":"false","nil":"null","key":"2026|511|2|3152"}'],
  ['array content and order', '{"a":[3,1,null,{"at":"2026-10-09T01:06:05.1234567Z"},[]]}'],
  ['property order and whitespace', '  {\r\n  "z" : 1, "a": { "second": 2, "first": 1 }\r\n}\r\n'],
  ['embedded beforeSegmentsJson escapes', String.raw`{"beforeSegmentsJson":"[{\u0022at\u0022:\u00222026-09-04T15:07:31.459524\u002B00:00\u0022}]"}`],
  ['Unicode and escaping', '{"unicode":"FUJI 🍎 café 漢字","escape":"\\u0041\\uD83C\\uDF4E","literal":"\\\\ \\" \\n \\t < > &"}'],
];

function fixture(t, preview, metadata = '{"approvalKey":"test","explicitlyApprove":true}') {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'cropqc-protocol-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const input = path.join(dir, 'preview.json'), meta = path.join(dir, 'metadata.json'), output = path.join(dir, 'request.json');
  fs.writeFileSync(input, preview); fs.writeFileSync(meta, metadata);
  const invoke = (mode = 'Approve') => spawnSync(shell, ['-NoProfile', '-File', path.join(here, 'Write-ReconstructionRequest.ps1'),
    '-Mode', mode, '-PreviewFile', input, '-MetadataFile', meta, '-OutputFile', output], { encoding: 'utf8', timeout: 15000 });
  return { dir, input, meta, output, invoke };
}

for (const [name, json] of fixtures) {
  test(`opaque approval and execution preserve ${name}`, t => {
    const f = fixture(t, json);
    let r = f.invoke(); assert.equal(r.status, 0, r.stderr);
    const approval = fs.readFileSync(f.output);
    const expected = Buffer.from('{"approvalKey":"test","explicitlyApprove":true,"preview":' + json + '}');
    assert.deepEqual(approval, expected); // Byte-for-byte, including numeric/escape spellings.
    assert.deepEqual(fs.readFileSync(f.input), Buffer.from(json));
    JSON.parse(approval); // Syntax only; never used to construct transported data.
    fs.unlinkSync(f.output);
    fs.writeFileSync(f.meta, '{"operationKey":"test","approvalAuditId":123,"operatorId":1,"explicitlyExecute":true}');
    r = f.invoke('Execute'); assert.equal(r.status, 0, r.stderr);
    assert.equal(fs.readFileSync(f.output, 'utf8'), '{"operationKey":"test","approvalAuditId":123,"operatorId":1,"explicitlyExecute":true,"preview":' + json + '}');
  });
}

for (const [name, input, metadata] of [
  ['malformed JSON', '{"a":', '{}'],
  ['diagnostics mixed with JSON', 'warn: diagnostic\n{"a":1}', '{}'],
  ['multiple objects', '{} {}', '{}'],
  ['scalar preview', 'null', '{}'],
  ['duplicate preview properties', '{"value":1,"Value":2}', '{}'],
  ['preview metadata injection', '{}', '{"preview":{"changed":true}}'],
  ['case variant metadata injection', '{}', '{"Preview":{}}'],
  ['duplicate metadata', '{}', '{"approvalKey":"a","ApprovalKey":"b"}'],
  ['unexpected metadata', '{}', '{"unreviewedFlag":true}'],
  ['BOM', '\ufeff{}', '{}'],
  ['invalid UTF-8', Buffer.from([0x7b, 0x22, 0xff, 0x22, 0x3a, 0x31, 0x7d]), '{}'],
]) {
  test(`reject ${name} without creating an approval file`, t => {
    const f = fixture(t, input, metadata); const r = f.invoke();
    assert.notEqual(r.status, 0); assert.equal(fs.existsSync(f.output), false);
  });
}

test('never overwrite an existing bound request', t => {
  const f = fixture(t, '{}'); fs.writeFileSync(f.output, 'immutable prior intent');
  assert.notEqual(f.invoke().status, 0);
  assert.equal(fs.readFileSync(f.output, 'utf8'), 'immutable prior intent');
});
