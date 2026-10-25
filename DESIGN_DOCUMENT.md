# Training & Squad Autobattler Gacha - Game Design Document (GDD)

---

## 1. Executive Summary & Vision

### 1.1 High Concept
A cross-era hero collector and tactical squad auto-battler built around a deep roguelite **Training Mode** (inspired by *Umamusume*). Players pull Characters and Support Cards, train their roster through multi-turn story/stat events bolstered by Support Cards, and deploy finished Veteran builds into PvE autobattles against eldritch abominations and monstrous threats.

### 1.2 Creative Universe
- **Timeless & Genre-Defying**: The setting merges historical, fantastical, and futuristic eras without fixed boundaries.
- **Eclectic Cast Archetypes**: Sword duelists, arcane spellweavers, western gunslingers, pirate corsairs, cybernetic paladins, and tactical modern marksmen coexist within the same world.
- **Flavorful Naming**:
  - **Characters**: Tactical, evocative codenames/titles (e.g., *Lonesome Sniper*, *Frontline Vanguard*, *Celestial Spellweaver*, *Highnoon Gunslinger*, *Dread Corsair*).
  - **Support Cards (SC)**: Tactical scenarios, tactical doctrine, and event cards (e.g., *Pre-emptive Ambush*, *Tactical Reconnaissance*, *Forbidden Grimoire Study*, *Momentum Unleashed*).
- **The Adversaries**: PvE campaigns and challenge raids pit player teams against horrifying abominations, corrupted beasts, and monstrous colossal titans.

### 1.3 Core Pillars
1. **Dual Gacha Banners**: Dedicated banner pulls for **Characters** and **Support Cards (SC)**.
2. **Deep Roguelite Training Mode**: Characters run through event-driven training cycles where equipped Support Cards grant unique event triggers, skill acquisitions, and permanent stat boosts.
3. **Character Star Ascensions (1★ to 5★)**: Base characters are pulled at 1★, 2★, or 3★ and can all be upgraded up to 5★ using duplicate pulls / character shards.
4. **Support Card Level Progression**: Support cards do not ascend stars, but possess levels starting at Lv. 1 up to maximum level caps (Lv. 30 for Common/Tier 1, Lv. 40 for Rare/Tier 2, Lv. 50 for Super Rare/Tier 3) upgradable via in-game currencies.
5. **Squad PvE Auto-Battler**: Trained characters lock in final stat scores and skills to form synergistic squads combating monstrous bosses and wave-based threats.
6. **Backend-Authoritative RNG & State**: Secure server-side validation for banner selections, drop probabilities, and pity offsets.

---

## 2. Core Game Loops

### 2.1 Primary Game Loop
```
[Dual Gacha Pulls (Characters & SC)] 
   ➔ [Ascend Characters via Duplicates (up to 5★) / Level Up Support Cards (up to Lv.30/40/50)]
   ➔ [Equip Support Card Deck & Select Trainee Character] 
   ➔ [Enter Training Mode: Events, Stat Choices, Skill Unlocks] 
   ➔ [Graduate Character with Final Rating & Build] 
   ➔ [Form Squad & Battle in PvE Auto-Battler vs. Abominations] 
   ➔ [Earn Gacha Tokens & Training / Leveling Currencies]
```

### 2.2 Dual Banner System
- **Character Banner (`target=character`)**: Unlocks base characters for initiation into Training Mode.
- **Support Card Banner (`target=support_card`)**: Unlocks Support Cards with passive skill arrays and training event triggers.

---

## 3. Entity & Item Architecture

### 3.1 Tier Scoring Representation (1-based Integer)
All catalog entries and gacha pull returns use an integer `tier` value (`1`, `2`, `3`):
- `tier: 1` ➔ Common / 1-Star (`1★`)
- `tier: 2` ➔ Rare / 2-Star (`2★`)
- `tier: 3` ➔ Super Rare / 3-Star (`3★`)

*Note: The display naming (Stars `1★`-`5★` or Rarity tags `C`/`R`/`SR`) is handled on the Frontend based on the item type and integer tier.*

