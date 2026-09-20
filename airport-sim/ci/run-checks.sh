#!/usr/bin/env bash
# Local pre-push checks. Mirrors CI. Workers run this before pushing.
# Fill in the commands once the engine choice in spec/01-architecture.md is made.
set -euo pipefail

fail() { echo "FAIL: $1" >&2; exit 1; }
step() { echo "--- $1"; }

step "build"
# TODO: build command

step "unit + integration tests"
# TODO: test command

step "determinism: same process"
# TODO: run 10 sim-days twice in one process, compare checkpoint hashes

step "determinism: cross process"
# TODO: run in two processes, compare final state hash

step "determinism: save/load"
# TODO: 1000 ticks, save at 500, reload, continue, compare

step "determinism: promotion neutrality"
# TODO: same day headless vs camera parked on a gate

step "content schema validation"
# TODO: validate data/**/*.json against data/schemas/

step "performance budgets"
# TODO: assert per-module ms/tick at max tier

step "static analysis"
# TODO: sim->render refs, unseeded RNG, wall-clock, floats in sim state,
#       per-agent A*, hot-path allocation

echo "ALL CHECKS PASSED"
