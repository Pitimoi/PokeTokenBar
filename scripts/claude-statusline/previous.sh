#!/bin/bash
# previous.sh — the last companion whose line completed, as one Claude Code status-line segment:
#   <icon> #<species> <name>
# Prints nothing until a line has completed, or when the tray app has never written its status.
# Uses kitty image id 201.
#
# Usage from ~/.claude/statusline.sh:   PREVIOUS=$(/path/to/previous.sh)

source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
require_status

read -r ID HEX ICON NAME < <(jq -r \
  '.lastGraduated | if . == null then "-" else "\(.speciesId) \(.color // "-") \(.icon // "-") \(.name // "")" end' "$STATUS")
[ -n "$ID" ] && [ "$ID" != "-" ] || exit 0

C=$(truecolor "$HEX")
printf '%s #%s %s\n' "$(icon "$ICON" 201 "$C")" "$ID" "$NAME"
