#!/bin/bash
# progress.sh — the companion being raised, as one Claude Code status-line segment:
#   <icon> #<species> <10-cell progress bar> <percent>
# The percent becomes 🍓 when enough is banked to feed the companion.
# While eggs are on offer instead (nothing hatched yet):
#   🥚 <eggs on offer> · <budget available> / <hatch price>
# Prints nothing when the tray app has never written its status. Uses kitty image id 200.
#
# Usage from ~/.claude/statusline.sh:   PROGRESS=$(/path/to/progress.sh)

source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
require_status

read -r ID PCT HEX ICON FEED < <(jq -r \
  '(.current | if . == null then "-" else "\(.speciesId) \((.progress * 100) | floor) \(.color // "-") \(.icon // "-")" end) + " \(.budget.canAdvance // false)"' "$STATUS")

if [ "$ID" = "-" ]; then
  read -r OFFER AVAILABLE PRICE < <(jq -r '.budget | "\(.offerCount) \(.available) \(.hatchPrice)"' "$STATUS")
  [ -n "$OFFER" ] || exit 0
  printf '🥚 ×%s · %s / %s\n' "$OFFER" "$(compact "$AVAILABLE")" "$(compact "$PRICE")"
  exit 0
fi

[ -n "$ID" ] || exit 0

C=$(truecolor "$HEX")
if [ "$FEED" = "true" ]; then TAIL="🍓"; else TAIL="${PCT}%"; fi
printf '%s #%s %b%s%b %s\n' "$(icon "$ICON" 200 "$C")" "$ID" "$C" "$(bar "$PCT")" "$RESET" "$TAIL"
