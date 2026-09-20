# 05 — Policy system

Policies are standing rules, distinct from buildings and staffing. Cheap to change,
slow to take effect, and each trades a KPI the player likes for one they don't.

## Mechanics

- Set on a dedicated screen with a **projected impact preview** before commit.
- **Lag:** effects land after `effect_lag` (24 hours to 7 days depending on
  category). Nothing is instant.
- **Cooldown:** a changed policy cannot change again for `cooldown_days`. Prevents
  hourly flip-flopping.
- **Approval:** roughly one third of policies require an external body to approve.
  Requests take in-game weeks and can be refused. This is what turns the policy
  screen into planning rather than a settings menu.

| Approving body | Approves |
|---|---|
| Regulator | Pricing, security posture, service standards |
| Community council | Operating hours, noise, environmental |
| Airline consortium | Slot policy, ground handling arrangements |

## Categories

### Operating hours and curfew
Per-runway opening and closing times. Modes:
- `hard_curfew` — no movements in the window
- `quota_count` — a nightly noise budget spent per movement, weighted by aircraft
  noise category. A quiet regional jet costs less than a loaded widebody.
- `open_24h` — maximum cargo and red-eye revenue, continuous community damage

Player-authored **exemption rules**: medical, diversion, delayed inbound within a
grace window. The grace window is the interesting choice — generous windows rescue
on-time performance and slowly poison community reputation.

### Slot policy
- `grandfathered` — incumbents happy, schedule calcifies
- `auctioned` — maximum revenue, carriers resent it
- `use_it_or_lose_it` with a threshold (default 80%) — reclaim hoarded slots at a
  reputation cost
- **Declared capacity per hour** is player-set. Setting it optimistically is a
  designed self-inflicted wound.

### Pricing
Landing fees by weight and noise category, passenger charges, parking tariffs,
peak/off-peak multipliers, new-route incentive schemes. Aeronautical charges are
subject to a regulated cap; the regulator audits. Carrier discounts are contractual
and hard to claw back.

### Security posture
`standard` | `enhanced` | `risk_based` (with trusted-traveller lanes).
Higher posture lowers breach probability and lowers throughput. A breach forces
evacuation and full re-screening, costing more than a season of enhanced screening.
Tune so that insurance is the rational play — but not obviously so.

### Staffing and labour
Shift patterns, overtime authorisation, minimum staffing floors, in-house versus
outsourced ground handling, wage policy. Outsourcing is cheaper, less reliable, and
carries a strike risk the player does not control.

### Environmental
Single-engine taxi mandates, APU restrictions, ground fleet electrification
targets, deicing fluid recovery. Cost now, goodwill and grant eligibility later.

### Accessibility and service standards
Published maximum queue times, assistance guarantees, minimum connection time,
delay compensation thresholds. Publishing a 10-minute security standard is a
commitment: missing it costs cash and trust.

### Commercial
Tenant mix rules, retail opening hours, advertising density, smoking and pet
policy, exclusive versus open ground transport.

### Contingency
Pre-written rules that auto-execute during incidents so the player is not
micromanaging at 3am: diversion acceptance criteria, snow plan trigger thresholds,
IT failover to manual check-in, evacuation routing. Writing a good contingency
policy before winter is the season's real skill test.

## Reputation

Four independent meters. Never collapse them into one score.

| Meter | Driven by | Gates |
|---|---|---|
| Passenger | Queue times, walking distance, mishandled bags, delays | Traffic growth |
| Airline | Turn times, fees, slot policy, stand availability | Contract renewals |
| Regulator | Safety, compliance, audit results, fee caps | Licence, ARFF category |
| Community | Noise contour, curfew adherence, environmental policy, jobs | **Expansion permits** |

Community reputation gating expansion permits is the key coupling: it makes the
curfew decision a long-term strategic one rather than a nightly revenue calculation.

## Data

Policies are content, not code. Schema: `data/schemas/policy.schema.json`.
Each definition declares its options, effect lag, cooldown, approving body,
affected KPIs and effect magnitudes. `sim.policy` reads definitions and applies
effects; it hardcodes no policy.
