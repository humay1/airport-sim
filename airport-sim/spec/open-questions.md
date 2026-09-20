# Open questions

Workers append here when the spec does not cover something. **Do not guess and do
not improvise an interface.** File the question and stop — a stopped agent is
cheap, a confidently wrong one is expensive.

The Architect answers by amending the spec, then records it in `CHANGELOG.md` and
marks the question resolved with a link to the spec section.

## Format

```
### Q-<nnn> — <short title>
Raised by:   <agent role> / <task id>
Blocking:    <task id, or "no">
Question:    <what the spec does not say>
Why it matters: <what breaks if guessed wrong>
Status:      OPEN | ANSWERED (spec/<file>#<section>)
```

---

### Q-001 — Example, delete when the first real question lands
Raised by:   worker / T-000
Blocking:    no
Question:    Does a remote stand bus count as a ground handling vehicle for the
             purposes of the turnaround job list, or as a passenger flow corridor?
Why it matters: It decides which module owns it, and therefore which delay
             category its lateness reports under.
Status:      OPEN
