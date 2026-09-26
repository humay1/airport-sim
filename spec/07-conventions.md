# 07 — Conventions

Extends `CLAUDE.md`. Where they disagree, `CLAUDE.md` wins.

## Naming

- Modules: `sim.<lowercase>`, `app.<lowercase>`
- Events: past tense, `FlightDeparted`, `QueueThresholdExceeded`
- Commands: imperative, `OpenSecurityLane`, `ReassignStand`
- Tests: `test_<subject>_<condition>_<expectation>`, which is the C# method
  name verbatim ("Solution layout and build", rule L7)

## Testing

- Unit tests are deterministic and never use wall-clock time or unseeded RNG.
- Every sim module ships a **headless day test**: run one full simulated day
  against a fixture schedule and assert invariants.
- Property-based tests preferred for the flow, baggage and delay modules.
- Test fixtures live beside tests, never in `data/`.
- **Workers never edit tests.** A wrong test is a spec question.
- The test framework, project files and naming are fixed by "Solution layout
  and build" below.

## Solution layout and build (Q-013)

Binding on every project built by `AirportSim.sln`. It replaces the freedom
`08-interfaces-core.md` (Notation) and the later interface files gave workers
over namespaces, access modifiers and project layout. Build settings live in
the `.csproj` files. There is **no** `Directory.Build.props`,
`Directory.Build.targets`, `Directory.Packages.props`, `global.json` or
`NuGet.config` in the repo unless a spec section names it and a task owns it.

**L1. One project per module, at a fixed path.** Module `sim.<m>` has exactly
one production project and one test project. `<M>` is `<m>` with its first
letter upper-cased (`core` → `Core`, `turnaround` → `Turnaround`).

| Unit | Project file | AssemblyName = RootNamespace | Target |
|---|---|---|---|
| `sim.<m>` | `src/sim/<m>/AirportSim.Sim.<M>.csproj` | `AirportSim.Sim.<M>` | `netstandard2.1`, C# 9 |
| tests of `sim.<m>` | `tests/sim/<m>/AirportSim.Sim.<M>.Tests.csproj` | `AirportSim.Sim.<M>.Tests` | `net8.0`, C# 12 |
| `app.render` scene layer | `src/app/render/Scene/AirportSim.App.Render.csproj` | `AirportSim.App.Render` | `netstandard2.1`, C# 9 |
| `app.ui` scene layer | `src/app/ui/Scene/AirportSim.App.Ui.csproj` | `AirportSim.App.Ui` | `netstandard2.1`, C# 9 |
| `app.host` headless host | `src/app/host/AirportSim.App.Host.csproj` | `AirportSim.App.Host` | `netstandard2.1`, C# 9 |
| tests of `app.<m>` | `tests/app/<m>/AirportSim.App.<M>.Tests.csproj` | `AirportSim.App.<M>.Tests` | `net8.0`, C# 12 |
| `tools.simharness` | `tools/SimHarness/AirportSim.Tools.SimHarness.csproj` | `AirportSim.Tools.SimHarness` | `net8.0`, C# 12, `Exe` |
| tests of `tools.simharness` (Q-025) | `tests/tools/simharness/AirportSim.Tools.SimHarness.Tests.csproj` | `AirportSim.Tools.SimHarness.Tests` | `net8.0`, C# 12 |

The engine backends (`src/app/render/Unity/`, `src/app/ui/Unity/`) and
`unity/AirportSim/` are compiled by Unity (`15` §15.3, `16` §16.2). They have
no `.csproj` and are not in `AirportSim.sln`. Fixture directories
(`tests/fixtures/**`) hold no project.

**L2. Production project file, byte for byte.** `src/sim/core/AirportSim.Sim.Core.csproj`
is exactly the text between the fences below. It is UTF-8 without a BOM, uses
LF line endings in the repository, indents with two spaces, and ends with one
newline after `</Project>`. Every task that writes `src/sim/core/**` and finds
the file absent adds it with exactly this content. Because git merges
identical additions cleanly, T-001 and T-003 can both add it.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>AirportSim.Sim.Core</AssemblyName>
    <RootNamespace>AirportSim.Sim.Core</RootNamespace>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

