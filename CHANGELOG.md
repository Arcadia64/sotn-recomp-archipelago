# Changelog

## 0.5.0

Test build of the Richter goal and traps. The shuffled castle from the 0.4.0 test build isn't in this one.

- New `goal` option: `defeat_richter` ends the game at Richter in the Castle Keep, for a shorter game. Saving him counts too, and the inverted castle isn't part of the seed
- New `trap_percentage` and `trap_types` options: some of your filler becomes Poison, Stone and Curse traps, which go off on you when anyone finds them. Off by default
- New `disguise_traps` option, on by default: traps show the gold progression badge until you pick them up
- When 25 or more items arrive at once, they share one popup, and key items and traps keep their own
- Works with SymphonyRecomp 1.0
- `starting_area`: sotn.io's fixes for Royal Chapel and Castle Keep starts

## 0.3.3

- Docked windows now actually come back docked after restarting the game (in 0.3.2 they came back floating together)

## 0.3.2

- Docked windows stay where you put them after restarting the game
- Right-click an item you don't have (Items) or a location (Map) to ask for a hint

## 0.3.1

- Map: your position updates right after using a warp room, instead of when you leave the room

## 0.3.0

First public test build.

- Runs on SymphonyRecomp; based on fdelduque's SotN apworld 0.8.16.1
- Places your seed while the game runs, with no patching
- AP items show as colored badges by importance
- Saves are tied to their seed and work offline
- In-game map, item tracker and text client
- DeathLink and all the original options, with clearer names
- Fixed:
  - items lost to progression balancing
  - a crash from certain enemy drops
  - missing powerful items
  - key items placed in spots where they could be missed
  - enemy stat text overlapping
  - Dracula's second form ignoring difficulty
  - some wrong enemy stats
  - starting gear that could include key items
  - start_inventory_from_pool
