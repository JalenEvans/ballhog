# Ballhog Tests

Test project for the Ballhog.Basketball library (`net8.0` + `net10.0`). The suite spans the
full C-002..C-008 range: the offline test substrate plus behavior tests for the
client, the Stats NBA provider, and the cache-aside composition, and a reflection
architecture guard over the public surface.

## Stack

- **xUnit v3 (VSTest-only flavor)** — `xunit.v3.core.mtp-off` + `xunit.v3.assert`
  + `xunit.analyzers`, driven by `Microsoft.NET.Test.Sdk` +
  `xunit.runner.visualstudio`.
  - The default `xunit.v3` meta-package forces Microsoft Testing Platform (MTP)
    into the project, and MTP's MSBuild targets refuse the VSTest target on the
    .NET 10 SDK. The `mtp-off` flavor is the officially documented way to keep
    the classic `dotnet test` (VSTest) stack: a plain test library, so traits,
    filters, and runsettings behave exactly as documented below.
  - xUnit v3's `Assert.SkipUnless` gives us first-class **dynamic skipping** for
    live tests with no extra package.
- **Microsoft.Extensions.DependencyInjection**, **Microsoft.Extensions.Http**,
  **Microsoft.Extensions.TimeProvider.Testing** (`FakeTimeProvider`) — wired up
  for the DI, `IHttpClientFactory`, and deterministic-time tests.

All package versions are the latest stable that still target `net8.0` (so the
same set works on both target frameworks).

## Running the offline suite

The commands below are run from the package root: `Ballhog.Basketball/`.

Live tests (marked `[Trait("Category", "Live")]`) are excluded **by default**:

```bash
dotnet test tests/Ballhog.Tests/Ballhog.Tests.csproj -c Release
```

The default run filter (`Category!=Live`) comes from `test.runsettings`, which is
picked up automatically via `<RunSettingsFilePath>` in the test project. This is
the same command CI uses; it needs no network at test time (fixtures are local
files copied to the output directory; restore only needed package downloads once).

To run the whole solution (library + tests):

```bash
dotnet build Ballhog.Basketball.sln -c Release
```

## Running live tests

Live tests call the real NBA API and are **skipped** (never failed) unless
`BALLHOG_LIVE=1` is set. They use the `LiveTestGate` helper:

```csharp
[Fact]
[Trait("Category", "Live")]
public void Something_that_hits_the_real_api()
{
    LiveTestGate.SkipUnlessEnabled();   // skips the test unless BALLHOG_LIVE=1
    // ... real network calls ...
}
```

Two steps are required to actually run them, because VSTest ANDs the runsettings
filter with any command-line filter. Use the optional `live.runsettings` (which
has no `TestCaseFilter`) plus an explicit filter:

```bash
# Live tests run (they may take a while and require network):
BALLHOG_LIVE=1 dotnet test tests/Ballhog.Tests/Ballhog.Tests.csproj -c Release \
  --settings tests/Ballhog.Tests/live.runsettings --filter "Category=Live"

# Same command WITHOUT the env var: live tests are reported as SKIPPED:
dotnet test tests/Ballhog.Tests/Ballhog.Tests.csproj -c Release \
  --settings tests/Ballhog.Tests/live.runsettings --filter "Category=Live"
```

And when you only want to confirm which tests are gated as live (no run):

```bash
dotnet test tests/Ballhog.Tests/Ballhog.Tests.csproj -c Release --filter "Category=Live"
```

## Fixture convention

- JSON fixtures live in `tests/Ballhog.Tests/Fixtures/**/*.json`.
- They are copied to the test output directory (`PreserveNewest`), so they are
  read via `AppContext.BaseDirectory` and never depend on the working directory
  or a host.
- Load them with the typed helper `Ballhog.Tests.Infrastructure.FixtureLoader`:
  - `FixtureLoader.GetPath("season")` → absolute path to `Fixtures/season.json`
  - `FixtureLoader.LoadDocument("season")` → raw `System.Text.Json.JsonDocument`
  - `FixtureLoader.LoadObject("season")` → `JsonObject`
  - `FixtureLoader.Load<SeasonDto>("season")` → deserialized `T`
  - the `.json` extension is added automatically if omitted
- `Fixtures/placeholder.json` is a placeholder that exists only to exercise the
  loading mechanism; replace or delete it as real fixtures arrive.
- Fixture files should be small and representative (trimmed responses or
  hand-written samples), never wholesale copies of vendor payloads.

## Live test convention

- Every live test MUST carry `[Trait("Category", "Live")]` AND call
  `LiveTestGate.SkipUnlessEnabled()` as its first statement.
- Keep the number of live tests small; they are the only tests that need
  network (stats.nba.com also requires residential egress — see the package
  README, [`../README.md`](../README.md)).
- Live tests here are keyless: the public stats.nba.com endpoint needs no API
  key. If future live tests ever need other configuration, read it from
  environment variables through the gate, never hard-code secrets.

## Architecture-test approach

- Architecture rules are written with plain **`System.Reflection`** over the
  compiled `Ballhog.Basketball` assembly; no architecture-test package is needed. Typical
  rules for this repo: only the approved types are public, and internal
  implementation types stay hidden.
- Rules examine the compiled `Ballhog.Basketball` assembly (a project reference), so they
  run fully offline and against both TFMs.
- Keep architecture tests coarse: assert the invariant that protects the design
  (e.g., no forbidden dependencies), not cosmetic details.