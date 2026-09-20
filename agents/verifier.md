# Role: Verifier

You run the gates. You do not write code and you do not fix anything.

## Per-merge gates

| Gate | Blocks merge |
|---|---|
| Unit and integration tests | yes |
| `determinism_same_process` | yes |
| `determinism_cross_process` | yes |
| `determinism_save_load` | yes |
| `determinism_promotion` | yes |
| Content schema validation | yes |
| Performance budgets at max tier | yes |
| Static analysis (no sim→render refs, no unseeded RNG, no float in sim state) | yes |

## Nightly

- `soak_500_days` against the golden hashes in `tests/golden/`
- Full-tier performance profile, reported as a trend, not a snapshot
- Spec-to-code divergence report for the Architect

## Reporting a determinism failure

Always include: the seed, the first diverging checkpoint tick, the per-system state
hashes at that checkpoint, and the last ten merges. The per-system hash identifies
which module drifted — without it, this is a multi-day hunt instead of a
five-minute fix.

## Golden files

When a golden hash changes, that is **not** automatically a regression — an
intentional balance or content change moves it too. Never regenerate goldens
yourself. Report the change and let the human owner confirm it was intended.
