#!/usr/bin/env bash
# Path guard, CI copy. Compares changed files against the writable paths declared
# in the task file named by the branch. Branch convention: <role>/<task-id>-<slug>
#
# This is layer 2 of four. Layer 1 (server-side branch protection + CODEOWNERS) is
# the one that actually holds; this one fails fast and explains why.
set -euo pipefail
cd "$(dirname "$0")/.."

BASE=${BASE_REF:-origin/main}
BRANCH=$(git rev-parse --abbrev-ref HEAD)

# Paths no branch may touch except the role that owns them.
protected_for_role() {
  case "$1" in
    architect)   echo "tests/ data/balance/ ci/ .claude/ .github/" ;;
    test-author) echo "spec/ src/ data/balance/ ci/ .claude/ .github/" ;;
    *)           echo "spec/ tests/ data/balance/ ci/ .claude/ .github/" ;;
  esac
}

ROLE=${BRANCH%%/*}
TASK=$(echo "$BRANCH" | sed -n 's|^[^/]*/\(T-[0-9]\{3\}\).*|\1|p')

CHANGED=$(git diff --name-only "$BASE"...HEAD || git diff --name-only --cached)
[ -z "$CHANGED" ] && { echo "no changes"; exit 0; }

STATUS=0

# --- universally protected paths for this role
for p in $(protected_for_role "$ROLE"); do
  if echo "$CHANGED" | grep -q "^$p"; then
    echo "BLOCKED: branch role '$ROLE' may not write to $p"
    echo "$CHANGED" | grep "^$p" | sed 's/^/  /'
    STATUS=1
  fi
done

# --- task-declared writable paths
if [ -n "$TASK" ]; then
  TASKFILE=$(ls tasks/${TASK}-*.md 2>/dev/null | head -1 || true)
  if [ -n "$TASKFILE" ]; then
    ALLOWED=$(awk '/^## Writable paths/{f=1;next} /^```$/{if(f==1){f=2;next}else if(f==2){exit}} f==2' "$TASKFILE" \
              | sed 's/\*\*$//' | sed 's|/\*\*$|/|' | grep -v '^$' || true)
    if [ -n "$ALLOWED" ]; then
      while read -r file; do
        [ -z "$file" ] && continue
        match=0
        while read -r allow; do
          case "$file" in $allow*) match=1 ;; esac
        done <<< "$ALLOWED"
        if [ "$match" -eq 0 ]; then
          echo "BLOCKED: $file is outside the writable paths declared in $TASKFILE"
          STATUS=1
        fi
      done <<< "$CHANGED"
    fi
  else
    echo "WARNING: branch names $TASK but tasks/${TASK}-*.md does not exist"
  fi
fi

[ "$STATUS" -eq 0 ] && echo "path guard: ok"
exit $STATUS