### 3.2 Characters
- Base entity required to enter Training Mode.
- Initial pull tier is `1` (1★), `2` (2★), or `3` (3★).
- **Ascension System**: All characters can be upgraded up to **5★** (5 Stars) by spending duplicate pulls / shards of that character (akin to *Umamusume*).
- Base schema contains `base_traits` (see 3.5), `growth` bonuses, positional `affinity` and a unique `gear`.
- **Example Data Contract**:
  ```json
  {
    "id": "char_001",
    "name": "Lonesome Sniper",
    "tier": 1,
    "data": {
      "base_traits": { "power": 90, "agility": 40, "impact": 100, "defense": 50, "finesse": 70 },
      "growth": { "power": 10, "agility": -20, "impact": 20, "defense": -20, "finesse": 0 },
      "affinity": { "front": "F", "close": "E", "mid": "C", "back": "A" },
      "gear": { "id": "gear_001", "name": "Longshot Rifle", "trait": "impact", "effect": "penetration_damage", "value": 25 }
    }
  }
  ```

### 3.3 Support Cards (SC)
- Cards equipped into Support Deck slots before commencing a Training Run.
- Not star-upgradeable, but feature **Level Progression** (starting at Lv. 1):
  - **Tier 1 (Common / C)**: Max Level **30**
  - **Tier 2 (Rare / R)**: Max Level **40**
  - **Tier 3 (Super Rare / SR)**: Max Level **50**
- Levels are upgraded outside the gacha reward catalog using in-game currencies.
- Provide training event opportunities, stat gains, and teachable battle skills.
- **Example Data Contract**:
  ```json
  {
    "id": "sc_001",
    "name": "Pre-emptive Ambush",
    "tier": 1,
    "data": {
      "skills": [
        "First Strike Training"
      ]
    }
  }
  ```

### 3.4 Rarity Tiers & Pull Odds (Identical for Both Banners)
- **Tier 1 (Common / 1★ / C)**: 79% base pull rate (Pulls 1–9).
- **Tier 2 (Rare / 2★ / R)**: 18% base pull rate (Pulls 1–9).
- **Tier 3 (Super Rare / 3★ / SR)**: 3% base pull rate (Pulls 1–9).
- **Pity & 10th Pull Guarantees**:
  - Pull 10 guarantees at least `Tier 2` (Rare / 2★) or `Tier 3` (Super Rare / 3★).
  - `sr_offset`: Dynamic 10-pack cycle pity guarantee ensuring at least one `Tier 3` (Super Rare / 3★).

### 3.5 Traits (Trainable Stats & PvE Math Basis)
Five traits are improved in Training Mode (akin to *Umamusume* stats) and drive all PvE math:
| Trait | Role in PvE | Baseline formula (`TraitMath`) |
|---|---|---|
| **Power** | Raw damage base value. Maxed by most characters. | `damage = power` |
| **Agility** | Attack speed / fire rate. | `attacks/s = 0.5 + agility / 200` |
| **Impact** | Penetration of enemy armor. | `penetration = impact / 2` |
| **Defense** | Health and armor (damage reduction). Prioritized by tanks. | `maxHP = 100 + 2·defense`, `armor = defense / 2` |
| **Finesse** | Likelihood to trigger skills and to dodge damage. | `skill chance = finesse / 1000`, `dodge = min(finesse / 2000, 50%)` |

- Damage reduction: `effectiveArmor = max(0, armor − penetration)`, `reduction = effectiveArmor / (effectiveArmor + 100)`.
- *All formulas are placeholders to be tuned.*

### 3.6 Trait Growth Bonuses
- Every character has a per-trait `growth` modifier in percent (e.g. `+20`, `-20`) applied to all training gains: `gain × (1 + growth / 100)`.
- Example: **Lonesome Sniper** — Power `+10%`, Impact `+20%` (greatly improved), Agility `-20%` and Defense `-20%` (greatly reduced).
- This limits growth in off-role traits, so players are automatically rewarded for building the character correctly.
- Range is `-20%` to `+20%`. Growth does **not** scale with tier (star rank).
- Growth is not meant to be balanced across characters; it expresses the character's fantasy.

### 3.7 Unique Gear
- Each character owns one unique gear piece (weapon or similar) that boosts an effect tied to one trait (`trait`, `effect`, `value` in percent), further rewarding the correct build.
- Example: **Longshot Rifle** (Sniper) — `+25%` bonus to penetrated damage from the Impact calculation.
- Current effects: `damage`, `attack_speed`, `penetration_damage`, `armor`, `skill_trigger`, `dodge`, `counter_damage`.

