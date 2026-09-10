#!/bin/bash
# common.sh — shared by progress.sh and previous.sh; source, do not run.
#
# Reads status.json written by PokeTokenBar.Tray and provides the rendering helpers. The icon is
# the real sprite in kitty and a coloured ● elsewhere. Claude Code redraws the status line as
# text, which wipes ordinary inline images; kitty's Unicode placeholders survive that: the PNG
# is sent once to the terminal (invisibly, through the nearest ancestor that owns the tty — the
# hook process itself has none), then shown via placeholder characters whose foreground colour
# carries the image id. Two cells, one row.
#
# Output contains ANSI escapes and, in kitty, combining marks; strip both before measuring width.

# Where PokeTokenBar.Tray writes: LocalApplicationData on each platform.
if [ -n "$LOCALAPPDATA" ]; then
  STATUS="$LOCALAPPDATA/PokeTokenBar/status.json"
elif [ "$(uname -s 2>/dev/null)" = "Darwin" ]; then
  STATUS="$HOME/Library/Application Support/PokeTokenBar/status.json"
else
  STATUS="${XDG_DATA_HOME:-$HOME/.local/share}/PokeTokenBar/status.json"
fi
RESET='\033[0m'

# Exits the caller quietly when the tray app has never written its status.
require_status() { [ -r "$STATUS" ] || exit 0; }

bar() { # $1 = percent 0..100 -> 10-cell bar
  local filled=$(($1 / 10)) empty; empty=$((10 - filled))
  local fill pad; printf -v fill "%${filled}s"; printf -v pad "%${empty}s"
  printf '%s' "${fill// /█}${pad// /░}"
}

truecolor() { # "#RRGGBB" -> ANSI foreground escape (empty when no colour)
  [ "${#1}" -eq 7 ] || return 0
  printf '\033[38;2;%d;%d;%dm' "0x${1:1:2}" "0x${1:3:2}" "0x${1:5:2}"
}

find_tty() {
  local p=$$ t i
  for i in 1 2 3 4 5 6 7 8; do
    p=$(sed 's/.*) //' "/proc/$p/stat" 2>/dev/null | cut -d' ' -f2) || return 1
    [ -n "$p" ] && [ "$p" -gt 1 ] || return 1
    t=$(readlink "/proc/$p/fd/1" 2>/dev/null)
    case $t in /dev/pts/*|/dev/tty[0-9]*) echo "$t"; return 0;; esac
  done
  return 1
}

# Terminals known to implement kitty graphics *with Unicode placeholders*. The terminal cannot be
# asked (the hook has no tty to read a reply from), so this is by environment only.
supports_placeholders() {
  [ "$TERM" = "xterm-kitty" ] || [ -n "$KITTY_WINDOW_ID" ] && return 0
  [ "$TERM" = "xterm-ghostty" ] || [ "$TERM_PROGRAM" = "ghostty" ] && return 0
  return 1
}

kitty_icon() { # $1 png path, $2 image id (1..255) -> placeholder cells on stdout; fails when not possible
  [ -n "$1" ] && [ "$1" != "-" ] && [ -r "$1" ] || return 1
  supports_placeholders || return 1
  local tty data chunk more first=1
  tty=$(find_tty) || return 1
  data=$(base64 -w0 "$1") || return 1
  {
    while [ -n "$data" ]; do
      chunk=${data:0:4096}; data=${data:4096}; more=0; [ -n "$data" ] && more=1
      if [ $first -eq 1 ]; then
        printf '\e_Ga=T,U=1,i=%d,f=100,q=2,c=2,r=1,m=%d;%s\e\\' "$2" "$more" "$chunk"; first=0
      else
        printf '\e_Gm=%d;%s\e\\' "$more" "$chunk"
      fi
    done
  } > "$tty" 2>/dev/null || return 1
  # U+10EEEE placeholder; diacritics = row 0, then column 0 / column 1.
  printf '\e[38;5;%dm\U0010EEEE̅̅\U0010EEEE̅̍\e[39m' "$2"
}

icon() { # $1 png path, $2 image id, $3 colour escape -> sprite in kitty, coloured dot elsewhere
  kitty_icon "$1" "$2" || printf '%b●%b' "$3" "$RESET"
}
