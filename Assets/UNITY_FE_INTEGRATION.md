# Unity FE Integration

## Player data

All endpoints use `GET` requests and accept `player_id` as a query parameter.

### `GET /connect?player_id=test`

Returns the existing connection response:

```json
{
  "success": true,
  "player_id": "test",
  "userdata": {
    "tokens": 100,
    "packs_opened": 0,
    "sr_offset": null
  }
}
```

The `/connect` response keeps the existing shape. `name` is available from `/player-stats`; it is read from the user's top-level MongoDB `name` field. If no name is stored, the backend uses `player_id` as the display fallback. `tokens` is the current balance. `packs_opened` is the number of successful pack opens. `sr_offset` is the player's hidden guaranteed-super-rare pack position and should not be displayed.

### `GET /player-stats?player_id=test`

Use this endpoint to refresh the player's current stats after any action:

```json
{
  "success": true,
  "player_id": "test",
  "stats": {
    "name": "test",
    "tokens": 9100,
    "packs_opened": 1,
    "sr_offset": 7
  }
}
```

The frontend should display `stats.name`, `stats.tokens`, and optionally `stats.packs_opened`. Treat `sr_offset` as backend state; do not show it to the player.

## Opening a pack

### `GET /gacha-pack?player_id=test`

Each pack costs **900 tokens**. The backend checks and debits the balance in the same MongoDB update. A request is denied when the balance is below 900, and no tokens or pack counters are changed.

Successful response:

```json
{
  "success": true,
  "player_id": "test",
  "rewards": [
    {
      "id": "R1",
      "name": "Rookie",
      "tier": "rare",
      "type": "pilot",
      "data": { "base_traits": { "power": 90, "agility": 40, "impact": 100, "defense": 50, "finesse": 70 }, "growth": { "power": 10, "agility": -20, "impact": 20, "defense": -20, "finesse": 0 }, "affinity": { "front": "F", "close": "E", "mid": "C", "back": "A" }, "gear": { "id": "gear_001", "name": "Longshot Rifle", "trait": "impact", "effect": "penetration_damage", "value": 25 } },
      "awarded_at": "2026-09-16T12:00:00.000000"
    }
  ],
  "stats": {
    "name": "test",
    "tokens": 8200,
    "packs_opened": 1,
    "ssr_offset": 7,
    "sr_offset": 7
  }
}
```

The `rewards` array contains 10 items in a real response. Use the returned `stats` object immediately to update the token display; a separate stats request is not required after a successful pack open.

Insufficient-balance response:

```json
{
  "success": false,
  "message": "insufficient tokens",
  "required_tokens": 900
}
```

This response uses HTTP status `403`. Keep the current player stats on screen and show an insufficient-token message. Other failures use the existing `success: false` response format.

## Recommended Unity flow

1. Call `/connect?player_id=...` when the player enters the game.
2. Store and display the returned name and token balance.
3. Before opening, optionally disable the button when the displayed balance is below 900.
4. Call `/gacha-pack?player_id=...` when the player opens a pack.
5. On HTTP `200`, display the 10 rewards and replace the displayed stats with the response's `stats` object.
6. On HTTP `403`, do not modify the local balance; show that 900 tokens are required.
7. Call `/player-stats?player_id=...` whenever the UI needs to refresh the persisted player state.

## Training selection

When no active training run exists, call `/player-characters?player_id=...` to populate the character grid. The response's `characters` array contains only characters owned by that player. Show each character as an image loaded from `Resources/Images/Characters/<character_id>`; a missing resource resolves to `null`.

Use `/player-support-card-presets?player_id=...&preset_id=default` to read the saved `character_id` and preselect it in the grid. The selected character is highlighted with `SpriteGlowOutline.shader`. Clicking a different character moves the highlight; the Continue button accepts and saves the selection before opening the support-card selector:

```json
{
  "preset_id": "default",
  "character_id": "char_001"
}
```

This character selection is stored in the same preset document as `support_card_ids`; omitting `support_card_ids` leaves the saved cards unchanged. After the support-card selection is completed, start training with the chosen `character_id` and selected card IDs. If a training run is already active, skip both selectors and resume it.