### 3.8 Positional Affinity
- Similar to *Umamusume* running strategies (Front Runner, etc.). Four range steps (JSON key in brackets):
  1. **Front Line** (`front`): tanks and melee.
  2. **Close Range** (`close`): melee with longer reach, short-ranged weapons like shotguns, short-ranged magic.
  3. **Mid Range** (`mid`): classic rifle range and most magic. No melee (unless a special type).
  4. **Back Line** (`back`): only special long-range characters such as snipers, possibly very long-range magic, and supporters (e.g. healers) or other special characters that target allies rather than enemies.
- Each position has a grade: `S`, `A`, `B`, `C`, `D`, `E`, `F`.
- Default rules:
  - At least one of the four positions is always `A`.
  - `S` is never a default grade; it can only be reached through play.
- Progression limits (based on the starting grade):
  - `D`: can only reach `A`/`S` with very deep commitment.
  - `E`/`F`: can never reach `A` or `S` (capped at `B`).
- Example: **Lonesome Sniper** — Front `F`, Close `E`, Mid `C`, Back `A`.
- Affinity will scale combat effectiveness when a character is deployed in a given squad position (multipliers TBD).

---

## 4. Systems Breakdown

### 4.1 Training Mode (Core Progression)
- Select 1 Trainee Character + a team of Support Cards.
- Multi-turn progression:
  - Fixed run length of **36 rounds** (3 years, 12 months/rounds each).
  - Max 1 simultaneous active training run per user stored in MongoDB `training_runs`.
  - **Energy System (0–100 Base Energy, can scale with events/SCs)**:
    - Physical facility training (Power, Agility, Impact, Defense) consumes `15 Energy points`.
    - **Finesse Facility Training**: Does not cost energy; instead **restores 10% of max energy in points** (e.g. `+10 Energy` for 100 max energy).
    - **Rest Action**: Recovers `+50 Energy points` (cannot rest if energy is already at max energy).
    - **Failure Rate Mechanics**: Starts at the **50% Energy mark** (when current energy < 50% of max energy). Increases by `2% for every 1% below 50%` (e.g. at 40% energy = 20% fail rate, 0 energy = 100% fail rate).
    - If training fails, energy is consumed (or no energy restored for finesse), 0 stats are gained, and no other penalties are applied.
  - **5 Facilities & Stat Distribution**:
    - Stats are capped at `1000` max per trait.
    - Each facility trains its primary trait and also a secondary trait at `30%` of base value:
      - **Power** -> secondary **Impact**
      - **Agility** -> secondary **Defense**
      - **Impact** -> secondary **Agility**
      - **Defense** -> secondary **Finesse**
      - **Finesse** -> secondary **Power**
    - If the main stat is capped, secondary stat still gains independently.
    - Base Gains: Level 1 starts at `10 pts` main (30% secondary), and each level adds `+2 pts` (L1: 10, L2: 12, L3: 14, L4: 16, L5: 18).
    - Character growth percentages scale the gains: `gain × (1 + growth / 100)`.
  - **Facility Level Progression**:
    - Facility levels range from 1 to 5.
    - Every 3 uses of a facility automatically increases its level by 1.
  - **Support Cards & Rainbow Training**:
    - Each Support Card has an assigned facility type (`power`, `agility`, `impact`, `defense`, `finesse`).
    - Support Cards randomly distribute across facilities on each turn.
    - Support Cards start with `20%` bond gauge and gain `+15%` bond whenever the player trains at their facility (only cards sitting at the selected facility gain bond).
    - **Rainbow Training**: Triggers only when a card's bond is `≥ 80%` and the card is currently sitting at its matching facility. Boosts training yield by `+10%` (Tier 1 / C), `+20%` (Tier 2 / R), or `+30%` (Tier 3 / SR).
  - **Two-Click Action Confirmation & Live Preview**:
    - In frontend UI, first click on any facility or Rest button previews stat gains, energy change (cost/restore), failure chance, and list of support cards currently sitting at that facility.
    - Second click on the same button executes the action via backend API.
- Upon round 36 completion, the run completes and trainee is prepared for graduation.

### 4.2 PvE Auto-Battler (Combat)
- Squad selection: Assemble up to 3–5 trained characters into a battle line, assigning each to Front Line, Close Range, Mid Range or Back Line according to positional affinity.
- Automated combat resolution: Characters use normal attacks and trigger unlocked skills based on cooldowns, conditions, and tactical priority against abominations.

