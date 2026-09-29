from dataclasses import dataclass
from Options import (OptionGroup, Toggle, Choice, NamedRange, ItemsAccessibility, StartInventoryPool,
                     PerGameCommonOptions)

# "Key items" below are the items the logic can need: the relics, the Gold and Silver rings, the Spike breaker
# and the Holy glasses.


# ---- Item pool ----

class ItemPool(Choice):
    """Which spots hold items from the multiworld. The others keep their vanilla item (or get shuffled, with
    randomize_other_items).
    key_items: only the spots that hold key items in the vanilla game.
    guarded: those, plus most items guarded by bosses.
    equipment: those, plus most of the equipment lying around.
    everything: every item spot in the castle."""
    display_name = "Item pool"
    option_key_items = 0
    option_guarded = 1
    option_equipment = 2
    option_everything = 3
    alias_relic_prog = 0
    alias_full = 3
    default = 3


class RandomizeOtherItems(Toggle):
    """The vanilla items of the spots that aren't in the item pool are shuffled among those spots."""
    display_name = "Shuffle the other items"


class PowerfulItems(Toggle):
    """Puts the game's most powerful items in: Duplicator, Crissaegrim, Ring of varda, Mablung sword, Masamune,
    Marsil and Yasutsuna, each in place of another item (in the item pool, or with randomize_other_items at one
    of the other spots)."""
    display_name = "Powerful items"


class BossDrops(Toggle):
    """The items the 13 bosses drop when beaten are checks. Your own key items are never put there: a drop you
    leave behind is gone for good."""
    display_name = "Boss drops"


class Enemysanity(Toggle):
    """Each kind of enemy is a check the first time it gets into your enemy list (141 checks). What fills them
    depends on difficulty:
    easy: a second copy of every key item, plus 50 vessels and 50 pieces of equipment.
    normal: a second Gold ring, Silver ring, Spike breaker and Holy glasses, plus 35 vessels and 35 pieces of
    equipment.
    hard: 15 vessels and 15 pieces of equipment.
    very_hard: nothing extra.
    The rest is filler (fruit)."""
    display_name = "Enemysanity"


class EnemysanityNeedsFaerieScroll(Toggle):
    """Enemysanity checks only count once you have the Faerie scroll (enemies met before then count once you
    get it)."""
    display_name = "Enemysanity needs the Faerie scroll"


# ---- Castle ----

class CavernsBackDoor(Choice):
    """The back door between the Castle Entrance and the Underground Caverns.
    closed: closed, as in the vanilla game (opened from the Caverns side).
    open_after_alchemy_lab: open once you've been to the Alchemy Laboratory.
    open_from_start: open from the start, so you can get to it before meeting Death at the entrance."""
    display_name = "Caverns back door"
    option_closed = 0
    option_open_after_alchemy_lab = 1
    option_open_from_start = 2
    alias_open = 1
    alias_open_early = 2
    default = 0


class ColosseumBackDoor(Toggle):
    """The back door between the Royal Chapel and the Colosseum is open from the start."""
    display_name = "Colosseum back door"


class OpenShortcuts(Toggle):
    """The castle's shortcuts are open from the start: from the Castle Entrance to the Underground Caverns, to the
    Marble Gallery and to its warp room, from Olrox's Quarters and from the Colosseum to the Royal Chapel, and the
    one in the inverted castle's Entrance. The logic doesn't count on them."""
    display_name = "Open shortcuts"


class StartingArea(Choice):
    """After the first Warg at the entrance, you're taken to a random room and play on from there.
    vanilla: you aren't; the game goes on from the entrance.
    random_castle: a room in the castle.
    random_inverted_castle: a room in the inverted castle (Leap stone and Gravity boots work there until you
    reach the castle).
    random_anywhere: a room in either.
    The logic doesn't take the new start into account."""
    display_name = "Starting area"
    option_vanilla = 0
    option_random_castle = 1
    option_random_inverted_castle = 2
    option_random_anywhere = 3
    alias_normal_castle = 1
    alias_reverse_castle = 2
    alias_any_castle = 3
    default = 0


