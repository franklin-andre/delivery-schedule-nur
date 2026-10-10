---
name: pact-provider-verification
description: How MS4 verifies pacts as provider (tests/Contract.Provider.Tests) - real Kestrel host, Aspire Postgres, provider-state endpoint, adding a state, debugging failures, Docker preflight. Use when adding provider states or when a verification fails.
---

# Pact provider verification (MS4 as provider)

## Preflight (stop if it fails)
```powershell
docker info --format '{{.ServerVersion}}'     # Docker Desktop must be running (Aspire starts Postgres)
dotnet --version
```

## Moving parts (`tests/Contract.Provider.Tests`)
- `ProviderHost.cs` (`[SetUpFixture]`): starts Postgres via `TestAppHost`, boots the REAL API with `WebApplicationFactory<Program>.UseKestrel(0)` + `StartServer()` (Pact needs a real URL), and a second tiny Kestrel app exposing `POST /provider-states`.
- `Ms3ContractVerificationTests.cs`: `PactVerifier("MS4DeliveryCalendar")` `.WithHttpEndpoint(ProviderUri)` `.WithFileSource(pacts/MS3-MS4DeliveryCalendar.json)` `.WithProviderStateUrl(StatesUri/provider-states)` `.Verify()`.
- Verifier output is forwarded to the NUnit log (`NUnitOutput`).

## Adding a provider state
1. Copy the exact `Given("...")` string from the consumer test.
2. Add a branch in `ProviderHost.ArrangeAsync`. The DB is reset before every state (Respawn deletes rows AND `ResetDatabaseAsync` restarts all identity sequences), so each state starts empty and ids restart at 1. A request path with an id (`/calendars/1`) therefore works: the state seeds exactly one calendar and asserts it got id 1. Restarting sequences counts as "resetting" the DB and is allowed.
3. Arrange data through MS4's public HTTP API (see `SeedReadyDayForTomorrowAsync`: schedule -> address -> resolve-daily-catalog). Use dates relative to `DateTime.UtcNow` (`active-tomorrow` defaults to tomorrow).
4. States whose name starts with `no calendar ...` are the empty-DB state (use for 404 contracts). Unknown state => the handler throws => verifier reports "setup state change handlers has failed". An EMPTY state (no `Given`) must not throw.

## Reading a failure
The state endpoint writes the real exception of a failed state to the test log (`Provider state '...' failed: ...`) — look there first.
Run `dotnet test tests/Contract.Provider.Tests --logger "console;verbosity=detailed"`; the verifier prints per-interaction status, header and body diffs.
| Symptom | Cause |
|---|---|
| `setup state change handlers has failed` | state name mismatch/unhandled, or state seeding HTTP call failed (500) |
| body mismatch `Expected ... but was ...` | real contract break: provider changed shape, or consumer expects something MS4 never returned |
| `Pact file ... not found` | consumer tests not run; run `./scripts/run-pact.ps1` |
| key casing mismatch (`duration` vs `Duration`) | consumer used an anonymous object; use a Dictionary for PascalCase keys |

A real break is **reported, not "fixed"** by loosening the pact or editing `src/` from this agent.

## Prove the check can fail
Temporarily edit a value in the pact JSON (e.g. `"min": 15` -> `99`), expect `Con error`/failure, then restore the file (`git checkout pacts/`).

## Run
```powershell
./scripts/run-pact.ps1                          # consumer then provider
dotnet test tests/Contract.Provider.Tests       # provider only (needs pacts/ present)
```
Definition of done: consumer green, pact JSON regenerated and committed, provider green, nothing under `src/` modified.

## Not covered here
The MS4 -> MS2 pact is verified by MS2 (not in this repo). Until they verify it, it is MS4's declared expectation only.
