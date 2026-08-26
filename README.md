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
5. Impersonation automatically ends after that authored NPC turn. Select
   `Impersonate` again whenever you want to author another NPC turn.

The authored turn runs through the selected NPC's normal expression query,
dialogue-success mechanics, and LLM-based NPC-function checks before the next
speaker is generated. That next speaker may be the player or another NPC, but
never the NPC who was just impersonated.

Silverpine's player-only roleplay payment parser is bypassed for impersonated
turns. Mentioning a gold payment therefore cannot check, rewrite, or decrease
the player's gold, and the authored text reaches the NPC checks unchanged.

The selected NPC temporarily occupies the player-side portrait slot while
impersonation is active. For custom or overridden NPCs, its scale and
player-side portrait offsets come directly from the loaded SILC character
definition. Vanilla NPC offsets are converted from Silverpine's NPC-side
coordinates as a fallback. After choosing `Speak as Player`, that portrait
remains until the player submits their next line, then Silverpine's normal
player portrait is restored.

If the impersonated NPC is also the current right-side speaker, the left side
keeps the real player portrait so the same NPC is never shown on both sides.
The impersonated portrait returns to the left when a different NPC speaks.

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
