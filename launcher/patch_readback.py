import argparse
import hashlib
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent / 'pythondeps'))
import pefile

parser = argparse.ArgumentParser()
parser.add_argument('--emulator', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--manifest', type=Path, required=True)
args = parser.parse_args()

data = bytearray(args.emulator.read_bytes())
source_hash = hashlib.sha256(data).hexdigest()
if source_hash != '6663bd4553770723c6553d0b7dd37bcd32b93aa5472edd18f5afb6a299720368':
    raise ValueError('Readback integration requires the validated unified memory build')
pe = pefile.PE(data=data)
branch_rva = 0xd89c98
if pe.get_data(0xd89c83, 23) != bytes.fromhex('498b0c2480b9580d00000148bf00000000010000007518'):
    raise ValueError('Unexpected readback policy selection')
if pe.get_data(0xd89cb2, 14) != bytes.fromhex('41c68424f801000000e98d010000'):
    raise ValueError('Unexpected serialized readback branch')
offset = pe.get_offset_from_rva(branch_rva)
data[offset] = 0xeb
args.output.write_bytes(data)
manifest = {
    'revision': 'R2',
    'input_sha256': source_hash,
    'patched_sha256': hashlib.sha256(data).hexdigest(),
    'policy_branch_rva': hex(branch_rva),
    'before': '7518',
    'after': 'eb18',
    'changed_bytes': 1,
    'policy': 'Always select existing Runtime::CopyBuffer + Scheduler::Finish readback path; retains runtime hazard tracking and completion waits',
    'source_equivalent': 'readback-serialization.patch',
}
args.manifest.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
print(json.dumps(manifest, indent=2))
