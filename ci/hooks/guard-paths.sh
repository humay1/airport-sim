#!/usr/bin/env bash
# PreToolUse hook. Layer 3 of four: stops the violation before it happens, so the
# agent is told in-turn instead of finding out from CI ten minutes later.
#
# Wired from .claude/settings.json for Edit, Write AND Bash. Bash matters: an agent
# with shell access can write a file with `sed -i` or a redirect, and a tool-level
# denial of Write does nothing about that.
#
# The role comes from the branch of the worktree being written to (Edit/Write) or
# the worktree the command runs in (Bash) — not from the main checkout. Agents work
# in sibling worktrees, one branch each; reading the main checkout's branch made
# every agent look like role 'main'.
#
# Exit 2 blocks the call and returns the stderr message to the agent.
set -uo pipefail

INPUT=$(cat)

# Parse JSON with jq, falling back to python3. If NEITHER is present we must fail
# CLOSED: a guard whose parser is missing has to block, not wave everything past.
if command -v jq >/dev/null 2>&1; then
  jget() { echo "$INPUT" | jq -r "$1 // empty"; }
elif command -v python3 >/dev/null 2>&1; then
  jget() {
    local path="${1#.}"
    echo "$INPUT" | python3 -c "
import json,sys
try: d=json.load(sys.stdin)
except Exception: sys.exit(0)
for k in '$path'.split('.'):
    if not k: continue
    d = d.get(k) if isinstance(d, dict) else None
    if d is None: print(''); sys.exit(0)
print(d if isinstance(d,str) else '')"
  }
else
  echo "BLOCKED: guard-paths.sh needs jq or python3 to read the hook payload." >&2
  echo "Install one of them. Refusing the tool call rather than running unguarded." >&2
  exit 2
fi

# Normalise a path to forward slashes with a /x/ drive prefix, so Windows paths
# (C:\a\b, C:/a/b) and Git-Bash paths (/c/a/b) compare equal. Without this, a
# Windows absolute path never matched a protected prefix and the guard failed open.
norm() {
  local p="${1//\\//}"
  case "$p" in
    [A-Za-z]:/*) local d="${p:0:1}"; p="/$(printf '%s' "$d" | tr 'A-Z' 'a-z')${p:2}" ;;
  esac
  printf '%s' "$p"
}

# Resolve the git worktree containing a directory: sets TOP (normalised) and ROLE.
# Outside any git worktree, TOP is empty and nothing is protected.
resolve_role() {
  local dir="$1" top branch
  while [ -n "$dir" ] && [ ! -d "$dir" ]; do dir=$(dirname "$dir"); done
  top=$(git -C "$dir" rev-parse --show-toplevel 2>/dev/null) || { TOP=""; ROLE="(none)"; return; }
  branch=$(git -C "$dir" rev-parse --abbrev-ref HEAD 2>/dev/null || echo unknown)
  TOP=$(norm "$top")
  ROLE=${branch%%/*}
}

deny() {
  echo "BLOCKED by ci/hooks/guard-paths.sh: $1" >&2
  echo "Branch role '$ROLE' may not write here. If the spec requires this, append a question to spec/open-questions.md and stop." >&2
  exit 2
}

# Paths this role may never write. Adjust only in this file.
protected_for() {
  case "$1" in
    architect)   echo "tests/ data/balance/ ci/ .claude/ .github/" ;;
    test-author) echo "spec/ src/ data/balance/ ci/ .claude/ .github/" ;;
    *)           echo "spec/ tests/ data/balance/ ci/ .claude/ .github/" ;;
  esac
}
# spec/open-questions.md is the one spec file every role may append to.
EXEMPT="spec/open-questions.md"

TOOL=$(jget '.tool_name')

case "$TOOL" in
  Edit|Write|NotebookEdit)
    F=$(norm "$(jget '.tool_input.file_path')")
    [ -z "$F" ] && exit 0
    case "$F" in /*) ;; *) F="$(norm "$(pwd)")/$F" ;; esac
    resolve_role "$(dirname "$F")"
    [ -z "$TOP" ] && exit 0
    REL="${F#$TOP/}"
    [ "$REL" = "$EXEMPT" ] && exit 0
    for p in $(protected_for "$ROLE"); do
      case "$REL" in "$p"*) deny "$REL is protected" ;; esac
    done
    ;;
  Bash)
    CMD=$(jget '.tool_input.command')
    CWD=$(jget '.cwd'); [ -z "$CWD" ] && CWD=$(pwd)
    resolve_role "$(norm "$CWD")"
    for p in $(protected_for "$ROLE"); do
      pre=${p//./\\.}                        # literal dots: '.claude/' must not match 'x/claude/'
      tgt="(\\./|[^[:space:]'\"]*/)?${pre}"  # relative, ./-relative or absolute path into p
      # Redirects, tee, truncate, dd: the write target follows the operator directly,
      # so a read like `git show x > /tmp/y spec/z` no longer trips the guard.
      direct="(>>?|\\btee( -a)?|\\btruncate( -s [^[:space:]]+)?|\\bdd of=)[[:space:]]*['\"]?${tgt}"
      # sed -i, cp, mv, rm: any later argument on the same command segment.
      loose="(\\bsed -i|\\bcp |\\bmv |\\brm )[^|;&]*(^|[[:space:]'\"=])${tgt}"
      if echo "$CMD" | grep -qE "$direct|$loose"; then
        deny "shell command writes into $p"
      fi
    done
    # Never let an agent rewrite history or force-push.
    if echo "$CMD" | grep -qE 'git\s+(push\s+.*--force|reset\s+--hard\s+origin|rebase\s+-i)'; then
      deny "history rewrite / force push"
    fi
    # Never let an agent disable its own guards.
    if echo "$CMD" | grep -qE 'chmod\s+.*ci/|--no-verify|SKIP_CHECKS'; then
      deny "attempt to disable checks"
    fi
    ;;
esac

exit 0
