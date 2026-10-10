# Contract Testing — MS4 Delivery Calendar (Activity 3)

Tool: **PactNet 5.0.1** (Pact spec V4). Contracts are written by the consumer, committed in `pacts/`, and verified by the provider.

```
 MS3 (scheduling)  --pact-->  MS4 (this service)  --pact-->  MS2 (catalog)
   consumer                    provider | consumer             provider
        pacts/MS3-MS4DeliveryCalendar.json      pacts/MS4DeliveryCalendar-MS2Catalog.json
        VERIFIED here (Contract.Provider.Tests)  verified by MS2's team (not in this repo)
```

## Projects
| Project | Role |
|---|---|
| `tests/Contract.Consumer.Tests` | Generates both pact files. MS3 interactions use a small client modelling what MS3 reads; MS4 -> MS2 interactions exercise the **real** `HttpCatalogPort` |
| `tests/Contract.Provider.Tests` | MS4 as provider: real API on a Kestrel port + Aspire Postgres + a provider-states endpoint; verifies `MS3-MS4DeliveryCalendar.json` |

## Interactions
### MS3 -> MS4 (verified by MS4)
| # | Interaction | Provider state | Pinned |
|---|---|---|---|
| 1 | `POST /api/v1/schedule` (15 days) | no calendar exists for the contract | 200, `calendarId` int, `days` (>= 15) each with `id`, `status` |
| 2 | `POST /api/v1/schedule` duration 20 | none | 400, `status`, `errors.Duration` |
| 3 | `GET /api/v1/calendars/active-tomorrow` | tomorrow has one day ready for production | 200, array (>= 1) of `packId, patientId, planId, address, latitude, longitude` |
| 4 | `GET /api/v1/calendars/1` | a calendar with id 1 exists | 200, `id` int, `status` (Active\|Completed), `days` (>= 1) each with `id`, `status` |
| 5 | `GET /api/v1/calendars/999` | no calendar with id 999 exists | 404, `status` |

Ids are DB generated, so the request path uses the literal id `1`; the provider state resets identity sequences after the Respawn reset (`ProviderHost.ResetDatabaseAsync`) and asserts the seeded calendar got id 1.

### MS4 -> MS2 (consumer expectation, `HttpCatalogPort`)
| # | Interaction | Provider state | Pinned |
|---|---|---|---|
| 1 | `GET /catalog/plans/{planId}/day/{date}` | plan has a recipe for the date | 200 `{recipeRef, portions}` |
| 2 | same path, unknown plan | plan does not exist | 404 -> `NotFoundException` |

## Run
```powershell
./scripts/run-pact.ps1                 # consumer (writes pacts/) then provider verification (Docker)
./scripts/run-pact.ps1 -ConsumerOnly   # no Docker
```
Result: consumer tests (MS3 + MS2) all pass; the provider verification test covers every MS3 interaction in the pact file (see the log: `Verifying a pact between MS3 and MS4DeliveryCalendar`).

**The check can fail:** changing `"min": 15` to `99` in the pact file makes the provider test fail; restoring it makes it pass again.

## Code added to production
`src/Infrastructure/Catalog/HttpCatalogPort.cs` — the real MS2 adapter that the consumer pact needs. It is **opt-in**:
`Catalog:UseMock` defaults to `true` (existing mock keeps working everywhere); set `Catalog:UseMock=false` and `Catalog:BaseUrl` to call MS2.

## Caveats
- The MS2 contract is **not agreed upstream** (CLAUDE.md §5 gap 2). This pact is MS4's expectation; share it with MS2 and have them verify before enabling the real adapter.
- MS3's code is not in this repository: the MS3 side is a minimal client in the consumer test, modelling only fields MS3 reads (per its endpoint contract in CLAUDE.md §0).
- Provider states are arranged through MS4's public HTTP API, so verification reflects real behaviour.

## Publishing to a broker (next step)
`pact-broker publish pacts --consumer-app-version <git sha> --branch <branch>`, then switch the verifier from `WithFileSource` to `WithPactBrokerSource` and add `can-i-deploy` to CI.

## AI pact-writer
Agent `.claude/agents/pact-writer.md`, skills `pact-consumer-conventions`, `pact-provider-verification`. Environment: [AI-TEST-ENV.md](AI-TEST-ENV.md#pact-writer).
