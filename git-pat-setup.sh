#!/usr/bin/env bash
# Repo-scopet PAT via Git Credential Manager, avgrenset til klonemappa.
#
#   git-pat-setup.sh <owner>/<repo> <klonemappe>     sett opp (kjør FØR git clone)
#   git-pat-setup.sh --check <owner>/<repo> [mappe]  vis oppsett og test oppslag
#
# <owner>/<repo> kan også være https://github.com/<owner>/<repo>(.git).
# Oppsettet:
#   ~/.gitconfig                 credential.<url>.helper = ""  (slår av system-GCM og gh for repoet)
#                                includeIf "gitdir/i:<mappe>/" -> ~/.gitconfig.d/<owner>-<repo>.gitconfig
#   ~/.gitconfig.d/<...>.gitconfig  credential.<url>.helper = "", manager
# Utenfor <mappe> har repoet altså ingen helper. Tokenet vises aldri: --check maskerer det.
# Ligger tokenet allerede i GCM og <mappe> er ny, må brukeren godkjenne det i en dialog (default Nei).
set -euo pipefail

die() { echo "feil: $*" >&2; exit 1; }

usage() {
  sed -n '2,6p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
}

parse_repo() {
  local r="${1%/}"
  r="${r#https://github.com/}"
  r="${r%.git}"
  [[ "$r" =~ ^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$ ]] || die "ugyldig repo '$1', forventet <owner>/<repo>"
  OWNER="${r%%/*}"
  REPO="${r#*/}"
  URL="https://github.com/$OWNER/$REPO.git"
  U="credential.$URL"
  INC_NAME="$OWNER-$REPO.gitconfig"
  INC_FILE="$HOME/.gitconfig.d/$INC_NAME"
}

normalize_dir() {
  local d="$1"
  if command -v cygpath >/dev/null 2>&1; then d="$(cygpath -m -a "$d")"; fi
  d="${d%/}"
  [[ "$d" =~ ^[A-Za-z]:/ ]] || die "mappa må være en absolutt Windows-sti, fikk '$1'"
  echo "$d"
}

scopes() {
  git config --global --get-regexp '^includeif\..*\.path$' 2>/dev/null \
    | awk -v f="$INC_NAME" '$2 ~ f"$" { k=$1; sub(/^includeif\.gitdir(\/i)?:/, "", k); sub(/\.path$/, "", k); print k }'
}

# Slår opp credentials fra en mappe og viser bare brukernavnet eller feilen.
lookup() {
  local dir="$1" out
  out="$(printf 'protocol=https\nhost=github.com\npath=%s/%s.git\n\n' "$OWNER" "$REPO" \
    | GIT_TERMINAL_PROMPT=0 GCM_INTERACTIVE=never git -C "$dir" credential fill 2>&1)" || true
  if grep -q '^password=' <<<"$out"; then
    echo "token funnet (bruker $(sed -n 's/^username=//p' <<<"$out"))"
  else
    echo "ingen token ($(tail -n1 <<<"$out"))"
  fi
}

# Spør GCM direkte (uavhengig av scope) om repoet har et lagret token. Skriver brukernavnet.
stored_user() {
  local tmp out; tmp="$(mktemp -d)"
  out="$(printf 'protocol=https\nhost=github.com\npath=%s/%s.git\n\n' "$OWNER" "$REPO" \
    | GIT_TERMINAL_PROMPT=0 GCM_INTERACTIVE=never git -C "$tmp" \
        -c "$U.helper=" -c "$U.helper=manager" -c "$U.useHttpPath=true" credential fill 2>/dev/null)" || true
  rmdir "$tmp"
  grep -q '^password=' <<<"$out" && sed -n 's/^username=//p' <<<"$out"
}

