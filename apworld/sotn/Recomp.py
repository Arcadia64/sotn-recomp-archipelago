"""Patch data for the SymphonyRecomp mod.

The recomp runs the game from a vanilla disc and loads each file from it into RAM as usual. Its
code was built to work with discs patched by sotn.io and this world: wherever it replaced game
code, it reads the patched bytes back from RAM. So the mod needs the same bytes this world writes
into the disc image, but per file, to write them into RAM when that file is loaded.

recomp_payload() turns the patch's tokens into:
    {"version": 1,
     "files": {"<zone key>" | "DRA" | "BIN": [[offset, "hex bytes"], ...]}}
Zone keys (NO3, RBO2, ...) are stage and boss files, loaded at 0x80180000; offsets are within
the file. "DRA" is DRA.BIN, loaded at 0x800A0000. Anything else stays as raw disc offsets under
"BIN" for the mod to map from the disc's own directory if it needs them.
"""
import math
from typing import Dict, List, Optional, Tuple

from worlds.Files import APTokenTypes

from .data.Zones import ZONE, zones

PAYLOAD_VERSION = 1

SECTOR = 2352
SECTOR_HEADER = 24
SECTOR_DATA = 0x800

DRA_LBA = 299
DRA_SIZE = 1153136
DRA_RAM = 0x800A0000

# DRA's Time Attack labels, which write_seed fills with data only the BizHawk client reads.
SEED_BLOCK = (0x800DFAEC - DRA_RAM, 0x800DFD44 - DRA_RAM)

_ZONE_KEYS = {v: k for k, v in ZONE.items()}
_ZONE_SPANS = [
    (zone["pos"], zone["pos"] + math.ceil(zone["len"] / SECTOR_DATA) * SECTOR, _ZONE_KEYS[num])
    for num, zone in zones.items() if "pos" in zone and "len" in zone
]
_DRA_START = DRA_LBA * SECTOR
_DRA_END = (DRA_LBA + math.ceil(DRA_SIZE / SECTOR_DATA)) * SECTOR


def disc_to_file(offset: int) -> Optional[Tuple[str, int]]:
    """(file key, offset in file) for a raw disc-image offset; None for a sector's EDC/ECC or header
    bytes, which are not file data (recalculated when patching, so a write there has no effect)."""
    for start, end, key in _ZONE_SPANS:
        if start <= offset < end:
            sector, within = divmod(offset - start, SECTOR)
            return (key, sector * SECTOR_DATA + within) if within < SECTOR_DATA else None
    if _DRA_START <= offset < _DRA_END:
        sector, within = divmod(offset - _DRA_START, SECTOR)
        within -= SECTOR_HEADER
        return ("DRA", sector * SECTOR_DATA + within) if 0 <= within < SECTOR_DATA else None
    return "BIN", offset


def recomp_payload(patch) -> Dict:
    by_file: Dict[str, Dict[int, int]] = {}
    for token_type, offset, data in patch._tokens:
        if token_type != APTokenTypes.WRITE:
            raise ValueError(f"token type {token_type} at 0x{offset:X} not supported by the recomp payload")
        for i, byte in enumerate(data):
            where = disc_to_file(offset + i)
            if where is None:
                continue
            key, file_offset = where
            if key == "DRA" and SEED_BLOCK[0] <= file_offset < SEED_BLOCK[1]:
                continue
            by_file.setdefault(key, {})[file_offset] = byte  # later tokens win, as when patching

    files: Dict[str, List[list]] = {}
    for key, bytes_at in by_file.items():
        runs: List[list] = []
        last: Optional[int] = None
        for off in sorted(bytes_at):
            if last is not None and off == last + 1:
                runs[-1][1] += f"{bytes_at[off]:02x}"
            else:
                runs.append([off, f"{bytes_at[off]:02x}"])
            last = off
        files[key] = runs
    return {"version": PAYLOAD_VERSION, "files": files}
