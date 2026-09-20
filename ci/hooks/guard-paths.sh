#!/usr/bin/env bash
# PreToolUse hook. Layer 3 of four: stops the violation before it happens, so the
# agent is told in-turn instead of finding out from CI ten minutes later.
#
# Wired from .claude/settings.json for Edit, Write AND Bash. Bash matters: an agent
# with shell access can write a file with `sed -i` or a redirect, and a tool-level
# denial of Write does nothing about that.
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

TOOL=$(jget '.tool_name')
REPO=$(git rev-parse --show-toplevel 2>/dev/null || pwd)
BRANCH=$(git -C "$REPO" rev-parse --abbrev-ref HEAD 2>/dev/null || echo unknown)
ROLE=${BRANCH%%/*}

deny() {
  echo "BLOCKED by ci/hooks/guard-paths.sh: $1" >&2
  echo "Branch role '$ROLE' may not write here. If the spec requires this, append a question to spec/open-questions.md and stop." >&2
  exit 2
}

# Paths this role may never write. Adjust only in this file.
case "$ROLE" in
  architect)   PROTECTED="tests/ data/balance/ ci/ .claude/ .github/" ;;
  test-author) PROTECTED="spec/ src/ data/balance/ ci/ .claude/ .github/" ;;
  *)           PROTECTED="spec/ tests/ data/balance/ ci/ .claude/ .github/" ;;
esac
# spec/open-questions.md is the one spec file every role may append to.
EXEMPT="spec/open-questions.md"

check_path() {
  local f="$1"
  [ -z "$f" ] && return 0
  # normalise to a repo-relative path
  case "$f" in /*) f="${f#$REPO/}" ;; ./*) f="${f#./}" ;; esac
  [ "$f" = "$EXEMPT" ] && return 0
  for p in $PROTECTED; do
    case "$f" in "$p"*) deny "$f is protected" ;; esac
  done
}

case "$TOOL" in
  Edit|Write|NotebookEdit)
    check_path "$(jget '.tool_input.file_path')"
    ;;
  Bash)
    CMD=$(jget '.tool_input.command')
    # Block obvious write vectors aimed at protected trees.
    for p in $PROTECTED; do
      if echo "$CMD" | grep -qE "(>|>>|sed -i|tee|cp |mv |rm |truncate|dd of=)[^|;&]*${p//\//\\/}"; then
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