class InvertedLibraryCard(Toggle):
    """Hold down while using a Library card to go to the inverted castle's library instead (once Richter is
    saved)."""
    display_name = "Library card to the inverted castle"


class SkipClockTowerPuzzle(Toggle):
    """No gear puzzle in the Clock Tower: hitting any of the gears once opens the secret door."""
    display_name = "Skip the Clock Tower puzzle"


class NoLogic(Toggle):
    """Items go anywhere, without logic: the seed can need glitches, or be unbeatable."""
    display_name = "No logic"


# ---- Enemies and drops ----

class Difficulty(Choice):
    """easy: enemies have half their HP, attack and defence and always drop their items; the shop costs 50-75%.
    normal: the vanilla game.
    hard: enemies have 1.5 times their HP, attack and defence; the shop costs 100-125%.
    very_hard: enemies have twice their HP, attack and defence; the shop costs 125-150%."""
    display_name = "Difficulty"
    option_easy = 0
    option_normal = 1
    option_hard = 2
    option_very_hard = 3
    default = 1


class EnemyStrength(NamedRange):
    """Enemy HP, attack and defence as a percentage of the vanilla values (25-200), instead of what difficulty
    sets. use_difficulty: leave it to difficulty."""
    display_name = "Enemy strength"
    range_start = 24
    range_end = 200
    default = 24
    special_range_names = {"use_difficulty": 24}


class RandomEnemyStats(Toggle):
    """Each enemy gets random HP, attack and defence (25-200% of the vanilla values; difficulty or
    enemy_strength replaces this part with its own scaling), and a random attack element, weaknesses and
    resistances."""
    display_name = "Random enemy stats"


class DropRate(Choice):
    """How often enemies drop their items.
    normal: as difficulty sets (the vanilla rates, or always on easy).
    increased: more often.
    abundant: more often still.
    guaranteed: every enemy that has a drop always drops; with the Ring of arcana, its rare one."""
    display_name = "Drop rate"
    option_normal = 0
    option_increased = 1
    option_abundant = 2
    option_guaranteed = 3
    default = 0


class EnemyDrops(Choice):
    """What enemies drop.
    vanilla: the vanilla drops.
    shuffled: the vanilla drops, shuffled between enemies.
    same_type: each drop is a random item of the same kind (a weapon for a weapon, a potion for a potion...).
    any_item: each drop is any random item."""
    display_name = "Enemy drops"
    option_vanilla = 0
    option_shuffled = 1
    option_same_type = 2
    option_any_item = 3
    default = 0


class EnemyDropsIncludeHeartsAndGold(Toggle):
    """With enemy_drops, the hearts, gold and other small drops any enemy can leave change too."""
    display_name = "Enemy drops: hearts and gold too"


class EnemyDropsCanBeKeyItems(Toggle):
    """With enemy_drops same_type or any_item, a drop can be a Gold ring, Silver ring, Spike breaker or Holy
    glasses: extra copies, outside the logic."""
    display_name = "Enemy drops: can be key items"


class CandleDrops(Choice):
    """What candles drop (not the ones with the Stopwatch).
    vanilla: the vanilla items.
    shuffled: the vanilla items, shuffled between candles.
    same_type: each is a random item of the same kind, never a key item.
    any_item: each is any random item, never a key item.
    any_item_with_key_items: any random item, including the Gold ring, Silver ring, Spike breaker and Holy
    glasses (extra copies, outside the logic)."""
    display_name = "Candle drops"
    option_vanilla = 0
    option_shuffled = 1
    option_same_type = 2
    option_any_item = 3
    option_any_item_with_key_items = 4
    alias_off = 0
    alias_simple = 1
    alias_type = 2
    alias_full = 3
    alias_full_progression = 4
    default = 0


# ---- Shop and equipment ----

