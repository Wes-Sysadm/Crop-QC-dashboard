"""Opt-in backup-189 protocol rehearsal. Only an existing, isolated localhost restore.

Uses the actual PowerShell transport and the supplied frozen maintenance binary.
No Render connection, backup creation, schema migration, or production configuration.
"""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

p = argparse.ArgumentParser()
p.add_argument('--binary', required=True)
p.add_argument('--database', required=True)
p.add_argument('--pg-bin', required=True)
p.add_argument('--port', type=int, required=True)
p.add_argument('--output', required=True)
p.add_argument('--manifest', required=True)
a = p.parse_args()
assert a.database.startswith('pr271_release_') and a.database.replace('_', '').isalnum()
if os.name == 'nt':
    # Inherit no crash-dialog behavior for intentionally rejected CLI commands.
    ctypes.windll.kernel32.SetErrorMode(0x0002)
root = Path(__file__).resolve().parent
binary = Path(a.binary).resolve()
out = Path(a.output).resolve()
out.mkdir(exist_ok=False)
manifest = json.loads(Path(a.manifest).read_text(encoding='utf-8-sig'))
assert manifest['commit'] == 'fd7f691c2268779c26740e5f3fa7866390691b5d'
for f in manifest['files']:
    assert hashlib.sha256((binary.parent / f['file']).read_bytes()).hexdigest().upper() == f['sha256'].upper()
env = os.environ.copy()
env.update({'ConnectionStrings__CropQc': f'Host=127.0.0.1;Port={a.port};Database={a.database};Username=postgres;Timezone=UTC',
            'Database__Provider': 'PostgreSql', 'ASPNETCORE_ENVIRONMENT': 'Production',
            'CanonicalInventoryCommandsEnabled': 'true', 'Database__EnsureCreatedOnStartup': 'false',
            'Database__SeedMasterDataOnStartup': 'false', 'Logging__LogLevel__Default': 'Warning',
            'PGOPTIONS': '-c timezone=UTC', 'PGSSLMODE': 'disable'})
env.pop('DATABASE_URL', None)
psql = str(Path(a.pg_bin) / ('psql.exe' if os.name == 'nt' else 'psql'))
pg = [psql, '-X', '-q', '-A', '-t', '-h', '127.0.0.1', '-p', str(a.port), '-U', 'postgres', '-d', a.database, '-v', 'ON_ERROR_STOP=1']
def sql(query):
    return subprocess.run(pg + ['-c', query], env=env, capture_output=True, check=True, text=True).stdout.strip()
def save(name, value):
    path = out / (name + '.json')
    path.write_text(json.dumps(value, indent=2), encoding='utf-8')
    return path
def command(mode, request, name, accepted=True):
    stdout, stderr = out / (name + '.stdout'), out / (name + '.stderr')
    args = ['pwsh', '-NoProfile', '-File', str(root / 'Invoke-ReconstructionMaintenance.ps1'), '-Mode', mode,
            '-BinaryPath', str(binary), '-OutputFile', str(stdout), '-ErrorFile', str(stderr)]
    if request:
        args += ['-RequestFile', str(request)]
    if mode in ('Approve', 'Execute'):
        args += ['-Confirmation', 'DisposableRestore']
    before_bytes = request.read_bytes() if request else None
    r = subprocess.run(args, env=env, capture_output=True, timeout=120)
    assert (request.read_bytes() if request else None) == before_bytes
    if accepted:
        assert r.returncode == 0, (name, r.stderr.decode(), stderr.read_text())
    else:
        assert r.returncode != 0, name
    text = stdout.read_text(encoding='utf-8')
    return json.loads(text) if text.strip() else None
def envelope(mode, preview, metadata, name):
    meta = save(name + '-metadata', metadata)
    target = out / (name + '.json')
    r = subprocess.run(['pwsh', '-NoProfile', '-File', str(root / 'Write-ReconstructionRequest.ps1'),
                        '-Mode', mode, '-PreviewFile', str(preview), '-MetadataFile', str(meta), '-OutputFile', str(target)],
                       env=env, capture_output=True, timeout=30)
    assert r.returncode == 0, r.stderr.decode()
    expected = meta.read_bytes().rstrip()[:-1] + b',"preview":' + preview.read_bytes() + b'}'
    assert target.read_bytes() == expected, 'Wrapper changed exact preview bytes'
    return target
