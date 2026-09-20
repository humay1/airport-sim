# 07 — Conventions

Extends `CLAUDE.md`. Where they disagree, `CLAUDE.md` wins.

## Naming

- Modules: `sim.<lowercase>`, `app.<lowercase>`
- Events: past tense, `FlightDeparted`, `QueueThresholdExceeded`
- Commands: imperative, `OpenSecurityLane`, `ReassignStand`
- Tests: `test_<subject>_<condition>_<expectation>`

## Testing

- Unit tests are deterministic and never use wall-clock time or unseeded RNG.
- Every sim module ships a **headless day test**: run one full simulated day
  against a fixture schedule and assert invariants.
- Property-based tests preferred for the flow, baggage and delay modules.
- Test fixtures live beside tests, never in `data/`.
- **Workers never edit tests.** A wrong test is a spec question.

## Comments and documentation

- Comment *why*, never *what*.
- Every public interface carries a doc comment naming the spec section it
  implements. If there is no such section, the interface should not exist.

## Error handling

- Sim code does not throw for gameplay conditions. Invalid states are data.
- Sim code throws on programmer error (broken invariant) — loudly, with the tick
  number and the state hash.
- Content loading fails hard with the file path and schema violation.

## Logging

- Sim layer logs through a deterministic sink; log volume must not affect outcomes.
- Every log line in sim carries the tick number.

## Performance

- Allocation in the per-tick hot path is a rejection criterion.
- Budget assertions live in tests, not in comments.
- Optimise only against a measurement. "This looked slow" is not a reason.
