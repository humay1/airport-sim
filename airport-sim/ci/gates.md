# CI gates

CI is the only merge path. No agent merges on its own judgement.

## Blocking on every merge

1. Build, all targets
2. Unit and integration tests
3. `determinism_same_process`
4. `determinism_cross_process`
5. `determinism_save_load`
6. `determinism_promotion`
7. Content schema validation
8. Per-module performance budgets at max tier
9. Static analysis:
   - no references from `src/sim/` to render, UI or input
   - no unseeded RNG in `src/sim/`
   - no wall-clock time in `src/sim/`
   - no float types in serialised sim state
   - no per-agent A\*
   - no allocation in per-tick hot paths

## Path guards

| Path | Who may write |
|---|---|
| `spec/**` | Architect only |
| `spec/01-architecture.md`, `spec/02-determinism.md` | human only |
| `tests/**` | Test Author only |
| `data/balance/**` | human only |
| `ci/**` | human only |
| `src/sim/<module>/**` | that module's worker only |

Violations fail the build. These guards are what make unattended operation safe —
they are not advisory.

## Nightly

- `soak_500_days` against `tests/golden/`
- Performance trend report
- Spec-to-code divergence report

## Escalation

Three consecutive failures of the same gate on the same task escalate to the human
owner. That pattern usually means the spec is wrong, not the code.