class ShopStock(Choice):
    """What the Librarian sells.
    vanilla: the vanilla stock.
    random_items: random items, which can be key items (extra copies, outside the logic).
    random_non_key_items: random items, never a key item."""
    display_name = "Shop stock"
    option_vanilla = 0
    option_random_items = 1
    option_random_non_key_items = 2
    default = 0


class ShopSellsLibraryCard(Toggle):
    """With random items in shop_stock, the Librarian still sells the Library card."""
    display_name = "Shop sells the Library card"


class RandomShopPrices(Toggle):
    """Shop prices are random, 50-150% of the vanilla prices, instead of what difficulty sets."""
    display_name = "Random shop prices"


class RandomStartingEquipment(Toggle):
    """Alucard starts with a random weapon, shield, helmet, armour, cloak and accessory, never a key item."""
    display_name = "Random starting equipment"


class KeepStartingEquipment(Toggle):
    """Death doesn't take Alucard's equipment at the entrance."""
    display_name = "Death doesn't take your equipment"


# ---- Quality of life ----

class SkipPrologue(Toggle):
    """A new game starts with Alucard, without Richter's fight with Dracula."""
    display_name = "Skip the prologue"


class InfiniteWingSmash(Toggle):
    """Wing smash keeps going until you hit a wall or run out of MP (leave bat form to stop it)."""
    display_name = "Infinite wing smash"


class MagicVessels(Toggle):
    """Heart Vessels also raise max MP by 3 and refill MP."""
    display_name = "Magic vessels"


class NoScreenFreezes(Toggle):
    """The game doesn't pause when you level up or pick up a relic or a vessel."""
    display_name = "No screen freezes"


class FastWarp(Toggle):
    """Faster animation when using a warp room."""
    display_name = "Fast warp"


class HealAtSaveRooms(Toggle):
    """Entering a save room restores HP and MP."""
    display_name = "Heal at save rooms"


class DeathLink(Toggle):
    """When you die, everyone else with DeathLink on dies too, and the other way round."""
    display_name = "DeathLink"


# ---- Cosmetic ----

class MapColor(Choice):
    """Colour of the map (the pause map, and the mod's map window)."""
    display_name = "Map colour"
    option_default = 0
    option_dark_blue = 1
    option_crimson = 2
    option_brown = 3
    option_dark_green = 4
    option_gray = 5
    option_purple = 6
    option_pink = 7
    option_black = 8
    option_invisible = 9
    default = 0


class AlucardPalette(Choice):
    """Alucard's colours."""
    display_name = "Alucard's colours"
    option_default = 0
    option_bloody_tears = 1
    option_blue_danube = 2
    option_swamp_thing = 3
    option_white_knight = 4
    option_royal_purple = 5
    option_pink_passion = 6
    option_shadow_prince = 7
    default = 0


class CapeLining(Choice):
    """Colour of the lining of Alucard's cape."""
    display_name = "Cape lining"
    option_gold = 0
    option_bronze = 1
    option_silver = 2
    option_onyx = 3
    option_coral = 4
    alias_gold_trim = 0
    alias_bronze_trim = 1
    alias_silver_trim = 2
    alias_onyx_trim = 3
    alias_coral_trim = 4
    default = 0


class RandomColors(Toggle):
    """Random colours for Alucard's cape, the Gravity boots trail, Hydro storm, wing smash, Richter, Dracula's
    cape and Maria."""
    display_name = "Random colours"


class RelicSurprise(Toggle):
    """Every relic looks the same: you only find out which one it is when you pick it up."""
    display_name = "Relic surprise"


class RandomMusic(Toggle):
    """Each area plays a random song."""
    display_name = "Random music"


