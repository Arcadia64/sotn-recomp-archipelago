"""Where each AP location is on the castle map, from the game's own stage data on the disc.

A stage file (loaded at 0x80180000) holds:
  - a room table (pointer at +0x10): 8 bytes per room, ended by 0x40: map left, top, right, bottom in map
    cells (the 64x64 grid of the pause-screen map, one cell per 256x256 screen), then tile layout, tileset,
    graphics and object-layout ids;
  - object-layout tables: for each object-layout id, a pointer to a list of the room's entities, 10 bytes
    each (x, y in room pixels, entity id, slot, params), between an 0xFFFE entry and an 0xFFFF entry. There
    are two tables (lists sorted by x and by y) with the same entities.
A location's item or relic entity (Locations.py "entities") is an entry in such a list, so its map cell is
its room's corner plus its position / 256.

Not every stage header points at the layout tables, so they're read from the code that uses them: the
stage's InitRoomEntities (header +0x0C), given the object-layout id * 4 in a0, starts with
    lui at, 0x8018 / addu at, at, a0 / lw a1, TABLE_X(at) / lui at, 0x8018 / addu at, at, a0 / lw v0, TABLE_Y(at)
(the x-sorted and y-sorted tables). A location's entity list is found in either table by its start.

Boss drops have no item entity: they're placed in the middle of the boss stage's largest room (a boss
location's second zone). The few locations with neither are placed at a stage entity found by its function
(ENTITY_FUNCTIONS, names from the recomp's generated code): the stage's entity table (entity id - 1 -> update
function) turns the function into an entity id, which is then looked for in the room layouts. Enemysanity
locations aren't anywhere in particular and get no position.
"""
import struct

SECTOR, SECTOR_DATA = 2352, 0x800
OVERLAY_BASE = 0x80180000
INIT_ROOM_ENTITIES_PTR, ROOMS_PTR = 0x0C, 0x10
LUI_AT_8018, ADDU_AT_AT_A0, LW_OPCODE = 0x3C018018, 0x00240821, 0x23
ENTITY_SIZE = 10
LIST_START, LIST_END = 0xFFFE, 0xFFFF
ENTITY_ID_MASK = 0x3FF

# Locations with no item entity and no boss stage: the stage entity whose update function marks the spot.
ENTITY_FUNCTIONS = {
    70: ("LIB", "EntityLibrarianChair"),   # Long Library - Librarian Shop Item
    85: ("CEN", "EntityMaria"),            # Marble Gallery - Item Given by Maria (in the Center Cube)
    387: ("NZ0", "EntitySlogra_nz0"),      # Alchemy Lab. - Slogra and Gaibon item
    389: ("LIB", "func_us_801BB53C"),      # Long Library - Lesser Demon item: spawns the Lesser Demon (entity
                                           # 0x1F) until it's beaten; the demon itself isn't in a layout
    390: ("NZ1", "EntityKarasuman"),       # Clock Tower - Karasuman item
}


