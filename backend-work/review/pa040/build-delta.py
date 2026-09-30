import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import zipfile

repo = Path(__file__).resolve().parents[3]
out = Path(__file__).resolve().parent
baseline = '3d69e398056b92ebefe479affdf6e887c522b628'

def git(*args, cwd=repo, env=None):
    return subprocess.check_output(['git', *args], cwd=cwd, env=env)

assert git('rev-parse', 'HEAD').decode().strip() == baseline
temp = Path(tempfile.mkdtemp(prefix='unicore-pa040-delta-'))
env = dict(os.environ, GIT_INDEX_FILE=str(temp / 'index'))
git('read-tree', baseline, env=env)
git('add', '--', 'src', 'scripts', env=env)
git('diff', '--cached', '--check', baseline, env=env)
patch = out / 'CRM-PA-040-source-delta-3d69e39.patch'
patch.write_bytes(git('diff', '--cached', '--binary', '--full-index', baseline, '--', 'src', 'scripts', env=env))
numstat = git('diff', '--cached', '--numstat', baseline, env=env).decode().splitlines()
files = [line.split('\t', 2)[2] for line in numstat]
assert not any('/Migrations/' in name or name.endswith('.slnx') for name in files)
assert not any('frontend' in name.lower() for name in files)
git('apply', '--reverse', '--check', str(patch))
roundtrip = temp / 'roundtrip'
roundtrip.mkdir()
archive = git('archive', '--format=zip', baseline)
with zipfile.ZipFile(io.BytesIO(archive)) as z:
    z.extractall(roundtrip)

def manifest():
    return {str(p.relative_to(roundtrip)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in roundtrip.rglob('*') if p.is_file()}

before = manifest()
git('apply', '--check', str(patch), cwd=roundtrip)
git('apply', str(patch), cwd=roundtrip)
for name in files:
    archived = git('hash-object', str(roundtrip / name), cwd=roundtrip).strip()
    expected = git('rev-parse', ':' + name, env=env).strip()
    assert archived == expected, name
git('apply', '--reverse', '--check', str(patch), cwd=roundtrip)
git('apply', '--reverse', str(patch), cwd=roundtrip)
assert manifest() == before, 'Reverse apply did not reproduce the complete baseline archive.'
metadata = {
    'baseline': baseline, 'head': git('rev-parse', 'HEAD').decode().strip(),
    'changed_files': len(files),
    'insertions': sum(int(line.split('\t')[0]) for line in numstat),
    'deletions': sum(int(line.split('\t')[1]) for line in numstat),
    'artifact': str(patch), 'sha256': hashlib.sha256(patch.read_bytes()).hexdigest(),
    'reverse_apply_current_check': 'PASS', 'forward_apply_baseline_check': 'PASS',
    'forward_files_match_index_blobs': 'PASS', 'reverse_apply_complete_baseline_hash_manifest': 'PASS',
    'diff_check_including_new_files': 'PASS', 'roundtrip_directory': str(roundtrip),
    'files': files,
}
(out / 'delta-metadata.json').write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')
print(json.dumps(metadata, indent=2))