</Project>
```

Every other `netstandard2.1` project in L1 is this file with the two names
changed. It also gets one `ItemGroup` after the `PropertyGroup`, separated by
a blank line, with one `<ProjectReference Include="<relative path>" />` per
module it references directly. Paths use forward slashes and are listed in
the order of the table below (Q-022). A project references exactly the
modules whose types appear in its published interface file: its interfaces,
its factory parameters, and the types they carry. That is always a subset of
its `03-module-map.md` "Depends on" cell. Anything further away comes
transitively. For Phase 0/1 the list is binding:

| Project | Direct `ProjectReference`s, in this order |
|---|---|
| `AirportSim.Sim.Core` | none |
| `AirportSim.Sim.World` | Core |
| `AirportSim.Sim.Flow` | Core, World |
| `AirportSim.Sim.Schedule` | Core, Flow |
| `AirportSim.Sim.Airside` | Core, Schedule, Flow |
| `AirportSim.Sim.Turnaround` | Core, Schedule |
| `AirportSim.Sim.Delay` | Core (it learns everything through events, which are defined in `sim.core`) |
| `AirportSim.App.Render` | Core, Airside, Flow (`15` §15.9 `RenderSources`) |
| `AirportSim.App.Ui` | Core, Flow, App.Render (`17` §17.7) |
| `AirportSim.App.Host` | Core, World, Schedule, Airside, Flow, Turnaround, Delay, App.Render, App.Ui |

A module not in the table gets its row by amendment before its first task.
A dependency whose project does not exist yet blocks the task, and the task
files an open question. Nothing else is added: no package
references (rule 7 of "Runtime portability"), no `InternalsVisibleTo`, no
other properties. A spec amendment is the only way the file changes.

`GenerateDocumentationFile` together with `TreatWarningsAsErrors` makes a
missing doc comment on a public member a build error (CS1591). This
enforces "Comments and documentation" mechanically.

**L3. Test project file, byte for byte.** `tests/sim/core/AirportSim.Sim.Core.Tests.csproj`
is exactly the text below, with the same encoding rules as L2. Every other test
project in L1 is this file with the two names and the one `ProjectReference`
changed. A test project references **only** the production project of the
module it tests. The other modules it sees come through that project's own
references. For the harness's test project, that one reference is
`../../../tools/SimHarness/AirportSim.Tools.SimHarness.csproj`. Harness
tests call the harness in process, through its public surface (`19`
§19.1). They never spawn a process (Q-025).

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>AirportSim.Sim.Core.Tests</AssemblyName>
    <RootNamespace>AirportSim.Sim.Core.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../../src/sim/core/AirportSim.Sim.Core.csproj" />
  </ItemGroup>

</Project>
```