def approval(preview, name):
    return envelope('Approve', preview, {'approvalKey': 'lossless189-' + name, 'approverId': 1,
        'approvalReference': 'Isolated backup189 protocol regression only',
        'reason': 'Validate unchanged production approval guards through lossless PowerShell transport',
        'explicitlyApprove': True}, name + '-approval')

targets = [(1,15,511,2,'3152','GALA',61,162), (1,15,642,2,'9682','GALA',252,536),
           (4,4,448,17,'1372','BART',1122,1568), (2,47,495,18,'2350','DANJ',202,598),
           (4,2,398,2,'1084','GALA',10,130), (4,5,495,18,'2350','DANJ',170,362)]
old_ids = [414,426,419,428,617,618,619,630,669,670,407,557,65,597,552,553]
tables = ['Rooms','Warehouses','GrowerLots','FruitProfiles','Receipts','ReceiptVarietyLines',
          'RoomInventoryAdjustments','RoomTransfers','InterCrewTransfers','OutsideWarehouseTransfers','BinsRunEntries',
          'ActualRuns','ActualRunRevisions','RoomInventoryLosses','ReceiptInventoryOverrides','InventoryIdentityCorrections',
          'RoomTreatmentApplications','RoomTreatmentApplicationSources','TreatmentLineageMovements',
          'TreatmentLineageSegmentApplications','RoomDepletions','ProcessorShipments','ProcessorShipmentLines',
          'InventoryCommands','AuditLogs','TreatmentLineageSegments']
def fingerprints(excluded):
    queries = []
    for t in tables:
        where = " WHERE NOT (\"EntityName\"='ProjectionReconstruction' AND \"SourceApplication\"='CanonicalProjectionReconstruction/v1')" if t == 'AuditLogs' else (
            " WHERE \"OperationKey\" NOT LIKE 'lossless189-%'" if t == 'InventoryCommands' else (
            ' WHERE "Id" NOT IN (' + ','.join(map(str, excluded)) + ')' if t == 'TreatmentLineageSegments' else ''))
        queries.append(f"SELECT '{t}' AS name, count(*) AS count,md5(coalesce(string_agg(to_jsonb(t)::text,'' ORDER BY to_jsonb(t)::text),'')) AS hash FROM \"{t}\" t" + where)
    return json.loads(sql('SELECT json_agg(x) FROM (' + ' UNION ALL '.join(queries) + ') x'))
