#!/usr/bin/env bash
# Runs when a subagent finishes. Cheap tripwire: logs what the branch touched, so
# you can audit after the fact without reading every diff.
set -uo pipefail
cd "$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo unknown)
mkdir -p .agent-log
{
  echo "=== $(date -u +%FT%TZ) branch=$BRANCH"
  git diff --stat "${BASE_REF:-origin/main}"...HEAD 2>/dev/null || git diff --stat
} >> .agent-log/subagent-stops.log
exit 0