# Ja/nei fra brukeren selv: Windows-dialog (default Nei), ellers /dev/tty. Uten noen av dem: nei.
confirm() {
  if command -v powershell.exe >/dev/null 2>&1; then
    local ans
    ans="$(PAT_MSG="$1" powershell.exe -NoProfile -NonInteractive -Command '
      Add-Type -AssemblyName System.Windows.Forms
      $f = New-Object System.Windows.Forms.Form -Property @{ TopMost = $true }
      [System.Windows.Forms.MessageBox]::Show($f, $env:PAT_MSG, "git-pat-setup", "YesNo", "Warning", "Button2")' \
      | tr -d '\r')"
    [[ "$ans" == "Yes" ]]
  elif { exec 3<>/dev/tty; } 2>/dev/null; then
    local ans; printf '%s\nGi tilgang? [j/N] ' "$1" >&3; read -r ans <&3; exec 3>&-
    [[ "$ans" =~ ^[jJyY]$ ]]
  else
    return 1
  fi
}

# Ligger tokenet allerede i GCM og mappa er ny, må brukeren godkjenne at den får tilgang til det.
guard_new_path() {
  local dir="$1" existing user
  existing="$(scopes)"
  grep -qixF "$dir/" <<<"$existing" && return 0
  user="$(stored_user)" || return 0
  confirm "Tokenet for $OWNER/$REPO (bruker $user) ligger allerede i Git Credential Manager.

Det brukes i dag under:
${existing:-(ingen mapper)}

Ny mappe som vil få tilgang til det samme tokenet:
$dir/

Vil du gi denne mappa tilgang?" \
    || die "avbrutt: $dir fikk ikke tilgang til tokenet for $OWNER/$REPO (ingenting er endret)"
}

setup() {
  local dir; dir="$(normalize_dir "$1")"

  if [[ -d "$dir/.git" ]]; then
    local origin; origin="$(git -C "$dir" remote get-url origin 2>/dev/null || true)"
    [[ -z "$origin" || "$origin" == "$URL" ]] \
      || die "$dir har origin '$origin', som ikke matcher '$URL' nøyaktig"
  fi

  guard_new_path "$dir"

  git config --global --unset-all "$U.helper" 2>/dev/null || true
  git config --global --add "$U.helper" ""
  git config --global "$U.useHttpPath" true

  mkdir -p "$(dirname "$INC_FILE")"
  git config -f "$INC_FILE" --unset-all "$U.helper" 2>/dev/null || true
  git config -f "$INC_FILE" --add "$U.helper" ""
  git config -f "$INC_FILE" --add "$U.helper" manager

  git config --global "includeIf.gitdir/i:$dir/.path" "~/.gitconfig.d/$INC_NAME"

  echo "Satt opp $OWNER/$REPO"
  echo "  remote-URL: $URL"
  echo "  scope:      $(scopes | paste -sd ' ' -)"
  if [[ -d "$dir/.git" ]]; then
    echo "  repoet finnes, test med: $0 --check $OWNER/$REPO $dir"
  else
    echo "  klon nå:    git clone $URL \"$dir\""
  fi
}

check() {
  echo "$OWNER/$REPO"
  echo "  remote-URL:  $URL"
  echo "  global:      helper=$(git config --global --get-all "$U.helper" | sed 's/^$/(tom)/' | paste -sd ',' -)" \
       "useHttpPath=$(git config --global --get "$U.useHttpPath" || echo mangler)"
  echo "  scope-fil:   $INC_FILE $([[ -f "$INC_FILE" ]] && echo '(finnes)' || echo '(MANGLER)')"
  local s; s="$(scopes)"
  echo "  scope:       ${s:-(ingen includeIf)}"

  local dirs=()
  if [[ $# -ge 1 ]]; then dirs+=("$(normalize_dir "$1")"); else while read -r d; do [[ -n "$d" ]] && dirs+=("${d%/}"); done <<<"$s"; fi
  for d in "${dirs[@]}"; do
    if [[ -d "$d" ]]; then echo "  inne i $d: $(lookup "$d")"; else echo "  inne i $d: mappa finnes ikke ennå"; fi
  done
  local tmp; tmp="$(mktemp -d)"
  echo "  utenfor ($tmp): $(lookup "$tmp")"
  rmdir "$tmp"
}

case "${1:-}" in
  --check) [[ $# -ge 2 ]] || usage; parse_repo "$2"; shift 2; check "$@" ;;
  -h|--help|"") usage ;;
  *) [[ $# -eq 2 ]] || usage; parse_repo "$1"; setup "$2" ;;
esac