assert sql('SELECT count(*) FROM "InventoryCommands" WHERE "OperationKey" LIKE \'lossless189-%\'') == '0'
before = fingerprints(old_ids)
save('protected-before', before)
pre = command('Readiness', None, 'readiness-before', False)
assert pre['schemaReady'] and pre['inventory']['isReady'] and pre['topology']['success']
assert pre['treatment']['blockingIssueCount'] == 6
results = []
excluded = old_ids.copy()
stale_preview = None
for wh,room,lot,profile,number,variety,quantity,projected in targets:
    key = f'r{room}-g{lot}'
    target = save(key+'-target', {'warehouseId':wh, 'roomId':room, 'identity':{'cropYear':2026,'growerLotId':lot,
        'fruitProfileId':profile,'lot':number,'growerNumber':number,'variety':variety,'productionType':'Conventional','isOrganic':False,'status':''}})
    preview = command('Preview', target, key+'-preview')
    preview_file = out / (key+'-preview.stdout')
    assert preview['eligible'] and preview['authoritativeQuantity'] == quantity and preview['projectedQuantity'] == projected
    assert preview['plan']['replacementReceiptId'] is None and not preview['otherCustody']
    if not results:
        # Actually modified, otherwise eligible protocol data must remain rejected.
        changed = dict(preview, authoritativeQuantity=quantity+1)
        changed_file = save('modified-preview', changed)
        invalid = approval(changed_file, 'modified')
        command('Approve', invalid, 'modified-result', False)
        assert 'Approval preview is stale or unsupported.' in (out/'modified-result.stderr').read_text()
        # Independent PostgreSQL session holds an incompatible writer lock.
        holder = subprocess.Popen(pg, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            holder.stdin.write('BEGIN; LOCK TABLE "RoomInventoryAdjustments" IN ROW EXCLUSIVE MODE; SELECT \'LOCK_HELD\';\n'); holder.stdin.flush()
            assert holder.stdout.readline().strip() == 'LOCK_HELD'
            locked = approval(preview_file, 'locked')
            command('Approve', locked, 'locked-result', False)
            assert '55P03' in (out/'locked-result.stderr').read_text()
        finally:
            holder.communicate('ROLLBACK;\n', timeout=10)
        assert fingerprints(old_ids) == before
        assert sql('SELECT count(*) FROM "AuditLogs" WHERE "EntityKey" LIKE \'lossless189-%\'') == '0'
        stale_preview = preview_file
    approve_request = approval(preview_file, key)
    approved = command('Approve', approve_request, key+'-approval-result')
    execute_request = envelope('Execute', preview_file, {'operationKey':'lossless189-'+key,
        'approvalAuditId':approved['approvalAuditId'],'operatorId':1,'explicitlyExecute':True}, key+'-execute')
    result = command('Execute', execute_request, key+'-execute-result')
    assert result['status'] == 'Committed' and result['authoritativeQuantityDelta'] == 0
    verified = command('Verify', execute_request, key+'-verify')
    replay = command('Execute', execute_request, key+'-replay')
    assert verified['status'] == 'Verified' and replay['status'] == 'Replayed'
    excluded.append(result['replacementSegmentId'])
    results.append({'key':key,'authorityBefore':quantity,'authorityAfter':quantity,'projectionBefore':projected,
                    'projectionAfter':quantity,'approvalAuditId':approved['approvalAuditId'], 'result':result,'verification':verified,'replay':replay,
                    'previewSha256':hashlib.sha256(preview_file.read_bytes()).hexdigest(),'opaqueBytesPreserved':True})
    if len(results) == 1:
        # Original preview is now genuinely stale because its projections changed.
        stale = approval(stale_preview, 'stale')
        command('Approve', stale, 'stale-result', False)
        assert 'Approval preview is stale or unsupported.' in (out/'stale-result.stderr').read_text()
    print(f'{key}: committed, verified, replayed; authoritative delta 0', flush=True)
after = fingerprints(excluded)
save('protected-after', after)
assert before == after, 'Protected history changed'
ready = command('Readiness', None, 'readiness-after')
assert ready['success'] and ready['treatment']['blockingIssueCount'] == 0
assert sql('SELECT count(*) FROM "AuditLogs" WHERE "EntityName"=\'ProjectionReconstruction\' AND "EntityKey" LIKE \'lossless189-%\'') == '12'
assert sql('SELECT count(*) FROM "InventoryCommands" WHERE "OperationKey" LIKE \'lossless189-%\'') == '6'
for result in results:
    verified = command('Verify', out/(result['key']+'-execute.json'), result['key']+'-final-verify')
    assert verified['status'] == 'Verified'
report = {'sourceCommit':manifest['commit'], 'database':a.database, 'results':results, 'protectedGroups':len(before),
          'protectedUnchanged':True, 'modifiedRejected':True,'staleRejected':True,'concurrentWriterRejected':True,
          'approvalAudits':6,'repairAudits':6,'commands':6,'fullReadiness':ready,'authoritativeTotalBefore':1817,
          'authoritativeTotalAfter':1817,'projectionBefore':3356,'projectionAfter':1817,'productionAccess':False}
save('rehearsal', report)
print('PASS: six actual-wrapper repairs; modified/stale/concurrent rejection; 26 protected groups unchanged; full readiness passed.')
