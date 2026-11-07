# Prima zone sweep (2026-11-06) - monsters the Prima guides list that our world lacks

Read-only research. Nothing in the game or the database was changed by the sweep.

How: every monster the Prima Atlas (per-zone loot lists) and the Shrouded Isles guide (zone and dungeon lists) name
was compared with the live spawns of that zone (74 zones had at least one name with no live spawn: 1,204 names).
One agent per zone then checked every name against the database, the period sources and the server code.
Monsters only (NPCs left out), capital cities skipped.

## Verdicts (1,204 names)

| Verdict | Names | Meaning |
|---|---|---|
| MISSING_NEEDS_PLACEMENT | 549 | not in the world, no archived row: spots must come from period maps / sightings |
| MISSING_RESTORABLE | 18 | not in the world, archived rows exist (230 rows) - restore like Cornwall |
| PRESENT_OTHER_ZONE_ONLY_BORDER | 195 | live just across a zone border |
| PRESENT_OTHER_NAME | 138 | live under another spelling (or the guide's name is a scan typo) |
| BOSS_OR_EPIC | 90 | scripted boss / epic dungeon monster - report only (owner rule) |
| PRIMA_PARSE_ERROR | 84 | not a monster: garbled text from the guide's PDF |
| NPC_SKIP | 80 | an NPC, not a monster |
| QUEST_OR_EVENT | 50 | spawned by quest / event code, not the spawn table |

Most missing names are in Hibernia's old frontier (Mount Collory 58, Cruachan Gorge 49, Breifine 28), Lough Gur 29,
the Shrouded Isles (Dales of Devwy 27, Avalon City 25, Delling Crater 16, Gwyddneau 16) and Myrkwood 22.

## Items to research (Atlas drop items with no item on our server)

2,345 Atlas item names have no item by that name (the earlier Prima pass only added Atlas drops when we already
had the item):

- **Priority A - 418 items** dropped by monsters we would add (needed for them to drop period loot).
- **Priority B - 1,927 items** dropped only by monsters that are already live (they drop other period loot today).
- 241 one-word names are flagged in the `check` column: some are real (gems such as Obsidian, Pearl), some are pieces
  of a longer Atlas line.
- 139 of the Atlas loot groups (`<...>`, e.g. `<Pilfered Prizes>`) list items we lack (`atlas_loot_groups.csv`).

## Files

- `missing_monsters.csv` - the 567 missing monsters, per zone: Prima levels, period stats, model/template to reuse,
  Atlas drops (items we lack marked "no template"), coin, spots, evidence.
- `items_to_research.csv` - every Atlas item we lack: priority, highest drop chance, how many monsters drop it, and
  each monster with its levels, zone and chance.
- `atlas_loot_groups.csv` - the Atlas loot groups and their missing items.
- `all_zone_verdicts.csv` - all 1,204 names with their verdicts and evidence.

## Planned batch (waits for the owner's go-ahead)

1. Restore the 18 restorable monsters from their archived rows (230 rows), levels at the Prima range - as Cornwall.
2. Place the 549 others from period positions (CapnBry radar clusters, Uthgard map dots, Kirstena maps) as the
   2026-10-31 new-spawns pass did, with the Prima stats.
3. Build the Priority A items from period item pages (Allakhazam <= 2003-11, Uthgard), as the coin-only pass did,
   and give the new monsters their Atlas drop tables; coin from the server's level formula / coin165.
4. The wolf drop stack fix: wolf fang / torn wolf pelt / chunk of wolf meat PackSize 1, and the 208 existing
   "(20)/(100)" items set to 1 in the same change.
5. Priority B items afterwards (optional, large).
