# Integration Testing — MS4 Delivery Calendar (Activity 2)

Integration tests run MS4 against a **real Postgres** (container started by Aspire `TestAppHost`, reset between tests with Respawn). Docker is required.

## Layers and tools
| Layer | Project / artifact | Tool |
|---|---|---|
| HTTP API end-to-end | `tests/Application.FunctionalTests/Flows`, `Calendars/CalendarsEndpointsTests` | NUnit + `WebApplicationFactory` + `HttpClient` |
| Use cases through MediatR | `Calendars/CalendarAcceptanceTests` | NUnit |
| Repositories / EF mapping | `tests/Infrastructure.IntegrationTests` | NUnit + real DbContext |
| Black-box flows | `src/Api/MS4-Delivery-Calendar.postman_collection.json` | Postman / newman |

## Correct vs incorrect flows (requirement)
**Correct (happy) flow** — `CalendarHttpFlowTests.HappyFlow_Schedule_Address_Resolve_ActiveTomorrow_DeliveryResult`
and Postman *Flow 1*: `POST /schedule` → `POST /addresses` → `POST /resolve-daily-catalog` → `GET /active-tomorrow` → `PATCH /delivery-result` → `GET /calendars/{id}` = `Delivered`.

**Incorrect (unhappy) flows**
| Scenario | Expected | Where |
|---|---|---|
| Same contract twice | 200, same calendar, 1 row in DB | `UnhappyFlow_DuplicateContract…`, Postman Flow 2 |
| Invalid duration | 400 + `errors.Duration` | `UnhappyFlow_InvalidPayload…`, Postman Flow 5 |
| Day not ready for production | absent from `active-tomorrow` | `UnhappyFlow_ActiveTomorrow…` |
| Same delivery result twice | 200 idempotent | `UnhappyFlow_DeliveryResult_ShouldBeIdempotent…` |
| Opposite result after closing | 422 | same test |
| Unknown day | 404 | `UnhappyFlow_DeliveryResult_UnknownDay…` |
| Cancel / reschedule < 48h | 409 | `CalendarsEndpointsTests`, Postman Flow 3 |
| Invalid state transition | 422 | `CalendarsEndpointsTests`, Postman Flow 4 |
| Second calendar for a contract at DB level | `DbUpdateException` (unique index) | `DeliveryDayPersistenceTests` |

## Run
```powershell
dotnet test tests/Infrastructure.IntegrationTests     # 10 tests
dotnet test tests/Application.FunctionalTests         # 23 tests
./scripts/run-newman.ps1                              # 22 requests, 53 assertions
```
Postman manually: import the collection, set `baseUrl` (Aspire dashboard or `http://localhost:8080` for docker compose), run with the Collection Runner.

## Defects found by these tests
1. **Fixed — 400 responses had no `errors` map.** `ProblemDetailsExceptionHandler` called `WriteAsJsonAsync<ProblemDetails>`, serialising the base type and
   dropping `ValidationProblemDetails.Errors`. Clients could not tell which field failed. Found by newman (Postman Flow 5.1), reproduced with a failing
   functional test, fixed by serialising the runtime type.
2. **Stale tests fixed** — `RescheduleDate_ShouldReturn200…` rescheduled onto a date already inside the 15-day calendar (correctly rejected as duplicate).
3. **Doc mismatch (not changed)** — the Scalar summary for `POST /schedule` says a repeat call returns 409; the implemented (and tested) behaviour is 200 with the existing calendar.

## AI integration-test-writer
Agent `.claude/agents/integration-test-writer.md`, skills `integration-test-conventions` and `run-integration-tests`.
Validated environment: see [AI-TEST-ENV.md](AI-TEST-ENV.md#integration-test-writer).
