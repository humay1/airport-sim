# T-047 — `sim.core` test: command application allocates nothing (Q-065)

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` tests |
| Assigned role | test-author |
| Depends on | T-042 (not merged; both write `tests/sim/core/**`); spec PR #77 (`1574534`, Q-065) merged |
| Spec source | `spec/08-interfaces-core.md` §8.7 "Allocation" (Q-065) and its Tests list, §8.5 (loop allocation), §8.6 (bus, Q-035); `spec/03-module-map.md` "How a budget is measured" (`Step` meter bullet) |
| Blocked by | — |

## Writable paths

```
tests/sim/core/**
```

No other file. No `src/`, `spec/`, `ci/` or `.github/` edit. The Test Author
opens its own PR. T-042 also writes `tests/sim/core/**`, so **do not release
this task until T-042 has merged** (one writer per path). Check the queue
before release.

## Why this task exists

Q-065: `08` §8.7 "Allocation" pins `ApplyDue` and its dispatch as
allocation-free on a tick that completes normally, for every kind, any number
of due commands per tick and any log size, from the first tick after `Build`.
Merged T-005 code (`CommandQueue.ApplyDue`) already satisfies this. The merged
tests show it only as a difference between two hosts, for `SetServersOpen` and
`NoOp`. The spec names one new test that asserts exactly 0. The Q-065 answer
makes it "a separate small test-only task after T-042, not part of T-042".

## What this task does

Add one test, with the name the spec gives:

`test_command_queue_apply_due_of_every_kind_allocates_nothing`

Per `08` §8.7 "Tests (Q-065)":

- It meters `ISimHost.Step` over **non-checkpoint ticks**.
- The host has an allocation-free probe handler for `SetServersOpen` at
  payload length 4, and one for `ReassignStand` at payload length 3.
- Inside the window, some ticks have no due command and some have several of
  each kind, with `NoOp`s among them.
- It asserts **exactly 0 bytes**, not a difference between two hosts.
- Use the shared allocation helper from T-037 (forced-GC meter). A warm-up
  before the window is allowed, to keep one-time JIT and type setup out of the
  meter (`08` §8.7). No window tick may throw (a tick that throws is outside
  the rule).

Do not change any existing test. This is a new test, so the Slow tag question
(L11a) is decided by its CI duration; it is expected to be short.

## Tests to pass

This task is the Test Author's own change. No separate grant.

```
tests/sim/core/**
```

## Performance budget

Not applicable (allocation assertion only, no timing).

## Done when

- [ ] `test_command_queue_apply_due_of_every_kind_allocates_nothing` exists and asserts exactly 0 bytes
- [ ] It meters `ISimHost.Step` over non-checkpoint ticks with the two probe handlers (4-byte `SetServersOpen`, 3-byte `ReassignStand`), empty ticks, multi-command ticks and `NoOp`s in the window
- [ ] Passes against the merged `sim.core` with no `src/` change
- [ ] No existing test edited
- [ ] `ci/run-checks.sh` green; passes on repeated CI runs
- [ ] No writes outside `tests/sim/core/**`
- [ ] Reviewer approved

## Worker notes

No worker: tests only. A failure against merged code is a real `sim.core`
finding for the team lead, not a test edit.
