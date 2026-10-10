# Test fixtures

JSON fixtures are copied next to the test assembly (`PreserveNewest`) and loaded fully
offline through `Ballhog.Tests.Infrastructure.FixtureLoader`. They are small, representative
samples — never wholesale copies of vendor payloads.

## Provenance

| Fixture | Source shape | Status | Used by |
| --- | --- | --- | --- |
| `placeholder.json` | C-002 test-substrate placeholder | Synthetic (no vendor analogue) | `SubstrateSmokeTests` — proves the loader works |
| `teams.commonteamyears.json` | Stats NBA v3 `commonteamyears` response (`LeagueID=00`) for the 30 active franchises, plus 3 historical rows (SEA/VAN/NJN) kept to exercise MAX_YEAR filtering | **SAMPLE** — hand-authored, not yet captured live | `StatsNbaProviderTests`, `BallhogClientEndToEndTests` (offline) |

`teams.commonteamyears.json` is JSONC: the provenance comment and trailing commas are human
aids. Tests re-emit it as strict JSON (`JsonNode.ToJsonString()`) so the provider only ever
sees what the real Stats API would send.

### Pending live verification

The teams sample is **pending live parity**. The `Category=Live` test
`BallhogClientEndToEndTests.GetTeamsAsync_returns_the_30_active_teams_through_the_real_di_composition`
is the intended verification: when run with `BALLHOG_LIVE=1` it drives the real `AddBallhog`
container against the live Stats NBA API. Once that passes, this fixture may be promoted from
SAMPLE to **verified live capture** (reducing it to the fields the provider reads).

## Adding a fixture

1. Capture or hand-author the smallest payload that exercises one behavior.
2. Name it `<resource>[.<variant>].json` (e.g. `teams.commonteamyears.json`).
3. Record its provenance here: source shape, whether it is a verified live capture or a
   documented sample, and which tests consume it. If it is a sample, name the `Category=Live`
   test that will verify it.
4. Keep provenance comments in the fixture only where they clarify the data; tests must not
   depend on them (parse via `FixtureLoader`, which skips comments/trailing commas).