class StageFile:
    def __init__(self, disc_path, zone):
        size = zone["len"]
        data = bytearray()
        with open(disc_path, "rb") as f:
            for s in range((size + SECTOR_DATA - 1) // SECTOR_DATA):
                f.seek(zone["pos"] + s * SECTOR)
                data += f.read(SECTOR_DATA)
        self.data = bytes(data[:size])
        self._rooms = None

    def has(self, addr, size=4):
        return OVERLAY_BASE <= addr and addr - OVERLAY_BASE + size <= len(self.data)

    def u16(self, addr):
        return struct.unpack_from("<H", self.data, addr - OVERLAY_BASE)[0]

    def u32(self, addr):
        return struct.unpack_from("<I", self.data, addr - OVERLAY_BASE)[0]

    def rooms(self):
        """[(left, top, right, bottom, object layout id)]"""
        if self._rooms is None:
            self._rooms = []
            at = self.u32(OVERLAY_BASE + ROOMS_PTR)
            while self.data[at - OVERLAY_BASE] != 0x40:
                left, top, right, bottom, _, _, _, layout = self.data[at - OVERLAY_BASE:at - OVERLAY_BASE + 8]
                self._rooms.append((left, top, right, bottom, layout))
                at += 8
        return self._rooms

    def layout_tables(self):
        """(x-sorted table, y-sorted table) addresses, from InitRoomEntities' code."""
        code = self.u32(OVERLAY_BASE + INIT_ROOM_ENTITIES_PTR)
        words = [self.u32(code + 4 * i) for i in range(32)]
        tables = []
        for i in range(len(words) - 2):
            if words[i] == LUI_AT_8018 and words[i + 1] == ADDU_AT_AT_A0 and words[i + 2] >> 26 == LW_OPCODE:
                imm = words[i + 2] & 0xFFFF
                tables.append(OVERLAY_BASE + (imm - 0x10000 if imm & 0x8000 else imm))
        if len(tables) < 2:
            raise ValueError(f"InitRoomEntities at 0x{code:08X} doesn't load the layout tables as expected")
        return tables[0], tables[1]

    def room_of_entity(self, entity_addr):
        """The room (as in rooms()) whose object layout holds the entity, or None."""
        start = entity_addr
        while self.has(start, 2) and self.u16(start) != LIST_START and entity_addr - start < 400 * ENTITY_SIZE:
            start -= ENTITY_SIZE
        if not self.has(start, 2) or self.u16(start) != LIST_START:
            return None
        rooms = self.rooms()
        for table in self.layout_tables():
            for room in rooms:
                if self.u32(table + 4 * room[4]) == start:
                    return room
        return None


    def layout_entities(self):
        """[(room, entity address, entity id)] for every entity in the stage's x-sorted layouts."""
        table, _ = self.layout_tables()
        out = []
        for room in self.rooms():
            at = self.u32(table + 4 * room[4])
            if not self.has(at, 2) or self.u16(at) != LIST_START:
                continue
            while True:
                at += ENTITY_SIZE
                if self.u16(at) == LIST_END:
                    break
                out.append((room, at, self.u16(at + 4) & ENTITY_ID_MASK))
        return out

    def function_cell(self, function_addr, function_starts):
        """(x, y) of the layout entity whose update function is function_addr. The entity table's position is
        whichever makes every entity id in the layouts point at a function start (in a stage with few entity
        ids, several can). If the entity is placed more than once (Slogra and Gaibon), the middle cell."""
        entities = self.layout_entities()
        ids = {e[2] for e in entities if e[2] > 0}
        refs = [OVERLAY_BASE + i for i in range(0, len(self.data) - 3, 4)
                if struct.unpack_from("<I", self.data, i)[0] == function_addr]
        cells = set()
        for ref in refs:
            for guess in ids:
                base = ref - 4 * (guess - 1)
                if all(self.has(base + 4 * (i - 1)) and self.u32(base + 4 * (i - 1)) in function_starts for i in ids):
                    target = (ref - base) // 4 + 1
                    cells |= {(r[0] + self.u16(at) // 256, r[1] + self.u16(at + 2) // 256)
                              for r, at, i in entities if i == target}
        if not cells:
            return None
        xs, ys = sorted(c[0] for c in cells), sorted(c[1] for c in cells)
        return xs[len(xs) // 2], ys[len(ys) // 2]


def entity_cell(stage_file, entity_addr):
    """(x, y) map cell of a layout entity, or None if its room isn't found."""
    room = stage_file.room_of_entity(entity_addr)
    if room is None:
        return None
    left, top, right, bottom, _ = room
    x = left + stage_file.u16(entity_addr) // 256
    y = top + stage_file.u16(entity_addr + 2) // 256
    if not (left <= x <= right and top <= y <= bottom):
        raise ValueError(f"entity at 0x{entity_addr:08X} ({x}, {y}) is outside its room {room}")
    return x, y


def boss_cell(stage_file):
    """The cell nearest the middle of a boss stage's largest room."""
    left, top, right, bottom, _ = max(stage_file.rooms(), key=lambda r: (r[2] - r[0] + 1) * (r[3] - r[1] + 1))
    return (left + right) // 2, (top + bottom) // 2


def location_cells(zones_mod, loc_mod, stage_ids, disc_path, function_table):
    """{ap id: (x, y, stage id)} for every location with a place on the map.

    function_table(overlay name) -> {function start address: name} from the recomp's generated code."""
    by_key = {v: k for k, v in zones_mod.ZONE.items()}
    files = {}

    def stage_file(key):
        if key not in files:
            files[key] = StageFile(disc_path, zones_mod.zones[zones_mod.ZONE[key]])
        return files[key]

    cells = {}
    for name, loc in loc_mod.locations.items():
        ap_id = loc.get("ap_id")
        if ap_id is None or loc.get("game_id") is not None or "Enemysanity" in name:
            continue
        zone_keys = [by_key[z] for z in loc.get("zones", [])]
        cell = None
        for i, entity in enumerate(loc.get("entities", [])):
            key = zone_keys[i >> 1]
            xy = entity_cell(stage_file(key), OVERLAY_BASE + entity)
            if xy:
                cell = (*xy, stage_ids[key])
                break
        if cell is None and ap_id in ENTITY_FUNCTIONS:
            key, function = ENTITY_FUNCTIONS[ap_id]
            names = function_table(key.lower())
            address = next((a for a, n in names.items() if n == function), None)
            if address is None:
                raise ValueError(f"{name}: no function {function} in {key.lower()}")
            xy = stage_file(key).function_cell(address, set(names))
            cell = xy and (*xy, stage_ids[key])
        if cell is None and loc.get("kill_time") and len(zone_keys) > 1:
            cell = (*boss_cell(stage_file(zone_keys[1])), stage_ids[zone_keys[1]])
        if cell is None:
            raise ValueError(f"no map position for {ap_id} {name}")
        cells[ap_id] = cell
    return cells
