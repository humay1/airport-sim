#!/usr/bin/env bash
# Path guard, CI copy. Compares changed files against the writable paths declared
# in the task file named by the branch. Branch convention: <role>/<task-id>-<slug>
#
# This is layer 2 of four. Layer 1 (server-side branch protection + CODEOWNERS) is
# the one that actually holds; this one fails fast and explains why.
set -euo pipefail
cd "$(dirname "$0")/.."

# ---------------------------------------------------------------- branch name
# GitHub Actions checks out a DETACHED HEAD on pull requests, so
# `git rev-parse --abbrev-ref HEAD` returns the literal string "HEAD" and the role
# parse silently degrades to the most restrictive case. Prefer the CI-provided
# branch name, then fall back to local resolution.
resolve_branch() {
  # Pull request: the source branch
  [ -n "${GITHUB_HEAD_REF:-}" ] && { echo "$GITHUB_HEAD_REF"; return 0; }
  # Push event: refs/heads/<branch>
  if [ -n "${GITHUB_REF:-}" ]; then
    case "$GITHUB_REF" in refs/heads/*) echo "${GITHUB_REF#refs/heads/}"; return 0 ;; esac
  fi
  # Local
  local b
  b=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "")
  if [ -n "$b" ] && [ "$b" != "HEAD" ]; then echo "$b"; return 0; fi
  # Detached locally: find a branch containing HEAD
  b=$(git for-each-ref --format='%(refname:short)' --points-at HEAD refs/heads 2>/dev/null | head -1)
  [ -n "$b" ] && { echo "$b"; return 0; }
  return 1
}

if ! BRANCH=$(resolve_branch); then
  echo "BLOCKED: cannot determine the branch name, so the role cannot be established."
  echo "  Refusing to guess. In CI this usually means a detached checkout without GITHUB_HEAD_REF."
  exit 1
fi

# Paths no branch may touch except the role that owns them.
protected_for_role() {
  case "$1" in
    architect)   echo "tests/ data/balance/ ci/ .claude/ .github/" ;;
    test-author) echo "spec/ src/ data/balance/ ci/ .claude/ .github/" ;;
    *)           echo "spec/ tests/ data/balance/ ci/ .claude/ .github/" ;;
  esac
}

case "$BRANCH" in
  */*) ROLE=${BRANCH%%/*} ;;
  *)   ROLE="(none)"
       echo "NOTE: branch '$BRANCH' has no <role>/ prefix; applying the strictest rules." ;;
esac
TASK=$(echo "$BRANCH" | sed -n 's|^[^/]*/\(T-[0-9]\{3\}\).*|\1|p')

# ---------------------------------------------------------------- base ref
# Work out what to diff against. A guard that cannot see the changes must FAIL,
# never print "no changes" and exit 0. That is failing open, which is worse than
# having no guard at all, because it looks like it is working.
resolve_base() {
  if [ -n "${BASE_REF:-}" ] && git rev-parse --verify -q "$BASE_REF" >/dev/null; then
    echo "$BASE_REF"; return 0
  fi
  if [ -n "${GITHUB_BASE_REF:-}" ]; then
    git fetch --no-tags --depth=200 origin "$GITHUB_BASE_REF" >/dev/null 2>&1 || true
    for cand in "origin/$GITHUB_BASE_REF" FETCH_HEAD; do
      git rev-parse --verify -q "$cand" >/dev/null && { echo "$cand"; return 0; }
    done
  fi
  for cand in origin/main origin/master main master; do
    git rev-parse --verify -q "$cand" >/dev/null && { echo "$cand"; return 0; }
  done
  if git rev-parse --verify -q HEAD~1 >/dev/null; then
    echo "HEAD~1"; return 0
  fi
  return 1
}

if ! BASE=$(resolve_base); then
  echo "BLOCKED: cannot resolve a base ref to diff against."
  echo "  branch=$BRANCH  BASE_REF=${BASE_REF:-unset}  GITHUB_BASE_REF=${GITHUB_BASE_REF:-unset}"
  echo "  In CI, check out with fetch-depth: 0 so the base branch is present."
  echo "  Refusing to report ok when the guard cannot see the changes."
  exit 1
fi

if ! CHANGED=$(git diff --name-only "$BASE"...HEAD 2>/dev/null); then
  if ! CHANGED=$(git diff --name-only "$BASE" HEAD 2>/dev/null); then
    echo "BLOCKED: git diff against '$BASE' failed. Refusing to pass."
    exit 1
  fi
fi

if [ -z "$CHANGED" ]; then
  echo "path guard: no changes against $BASE"
  exit 0
fi
echo "path guard: comparing against $BASE"

STATUS=0

# --- test pairing (spec/07-conventions.md L9)
# A task's tests reach main in the same merge as its implementation, so a worker
# branch legitimately carries the Test Author's tests/ files. They are accepted
# ONLY if each one is byte-identical to the same path on the task's own
# test-author/<task-id>-* branch. A worker edit to any test still fails, and a
# test file with no matching test-author branch still fails.
if [ "$ROLE" = "worker" ] && [ -n "$TASK" ] && echo "$CHANGED" | grep -q '^tests/'; then
  git fetch --no-tags -q origin '+refs/heads/test-author/*:refs/remotes/origin/test-author/*' 2>/dev/null || true
  TEST_BRANCH=$(git for-each-ref --format='%(refname)' \
                  "refs/heads/test-author/${TASK}-*" "refs/remotes/origin/test-author/${TASK}-*" | head -1)
  if [ -n "$TEST_BRANCH" ]; then
    VOUCHED=""
    while read -r file; do
      case "$file" in tests/*) ;; *) continue ;; esac
      if git diff --quiet "$TEST_BRANCH" HEAD -- "$file"; then
        VOUCHED="$VOUCHED$file"$'\n'
      else
        echo "BLOCKED: $file differs from ${TEST_BRANCH#refs/} — workers never edit tests"
        STATUS=1
      fi
    done <<< "$CHANGED"
    [ -n "$VOUCHED" ] && echo "path guard: $(printf '%s' "$VOUCHED" | grep -c .) test file(s) match ${TEST_BRANCH#refs/}"
    # Vouched files are the Test Author's writes, not this branch's; drop them
    # from the role and writable-path checks below.
    CHANGED=$(echo "$CHANGED" | grep -vxF -f <(printf '%s' "$VOUCHED") || true)
  else
    echo "BLOCKED: branch carries tests/ changes but no test-author/${TASK}-* branch exists to pair them with"
    STATUS=1
  fi
fi

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