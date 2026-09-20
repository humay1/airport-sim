#!/usr/bin/env bash
# Local pre-push checks. Mirrors CI exactly. Workers run this before pushing.
# Usage: ci/run-checks.sh [--fast]
#   --fast skips the long determinism and budget gates (NOT sufficient to push)
set -euo pipefail

cd "$(dirname "$0")/.."
FAST=${1:-}
FAILED=0

step()  { printf '\n\033[1m--- %s\033[0m\n' "$1"; }
ok()    { printf '  \033[32mok\033[0m  %s\n' "$1"; }
fail()  { printf '  \033[31mFAIL\033[0m %s\n' "$1"; FAILED=1; }

# ---------------------------------------------------------------- build
step "build"
if [ -f AirportSim.sln ]; then
  dotnet build AirportSim.sln -c Release --nologo -warnaserror \
    && ok "solution builds" || fail "build"
else
  echo "  (no solution yet — T-001 creates it)"
fi

# ---------------------------------------------------------------- tests
step "unit + integration tests"
if [ -f AirportSim.sln ]; then
  dotnet test AirportSim.sln -c Release --nologo --no-build \
    && ok "tests pass" || fail "tests"
else
  echo "  (skipped, no solution yet)"
fi

# ------------------------------------------------- static analysis (cheap, always)
step "static analysis"

# Sim layer must never reference the presentation layer or the engine.
if grep -rInE '^\s*using\s+(UnityEngine|UnityEditor)' src/sim 2>/dev/null; then
  fail "sim references Unity"
else ok "no engine references in src/sim"; fi

# No unseeded randomness.
if grep -rInE '\bnew\s+Random\s*\(|System\.Random|Guid\.NewGuid' src/sim 2>/dev/null; then
  fail "unseeded RNG in src/sim — use IRandomService"
else ok "no unseeded RNG"; fi

# No wall-clock time.
if grep -rInE 'DateTime\.(Now|UtcNow)|Stopwatch\.|Environment\.TickCount' src/sim 2>/dev/null; then
  fail "wall-clock time in src/sim"
else ok "no wall-clock time"; fi

# No floats in sim code. Fx is the fixed-point type.
if grep -rInE '\b(float|double)\b' src/sim 2>/dev/null | grep -v '//.*allowed-float'; then
  fail "float/double in src/sim — use Fx"
else ok "no floating point in sim"; fi

# Unordered iteration where order matters.
if grep -rInE 'foreach\s*\([^)]*\bin\s+\w*(Dictionary|HashSet)\w*\b' src/sim 2>/dev/null; then
  fail "iteration over unordered collection in src/sim — sort by a stable key"
else ok "no unordered iteration"; fi

# Per-agent A* is banned by spec/01-architecture.md.
if grep -rIn 'AStar\|A_Star\|FindPathAStar' src/sim 2>/dev/null; then
  fail "per-agent A* found — use flow fields"
else ok "no per-agent A*"; fi

# ---------------------------------------------------------------- content
step "content schema validation"
if [ ! -f ci/validate-content.py ]; then
  fail "ci/validate-content.py is missing"
elif ! command -v python3 >/dev/null; then
  fail "python3 not available — cannot validate content"
else
  python3 ci/validate-content.py && ok "content valid" || fail "content schema"
fi

# ---------------------------------------------------------------- path guard
step "path guard"
bash ci/check-paths.sh && ok "writes inside declared paths" || fail "path guard"

# ---------------------------------------------------------------- slow gates
if [ "$FAST" = "--fast" ]; then
  printf '\n\033[33m--fast: determinism and budget gates SKIPPED. Not enough to push.\033[0m\n'
else
  step "determinism: same process"
  dotnet run --project tools/SimHarness -c Release -- determinism --days 10 --seed 12345 \
    && ok "same-process determinism" || fail "determinism_same_process"

  step "determinism: cross process"
  dotnet run --project tools/SimHarness -c Release -- determinism --days 10 --seed 12345 --hash-only > /tmp/run-a.hash
  dotnet run --project tools/SimHarness -c Release -- determinism --days 10 --seed 12345 --hash-only > /tmp/run-b.hash
  diff -q /tmp/run-a.hash /tmp/run-b.hash \
    && ok "cross-process determinism" || fail "determinism_cross_process"

  step "determinism: save/load"
  dotnet run --project tools/SimHarness -c Release -- saveload --ticks 1000 --save-at 500 \
    && ok "save/load round trip" || fail "determinism_save_load"

  step "determinism: promotion neutrality"
  dotnet run --project tools/SimHarness -c Release -- promotion --days 1 \
    && ok "promotion is outcome-neutral" || fail "determinism_promotion"

  step "performance budgets"
  dotnet run --project tools/SimHarness -c Release -- budget --tier max \
    && ok "within 6 ms/tick" || fail "performance budget"
fi

printf '\n'
if [ "$FAILED" -eq 0 ]; then
  printf '\033[32mALL CHECKS PASSED\033[0m\n'
else
  printf '\033[31mCHECKS FAILED — do not push\033[0m\n'; exit 1
fi
