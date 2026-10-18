# Randomizer Mode

Randomizer mode shuffles the game so every playthrough is different. It is off by default and is switched on in the `[Randomizer]` section of `Imlight.ini`.

```ini
[Randomizer]
Enabled = true
```

## What gets shuffled

| Setting | Default | What it does |
| ------- | ------- | ------------ |
| `RandomizeSchool` | `true` | New characters get a random school instead of the one they picked. |
| `RandomizeSpells` | `true` | Spells learned from trainers, quests and drops are swapped. Trainers show the swapped spell, including its prerequisite. |
| `RandomizeDrops` | `true` | Gear and treasure cards from drops and quest rewards are swapped. Quest items, keys and housing are never swapped. |
| `RandomizeCreatures` | `false` | Creatures in the world are swapped for others of a similar level. |
| `CreaturesChangeEverySpawn` | `true` | With `RandomizeCreatures` on, every spawn rolls a new creature. If false, each creature always swaps to the same one, for every player. |

## Battles

| Setting | Default | What it does |
| ------- | ------- | ------------ |
| `RandomizeCreatureDecks` | `true` | Each creature's deck is rolled fresh every spawn: every spell becomes a random spell of the same type and pip cost, from any school. |
| `RandomizeItemCards` | `true` | Cards your gear gives are rolled fresh every battle, the same way. |
| `RandomizePips` | `true` | Each round gives a random number of pips: 1 about half the time, otherwise 0, 2 or (rarely) 3. Creatures too. |
| `StartingSpells` | `6` | New characters start with this many random spells costing up to 3 pips, from any school. The tutorial still teaches its spell too. |
| `SpellAfterBattleChance` | `100` | The chance (0 to 100) that a win teaches a random spell. Any battle spell in the game files can come up, including ones players normally never get. |

These rolls are truly random and differ every time. They use every battle spell in the game files (treasure cards, gardening, fishing and cantrips are left out), not just trainer spells.

:::warning
`RandomizeCreatures` can make quests that ask you to defeat a specific creature impossible to finish, which is why it is off by default.
:::

## How it stays balanced

Things are only ever swapped for something in the same group:

- **Spells** keep their type (damage, healing, charm, ward, ...) and pip cost. Only spells some trainer teaches are used, so every swap is a spell players can normally learn.
- **Gear** keeps its slot, its school and its level range.
- **Treasure cards** keep their type and pip cost.
- **Creatures** keep their level range, and bosses only swap with bosses.

Within a group the swap is a shuffle, so two different spells never turn into the same spell. For a given character the same spell or drop always becomes the same thing. The battle rolls above are the exception: they are random every time.

## Seeds

| Setting | Default | What it does |
| ------- | ------- | ------------ |
| `Seed` | `imlight` | Any text. Changing it reshuffles everything for everyone, so set it before characters are made. |
| `SameForEveryone` | `false` | If true, every character gets the same shuffle, which suits races. If false, each character's shuffle is their own. |
| `LevelRange` | `10` | How many levels wide each gear and creature level range is. |

The admin commands `.sb learn` and `.mod additem` are never shuffled; they always give exactly the ID you type. Use `.lookup` to find IDs.
