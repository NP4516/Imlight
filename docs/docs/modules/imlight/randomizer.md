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
| `RandomizeCreatures` | `false` | Creatures in the world are swapped for others of a similar level, the same for every player. |

:::warning
`RandomizeCreatures` can make quests that ask you to defeat a specific creature impossible to finish, which is why it is off by default.
:::

## How it stays balanced

Things are only ever swapped for something in the same group:

- **Spells** keep their type (damage, healing, charm, ward, ...) and pip cost. Only spells some trainer teaches are used, so every swap is a spell players can normally learn.
- **Gear** keeps its slot, its school and its level range.
- **Treasure cards** keep their type and pip cost.
- **Creatures** keep their level range, and bosses only swap with bosses.

Within a group the swap is a shuffle, so two different spells never turn into the same spell. For a given character the same spell or drop always becomes the same thing.

## Seeds

| Setting | Default | What it does |
| ------- | ------- | ------------ |
| `Seed` | `imlight` | Any text. Changing it reshuffles everything for everyone, so set it before characters are made. |
| `SameForEveryone` | `false` | If true, every character gets the same shuffle, which suits races. If false, each character's shuffle is their own. |
| `LevelRange` | `10` | How many levels wide each gear and creature level range is. |

The admin commands `.sb learn` and `.mod additem` are never shuffled; they always give exactly the ID you type. Use `.lookup` to find IDs.
