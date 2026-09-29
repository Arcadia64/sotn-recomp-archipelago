"""Patch data for the SymphonyRecomp mod.

The recomp runs the game from a vanilla disc and loads each file from it into RAM as usual. Its
code was built to work with discs patched by sotn.io and this world: wherever it replaced game
code, it reads the patched bytes back from RAM. So the mod needs the same bytes this world writes
into the disc image, but per file, to write them into RAM when that file is loaded.

recomp_payload() turns the patch's tokens into:
    {"version": 1,
     "files": {"<zone key>" | "DRA" | "SEL" | "MAR" | "F_GAME" | "F_GAME2" | "RIC": [[offset, "hex bytes"], ...]}}
Zone keys (NO3, RBO2, ...) and MAR (the Clock Room cutscene, stage 0x17) are stage and boss files,
loaded at 0x80180000; offsets are within the file. DRA is DRA.BIN (0x800A0000). SEL is the title and
file-select overlay (also 0x80180000). F_GAME/F_GAME2 are graphics streamed to VRAM (Alucard/Richter).
RIC is Richter's overlay. Anything else stays as raw disc offsets under "BIN" (none with the current
Rom.py). The CD-loaded code block in WARNING.TIM is left out: the recomp never runs it.
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

# DRA's Time Attack labels, which Rom.py (from the BizHawk world) fills with lists for its client:
# relic copies, the Doppleganger 10 and Librarian items, enemysanity items. The mod works all of that
# out from the seed's scouts. Rom.py writes the same data again at DRA + SEED_COPY_OFFSET, which lands
# in SEL.BIN; that copy is skipped too.
SEED_BLOCK = (0x800DFAEC - DRA_RAM, 0x800DFD44 - DRA_RAM)
SEED_COPY_OFFSET = 0x4298798

# Other files Rom.py writes to: key -> (LBA, size in bytes), from the disc's ISO9660 directory.
OTHER_FILES = {
    "SEL": (30031, 355112),        # /ST/SEL/SEL.BIN
    "MAR": (45043, 110448),        # /BOSS/MAR/MAR.BIN
    "F_GAME": (25038, 270336),     # /BIN/F_GAME.BIN
    "F_GAME2": (25170, 270336),    # /BIN/F_GAME2.BIN
    "RIC": (25814, 236120),        # /BIN/RIC.BIN
    "WARNING": (24545, 327700),    # /WARNING.TIM (not sent)
}
SKIPPED_FILES = {"WARNING"}

_ZONE_KEYS = {v: k for k, v in ZONE.items()}
_ZONE_SPANS = [
    (zone["pos"], zone["pos"] + math.ceil(zone["len"] / SECTOR_DATA) * SECTOR, _ZONE_KEYS[num])
    for num, zone in zones.items() if "pos" in zone and "len" in zone
]
# (first disc byte, end, key) for files whose data starts SECTOR_HEADER into their first sector.
_SECTOR_SPANS = [(DRA_LBA * SECTOR, (DRA_LBA + math.ceil(DRA_SIZE / SECTOR_DATA)) * SECTOR, "DRA")] + [
    (lba * SECTOR, (lba + math.ceil(size / SECTOR_DATA)) * SECTOR, key) for key, (lba, size) in OTHER_FILES.items()
]


def disc_to_file(offset: int) -> Optional[Tuple[str, int]]:
    """(file key, offset in file) for a raw disc-image offset; None for a sector's EDC/ECC or header
    bytes, which are not file data (recalculated when patching, so a write there has no effect)."""
    for start, end, key in _ZONE_SPANS:
        if start <= offset < end:
            sector, within = divmod(offset - start, SECTOR)
            return (key, sector * SECTOR_DATA + within) if within < SECTOR_DATA else None
    for start, end, key in _SECTOR_SPANS:
        if start <= offset < end:
            sector, within = divmod(offset - start, SECTOR)
            within -= SECTOR_HEADER
            return (key, sector * SECTOR_DATA + within) if 0 <= within < SECTOR_DATA else None
    return "BIN", offset


def _in_seed_block(key: str, disc_offset: int, file_offset: int) -> bool:
    if key == "DRA":
        return SEED_BLOCK[0] <= file_offset < SEED_BLOCK[1]
    if key == "SEL":
        original = disc_to_file(disc_offset - SEED_COPY_OFFSET)
        return original is not None and original[0] == "DRA" and SEED_BLOCK[0] <= original[1] < SEED_BLOCK[1]
    return False


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
            if key in SKIPPED_FILES or _in_seed_block(key, offset + i, file_offset):
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
