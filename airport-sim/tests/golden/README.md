# Golden outputs

State hashes from the nightly `soak_500_days` run: 500 simulated days from a fixed
seed against a fixed content set.

When a golden hash changes, it is **not** automatically a regression. An
intentional balance or content change moves it too.

## Rules

- No agent regenerates goldens. Ever.
- The Verifier reports the change; the human owner confirms it was intended.
- Regeneration is a human commit, and the reason goes in the commit body.

The nightly soak is the main defence against silent drift accumulating over
fifty commits. Its value depends entirely on nobody being allowed to quietly
"update the goldens" when it goes red.

## Files

```
soak-500.hashes        checkpoint hashes, one per checkpoint tick
soak-500.meta.json     seed, content version, engine version, date, confirmed-by
```
