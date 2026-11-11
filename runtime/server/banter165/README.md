# banter165 - bot banter lines (owner 2026-11-10)

The server reads every `*.json` here once at start (files starting with `_` are skipped) from
`runtime\server\banter165\` (copy this folder there on deploy). GM `/banter reload` re-reads it, `/banter stats` counts it.
A missing folder or a bad line never breaks chat: bad lines are skipped and logged (`BANTER165`), and the bots fall back
to their built-in lines.

Rules live in `GameServer/bots/autonomous/BanterLibrary165.cs` and `tools/validate_banter165.py` (keep them in step).
Check before handing in:

    python -I tools\validate_banter165.py banter165 --stats

## File format

    {
      "category": "<file name without .json>",
      "description": "what this file covers",
      "defaults": { "channel": "guild" },          // optional, any line key except text
      "lines": [
        { "text": "Rams to the gate at {keep}!", "situation": "rvr_siege" },
        { "text": "On my way to {keep}.", "situation": "rvr_siege", "kind": "reply", "channel": ["realm", "guild"],
          "realm": "Midgard", "level": [20, 50], "classType": ["tank", "melee"], "weight": 2 }
      ]
    }

(JSON has no comments: the `//` above is only for this page.)

- `situation` (required): one key or a list; reply_* keys cannot mix with others.
- `channel`: realm | guild | say | group (group = reply_* only). Omitted = any.
- `realm`: Albion | Midgard | Hibernia. `level`: [min, max] 1-50. `classType`: tank melee healer support caster pet stealth archer.
- `kind`: open (default: starts a conversation or stands alone) | reply (answers another bot's line in the same situation).
  Not used on reply_*, chat_close, guild_join.
- `weight`: above 0, at most 10 (default 1).
- `only` (optional): the line is said only when the speaker's own value is one of the names given, for example
  `"only": { "zone": ["Spindelhalla"] }` or `"only": { "class": "Skald", "target": ["Pywrtr"] }`. It tests the same
  values the placeholders fill: zone, class, target, boss, raid, keep, bg, destination (a key must be one the line's
  situation can fill). Names are compared without case. Use it for a line that is true of one place, monster, boss or
  class and wrong of any other.
- `text`: 4-160 printable ASCII characters, no double quotes, no double spaces, no curly quotes or dashes; placeholders
  in braces from the allowed list for that situation. A line whose placeholder has no value at that moment is not said.