---

## 5. Technical Architecture & Integration (FE <-> BE)

### 5.1 Backend (Python / Flask / MongoDB)
- **API Endpoints**:
  - `GET /`: Health check.
  - `GET /connect?player_id={id}`: Player handshake and profile load.
  - `GET /player-stats?player_id={id}`: Player token balance and pack stats.
  - `GET /gacha-pack?player_id={id}&target={character|support_card}`: Executes 10-pull pack on target banner returning integer tiers (`tier: 1..3`) and debits tokens.
  - `GET /training/start-or-resume?player_id={id}&character_id={id}&support_card_ids={id1,id2,...}`: Returns active training run or initializes a new one.
  - `GET /training/state?player_id={id}`: Fetches current run state.
  - `GET /training/train?player_id={id}&facility={power|agility|impact|defense|finesse}`: Executes training action on the selected facility.
  - `GET /training/rest?player_id={id}`: Executes rest action to recover energy.
- **Data Stores**:
  - `data/characters.json`: Character catalog definitions with integer `tier` (`1`, `2`, `3`).
  - `data/support_cards.json`: Support card catalog definitions with integer `tier` (`1`, `2`, `3`) and assigned `facility`.
  - MongoDB `users`: Player profiles and tokens.
  - MongoDB `training_runs`: Active training run states.

- **Response Contract**:
  - Every pulled reward carries `type` (`character` | `support_card`) and `awarded_at`.
  - `/connect`, `/player-stats` and `/gacha-pack` all return the same `stats` object (`name`, `tokens`, `packs_opened`, `sr_offset`).

### 5.2 Frontend (Unity C#)
- **Data Structures** (`GachaData.cs`, `PlayerData.cs`):
  - `Tiers`: tier constants (`Common`/`Rare`/`SuperRare` = 1/2/3), `Clamp()` and `ShortName()` (`C`/`R`/`SR`).
  - `GachaReward`: wire format of a pulled item (`id`, `name`, `tier`, `type`, `data`). Provides `IsSupportCard`, `ToCharacter()`, `ToSupportCard()` and the display `Label`.
  - `CharacterData`: `id`, `name`, `tier` (1–5 stars), `traits`, `growth`, `affinity`, `gear`; `StarsDisplay` (`"1★"`…`"5★"`).
  - `Traits` (power/agility/impact/defense/finesse), `PositionAffinity` (front/close/mid/back, `Rank()`, `Grade()`, `Ceiling()`, `IsValidDefault()`), `GearData`, `TraitMath` (baseline PvE & growth formulas).
  - `SupportCardData`: `id`, `name`, `tier` (1–3), `level`, `skills[]`; `MaxLevel` (30/40/50).
  - `PlayerData`: `playerId`, server `stats`, and the rosters of acquired characters and support cards.
- **Scene Singletons** (`Singleton<T>`, registered in `Awake`, use `X.Instance` from `Start` or later — never link them in the Inspector):
  - `GameClient`: login, stats refresh and pack pulls (`Pull(banner)`); events `OnLoginSuccess`, `OnPullStarted`, `OnPullFinished`.
  - `GachaItemSpawner`: presents rewards (`Line` or `SingleItemOnClick` with anticipation reveal, pull indicators and Skip Commons); events `OnSequenceStarted`, `OnSequenceEnded`.
- **Other Components**:
  - `GachaPullButton`: one per banner (`targetGacha`), shown only while logged in and no reveal sequence is running.
  - `RotateScript`: mouse-drag rotation of a reward model.

---

## 6. Brain Dump & Roadmap Sandbox

- **Idea 1 - Support Card Friendship / Bond Level**: Reaching max bond with a support card during training unlocks a rainbow training event with guaranteed Super Rare skill inheritance.
- **Idea 2 - Duplicate Shard Converter**: Pulling duplicates of a character converts into Star Shards to ascend the character from 1★ up to 5★.
- **Idea 3 - Abomination Factions & Weaknesses**: Abominations classified into Eldritch, Necrotic, and Behemoth with specific skill counter-mechanics.
- **Idea 4 - Inherited Traits / Legacies**: Graduated characters can act as mentors/ancestors for future trainees, passing down unique passive traits.
- **Idea 5 - Asynchronous Arena (PvP)**: Pitching graduated squads against other players' trained teams in automated tactical clashes.