**L4. Test framework.** The framework is **xUnit v2** with the three pinned packages above
and no others. There is **no property-testing library.** A property test is an
xUnit `[Fact]` that loops over inputs drawn from a SplitMix64 sequence. The
generator is the pinned function of `08` §8.8, written as a private helper in
the test project and seeded with an integer literal inside the test. When the
test fails, its message names the seed and the iteration index.
`System.Random` is not used anywhere, tests included (`CLAUDE.md`: "No
`Random` outside the seeded RNG service"). Integer arithmetic only, because
`08` §8.3 bans floating point in tests too. Test code may use `net8.0`-only
APIs, for example the `Int128`/`BigInteger` oracle in `08` §8.3. None of that code ever reaches `src/**`. Each test
project disables xUnit test parallelisation with the assembly-level attribute
`[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]`, in
a file the Test Author writes. Tests that share a process must not distort
each other's timing budgets.

**L5. Public surface only. No `InternalsVisibleTo`.** Tests, the harness and other
modules compile only against the public surface. A type or member is `public` if
and only if a spec interface section names it. Everything else is
`internal`, or `private` inside its type. A test that needs an unnamed member is
testing an implementation choice, and it is filed as an open question instead.

**L6. Namespaces and files.** Every public type of a project is declared directly in
that project's RootNamespace, with no sub-namespaces. Each is in its own file named
`<TypeName>.cs`, directly in the project directory. Internal types may use
any file layout inside the project directory. Test classes are `public sealed class <Subject>Tests`
in the test project's RootNamespace, in a file named `<Subject>Tests.cs`.
`<Subject>` is the PascalCase form of the test name's subject segment.
A subject may span several underscore-separated words, and the Test Author
chooses where it ends. Every test in `<Subject>Tests` is named
`test_<s>_…`, where `<s>` is `<Subject>` in snake_case
(`RandomStreamTests` holds `test_random_stream_…`). A test goes in the class
with the longest subject that prefixes its name (Q-028).

**L7. Test names.** A test named `test_<subject>_<condition>_<expectation>` in a task
file or a spec is a C# method with exactly that name: lower-case ASCII
letters, digits and underscores, marked `[Fact]` (or `[Theory]` with inline data),
`public void`, taking no parameters unless it is a `[Theory]`. Example:
`test_fx_mul_negative_operand_truncates_toward_negative_infinity`. Names are
unique within their test project. They are not async: the sim is synchronous.

**L8. Who creates what.** The order is T-003 first, then T-001 (Q-014). Fx
merges first, because `ISimClock.MinutesBetween` returns `SimMinutes = Fx`.

| File | Created by | Later changes |
|---|---|---|
| `src/sim/core/AirportSim.Sim.Core.csproj` | every `sim.core` task that finds it absent, byte for byte (L2). In practice this is T-003. | by spec amendment only |
| `src/<layer>/<m>/…csproj` (other modules) | the first task whose writable paths include the directory, per L2 | by spec amendment only |
| `tests/<layer>/<m>/…Tests.csproj` and the parallelisation file (L4) | the Test Author, per L3/L4, in every test branch that needs them (identical additions merge cleanly) | by spec amendment only |
| `AirportSim.sln` (repo root) | the first task to merge a production project, which is T-003. It runs `dotnet new sln --name AirportSim` and adds `src/sim/core` and `tests/sim/core` | T-001 adds `tools/SimHarness`. After that, the first task that creates a module's production project adds that project and its test project. The Planner lists `AirportSim.sln` in that task's writable paths and never releases two such tasks concurrently, because concurrent `.sln` edits conflict |
| `tools/SimHarness/AirportSim.Tools.SimHarness.csproj` | T-001. It is the L3 file with `<OutputType>Exe</OutputType>` inserted as the first property. It drops `IsPackable`, `IsTestProject`, `NuGetAudit` and the package `ItemGroup`, uses the L1 names, and has one `ProjectReference` to `../../src/sim/core/AirportSim.Sim.Core.csproj` | later `tools/SimHarness/**` tasks add `ProjectReference`s only |
| `AirportSim.sln` entry for `tests/tools/simharness` (Q-025) | T-006, which adds the Test Author's project to the solution, as T-003 did for `tests/sim/core`. The Planner lists `AirportSim.sln` in T-006's writable paths | none |

**Everything under `tests/**`, fixtures included, is written by the Test
Author (Q-021).** A worker task's `tests/**` writable path grants nothing:
the path guard blocks workers from `tests/`. A fixture a spec calls "binding
on the Test Author", such as `11` §11.10, is the Test Author's file.

**L9. Tests merge with their implementation.** One test project per module
(L1), shared by every task of that module. A test file that references a type
not yet on `main` would break the build for everyone. So each test branch
carries only the tests of its own task, and those tests reach `main` in the
same merge as that task's implementation, never earlier. Each task's branch
then compiles on its own. A task's CI is green only if the run built and
tested its projects through `AirportSim.sln`. `ci/run-checks.sh` skips build
and tests when the solution is absent, and that skip is **not** green.

**L10. From IDL to C#.** The spec's IDL maps to C# as follows. The worker has
no other choices about public shape.

- `type X = Y` is an alias. No C# type `X` exists; signatures use `Y`
  (`Tick` is `ulong`, `SimMinutes` is `Fx`).
- `int32 int64 uint16 uint32 uint64 bool string bytes` are `int long ushort
  uint ulong bool string byte[]`. An enum with an IDL underlying type uses it.
  Otherwise the enum is `int`, and its members are numbered in declared order
  from 0. **Enum members are PascalCase in C#** (Q-028). An IDL member written
  in snake_case, such as `06`'s `DelayCategory`, becomes its segments with
  each first letter upper-cased and the underscores removed (`late_inbound` →
  `LateInbound`, `atc_flow` → `AtcFlow`). A member already in PascalCase is
  unchanged. The snake_case spelling survives only where the spec puts it in
  data, for example the JSON value `"security_queue"` (`04`). The loader maps
  that value to the member.
- A `struct` or `readonly struct` is a `public readonly struct`. Each member is
  a public get-only property with the IDL name. There is one public
  constructor taking the members in declared order. The only exception is a
  member the spec says a service assigns, such as `Command.Sequence`, which
  the constructor omits and sets to 0; the service's stored copy carries the
  assigned value. Structs with a single `Value` member (ids, `PlayerId`) and
  `EventId` implement `IEquatable<T>`, `==` and `!=`. `EventId` also
  implements `IComparable<EventId>` over `(Tick, Sequence)`.
- `X?` of a struct type is `System.Nullable<X>` (Q-018). An `event X { ... }`
  block is a `public readonly struct X : ISimEvent` under the struct rule.
- An `interface` is a `public interface`. `{ get }` is a get-only property.
- `Name.Method(...) -> R` on a factory is a public static method of
  `public static class Name`. On a type (`Fx.Add`), it is a public static
  member of that type, unless the IDL shows it taking no operand of the type
  (`Fx.ToDisplayString(int)`), in which case it is an instance member.
- Constants (`08` §8.1, `PLAYER_LOCAL`, `SYSTEM_CORE`) are
  `public const` where C# allows it, and `public static readonly` of an
  immutable type otherwise. They keep their IDL names. The §8.1 table lives in
  `public static class SimConstants` in `sim.core`. Constants and
  `static readonly` values of immutable types are not state. They are the only
  static members besides factories (`08` §8.11a).

**L11. Budget tests.** A budget assertion (`03-module-map.md`, "Performance")
is an xUnit test with `[Trait("Category", "Budget")]`. It measures with
`System.Diagnostics.Stopwatch.GetTimestamp()` and `Stopwatch.Frequency` in
`long` arithmetic only, with no `TimeSpan` and no floating point (`08` §8.3).
A measured time is only ever asserted against. It never feeds a sim input.
That is the only use of the clock that "Unit tests never use wall-clock time"
permits. The authoritative budget measurement is still
`tools/SimHarness budget` (`ci/run-checks.sh`).

## Comments and documentation

- Comment *why*, never *what*.
- Every public interface carries a doc comment naming the spec section it
  implements. If there is no such section, the interface should not exist.

## Error handling

- Sim code does not throw for gameplay conditions. Invalid states are data.
- Sim code throws on programmer error (broken invariant) — loudly, with the tick
  number and the state hash.
- Content loading fails hard with the file path and schema violation.
- **Exception types (Q-014, Q-015)** are fixed, and tests assert the exact
  type. A broken invariant during a tick (the cascade limit,
  `MAX_EVENTS_PER_TICK`, a `Blocked` interval left open) throws
  `SimInvariantException` with the tick. The host wraps every exception that
  escapes a tick in one that also carries the world hash (`08` §8.5a). A misused API throws the BCL type
  named where the API is specified: `ArgumentNullException`,
  `ArgumentOutOfRangeException`, `ArgumentException` for a bad argument and
  `InvalidOperationException` for a call in the wrong state (for example
  `Register` after `Build`). `Fx` throws `OverflowException`,
  `DivideByZeroException` and `FormatException` (`08` §8.3). Each of these is
  thrown explicitly by sim code after its own check. None comes from a BCL
  operator or a `checked` context.

## Logging

- Sim layer logs through a deterministic sink; log volume must not affect outcomes.
- Every log line in sim carries the tick number.

## Runtime portability (Mono and CoreCLR)

The sim is compiled **once**, for `netstandard2.1` (`01-architecture.md`, D1).
It is tested on CoreCLR (`net8.0` tests, `tools.simharness`) and shipped on
Unity's Mono. `02-determinism.md`'s contract, "on every machine", covers both.
Any BCL behaviour that is unspecified, differs between the two runtimes, or is
randomised per process must never reach an outcome, an event order, an id or a
hash. The rules below operationalise `02-determinism.md` rules 2, 5 and 7 for
that case. They bind `src/sim/**` and any code that feeds a state hash or a
checkpoint dump.

1. **Hash-collection order.** Enumerating `Dictionary`, `HashSet`, their
   `Keys`/`Values`, or LINQ over any of them never decides an outcome, an
   event order or a hash. The two runtimes' orders are not contract and differ
   after removals. Keyed lookup is fine. Iterate in the declared order of an
   ordered structure, or sort first under rule 3.
2. **No hash code reaches behaviour.** No `GetHashCode()` value is used as a
   seed, an id, a sort key, a bucket choice or a hashed value. This covers
   `string.GetHashCode()` (randomised per process on CoreCLR, not on Mono),
   `System.HashCode` (random per-process seed) and default `object` or
   `ValueType` hash codes (runtime-specific). The only hashes the sim computes
   are FNV-1a (`08-interfaces-core.md` §8.8, §8.9).
3. **Sorts are total.** Every comparison used to sort sim data is a strict
   total order over the items sorted: no two distinct items compare equal.
   Where natural keys can tie, the comparer breaks the tie on a unique id.
   `Array.Sort`, `List<T>.Sort` and span sorts are unstable introsorts, and
   the order in which they leave equal items is not the same across runtimes
   and versions. A total comparer makes the result independent of the
   algorithm.
4. **Strings are ordinal and culture-invariant.** Comparison, equality and
   ordering of strings use `StringComparison.Ordinal`/`StringComparer.Ordinal`.
   Every number parsed from a fixture or content file, and every number
   formatted into a dump, uses `CultureInfo.InvariantCulture`. No
   culture-sensitive `Compare`, `ToUpper` or `ToLower`. Culture data comes
   from ICU or NLS on CoreCLR and from Mono's own tables, and it differs by
   OS as well.
5. **No reflection order.** No behaviour, serialisation or hashing depends on
   the order of `GetFields`, `GetProperties`, `GetMethods` or attributes. That
   order is unspecified and differs between runtimes. Hashing and saving name
   fields explicitly, in their declared order (`08-interfaces-core.md` §8.9).
6. **No memory punning.** Never hash or serialise a struct by reinterpreting
   its bytes (`MemoryMarshal.AsBytes`, `Unsafe.As`, overlapping
   `StructLayout`). Padding bytes and managed layout are not guaranteed to
   match. Feed fields.
7. **API surface.** Only `netstandard2.1` APIs and C# 9. Sim assemblies take
   **no NuGet package references** unless a spec section names the package,
   so the missing .NET 8 APIs cannot be brought back through polyfills. The
   `init` accessors and `record` types of C# 9 need `IsExternalInit`, which
   `netstandard2.1` does not ship. The sim declares no such polyfill and does
   not use them.

The static checks in `ci/` catch only part of this. The cross-runtime gate
(`16-interfaces-host.md` §16.9) is what detects a violation that gets past
review.

## Performance

- Allocation in the per-tick hot path is a rejection criterion.
- Budget assertions live in tests, not in comments.
- Optimise only against a measurement. "This looked slow" is not a reason.