@dataclass
class SOTNOptions(PerGameCommonOptions):
    accessibility: ItemsAccessibility
    start_inventory_from_pool: StartInventoryPool
    item_pool: ItemPool
    randomize_other_items: RandomizeOtherItems
    powerful_items: PowerfulItems
    boss_drops: BossDrops
    enemysanity: Enemysanity
    enemysanity_needs_faerie_scroll: EnemysanityNeedsFaerieScroll
    caverns_back_door: CavernsBackDoor
    colosseum_back_door: ColosseumBackDoor
    open_shortcuts: OpenShortcuts
    starting_area: StartingArea
    inverted_library_card: InvertedLibraryCard
    skip_clock_tower_puzzle: SkipClockTowerPuzzle
    no_logic: NoLogic
    difficulty: Difficulty
    enemy_strength: EnemyStrength
    random_enemy_stats: RandomEnemyStats
    drop_rate: DropRate
    enemy_drops: EnemyDrops
    enemy_drops_include_hearts_and_gold: EnemyDropsIncludeHeartsAndGold
    enemy_drops_can_be_key_items: EnemyDropsCanBeKeyItems
    candle_drops: CandleDrops
    shop_stock: ShopStock
    shop_sells_library_card: ShopSellsLibraryCard
    random_shop_prices: RandomShopPrices
    random_starting_equipment: RandomStartingEquipment
    keep_starting_equipment: KeepStartingEquipment
    skip_prologue: SkipPrologue
    infinite_wing_smash: InfiniteWingSmash
    magic_vessels: MagicVessels
    no_screen_freezes: NoScreenFreezes
    fast_warp: FastWarp
    heal_at_save_rooms: HealAtSaveRooms
    death_link: DeathLink
    map_color: MapColor
    alucard_palette: AlucardPalette
    cape_lining: CapeLining
    random_colors: RandomColors
    relic_surprise: RelicSurprise
    random_music: RandomMusic


sotn_option_groups = [
    OptionGroup("Item pool", [
        ItemPool, RandomizeOtherItems, PowerfulItems, BossDrops, Enemysanity, EnemysanityNeedsFaerieScroll,
    ]),
    OptionGroup("Castle", [
        CavernsBackDoor, ColosseumBackDoor, OpenShortcuts, StartingArea, InvertedLibraryCard, SkipClockTowerPuzzle,
        NoLogic,
    ]),
    OptionGroup("Enemies and drops", [
        Difficulty, EnemyStrength, RandomEnemyStats, DropRate, EnemyDrops, EnemyDropsIncludeHeartsAndGold,
        EnemyDropsCanBeKeyItems, CandleDrops,
    ]),
    OptionGroup("Shop and equipment", [
        ShopStock, ShopSellsLibraryCard, RandomShopPrices, RandomStartingEquipment, KeepStartingEquipment,
    ]),
    OptionGroup("Quality of life", [
        SkipPrologue, InfiniteWingSmash, MagicVessels, NoScreenFreezes, FastWarp, HealAtSaveRooms, DeathLink,
    ]),
    OptionGroup("Cosmetic", [
        MapColor, AlucardPalette, CapeLining, RandomColors, RelicSurprise, RandomMusic,
    ]),
]


def enemy_drops_mode(options: SOTNOptions) -> int:
    """The enemy_drops options as Rom.randomize_drop's mode (upstream's single option): 0 off, 1-2 shuffled,
    3-6 same type, 7-10 random; +1 with hearts and gold, +2 with key items (not for shuffled)."""
    kind = options.enemy_drops.value
    if kind == EnemyDrops.option_vanilla:
        return 0
    extra = 1 if options.enemy_drops_include_hearts_and_gold.value else 0
    if kind == EnemyDrops.option_shuffled:
        return 1 + extra
    if options.enemy_drops_can_be_key_items.value:
        extra += 2
    return (3 if kind == EnemyDrops.option_same_type else 7) + extra


def shop_stock_mode(options: SOTNOptions) -> int:
    """The shop options as Rom.randomize_shop's mode (upstream's single option): 0 off, 1 random, 3 random
    without key items; +1 with the Library card."""
    stock = options.shop_stock.value
    if stock == ShopStock.option_vanilla:
        return 0
    mode = 1 if stock == ShopStock.option_random_items else 3
    return mode + (1 if options.shop_sells_library_card.value else 0)
