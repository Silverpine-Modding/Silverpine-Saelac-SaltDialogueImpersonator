# Salt Dialogue Impersonator

A standalone BepInEx plugin for Silverpine that replaces the dialogue debug
menu's original `Impersonate` behavior.

## Use

1. Start a dialogue and open its upper-button menu.
2. Select `Impersonate`.
3. Choose one of the NPCs currently participating in the conversation.
4. Type dialogue normally. Submitted lines are recorded as genuine NPC turns
   under that NPC's name, followed by an explicit system attribution for the
   LLM.
5. Select `Impersonate: NPC Name` and then `Speak as Player` to return to normal.

The selected NPC temporarily occupies the player-side portrait slot while
impersonation is active. For custom or overridden NPCs, its scale and
player-side portrait offsets come directly from the loaded SILC character
definition. Vanilla NPC offsets are converted from Silverpine's NPC-side
coordinates as a fallback. After choosing `Speak as Player`, that portrait
remains until the player submits their next line, then Silverpine's normal
player portrait is restored.

The selection automatically clears when the dialogue ends. The plugin has no
dependency on SaltExtraDebug. It registers its action through Modding Tools,
which also replaces the base game's debug-only Impersonate action without
creating a duplicate.

The selected NPC is exposed through Modding Tools' optional
`DialogueInputActors` hook. Conversation Observer can therefore accept a typed
NPC turn while the player is away without treating that turn as the player's
return.

## Credits

Created by **Saelac and ChatGPT**.
