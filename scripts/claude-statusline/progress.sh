#!/bin/bash
# progress.sh — the companion being raised, as one Claude Code status-line segment:
#   <icon> #<species> <10-cell progress bar> <percent>
# Prints nothing when the tray app has never written its status. Uses kitty image id 200.
#
# Usage from ~/.claude/statusline.sh:   PROGRESS=$(/path/to/progress.sh)

source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
require_status

read -r ID PCT HEX ICON < <(jq -r \
  '.current | "\(.speciesId) \((.progress * 100) | floor) \(.color // "-") \(.icon // "-")"' "$STATUS")
[ -n "$ID" ] || exit 0

C=$(truecolor "$HEX")
printf '%s #%s %b%s%b %s%%\n' "$(icon "$ICON" 200 "$C")" "$ID" "$C" "$(bar "$PCT")" "$RESET" "$PCT"
