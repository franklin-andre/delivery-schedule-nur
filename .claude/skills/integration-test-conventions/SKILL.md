---
name: integration-test-conventions
description: Patterns for MS4 integration tests - Aspire+Postgres setup, TestBase/Respawn, HttpClient flow tests, repository tests, Postman flows, and the happy+unhappy rule. Use before writing or reviewing any test in tests/Application.FunctionalTests or tests/Infrastructure.IntegrationTests.
---

# MS4 integration-test conventions

## What is where
| Project | Drives | Use for |
|---|---|---|
| `tests/Application.FunctionalTests/Flows` | real HTTP (`FunctionalTestSetup.CreateHttpClient()`) | end-to-end happy/unhappy flows through the public API |
| `tests/Application.FunctionalTests/Calendars` | HTTP (`CalendarsEndpointsTests`) and MediatR (`CalendarAcceptanceTests` via `TestApp.SendAsync`) | per-endpoint status codes; acceptance criteria |
| `tests/Infrastructure.IntegrationTests` | repositories on a real `ApplicationDbContext` | owned-type round trips, unique constraints, queries |
| `src/Api/MS4-Delivery-Calendar.postman_collection.json` | newman against the running API | manual/CI-style black-box flows |

All of them use a Postgres container started by Aspire (`TestAppHost`) — Docker is required.

## Mandatory shape: one correct + one incorrect flow
- Happy: Schedule → SetAddress → resolve-daily-catalog → active-tomorrow → delivery-result → GET calendar shows `Delivered`.
- Unhappy (pick what the change touches): duplicate contract (returns the SAME calendar, 200, nothing new persisted), invalid payload 400
  (body has `errors` with the field name), unknown id 404, <48h cancel/reschedule 409, invalid transition 422, conflicting delivery result 422,
  repeated identical delivery result = idempotent 200.

## HTTP test template
Usings you will need (copy from `Flows/CalendarHttpFlowTests.cs`): `System.Net`, `System.Net.Http.Json`, `System.Text.Json(.Serialization)`,
`delivery_schedule_management.Api.Endpoints` (request records e.g. `CreateScheduleRequest`, `SetAddressRequest`),
`...Application.Calendars.Commands.<UseCase>` (result records), `...Application.Calendars.Common` (`CalendarVm`), `...Domain.Enums`, `Microsoft.AspNetCore.Mvc`.
`NUnit`, `Shouldly`, `TestBase` come from `GlobalUsings.cs`. Namespace: `delivery_schedule_management.Application.FunctionalTests.<Folder>`.
```csharp
public class MyFlowTests : TestBase               // TestBase.SetUp resets the DB with Respawn before every test
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };   // REQUIRED: server serialises enums as strings

    [Test]
    public async Task UnhappyFlow_X_ShouldReturn409()
    {
        using var client = FunctionalTestSetup.CreateHttpClient();
        var response = await client.PostAsJsonAsync("/api/v1/schedule", new CreateScheduleRequest(...));
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Status.ShouldBe(409);
    }
}
```
Use `TestApp.SendAsync/FindAsync/AddAsync/CountAsync` only to arrange state or to assert persistence (e.g. `CountAsync<DeliveryCalendar>() == 0` after a 400).

For **400** responses read `ValidationProblemDetails` (not `ProblemDetails`) and assert `problem.Errors.ShouldContainKey("Latitude")` —
keys are the PascalCase FluentValidation property names. Other statuses: `ProblemDetails`.

## Request/response shapes (the real ones; CLAUDE.md §0 is approximate)
- `SetAddressRequest(DayId, Street, City, Latitude, Longitude, References?, Notes?)` — flat, no nested address object. Calendar read model: `AddressDto(Street, City, References, Notes, Latitude, Longitude)`.
- `CreateScheduleRequest(ContractId, PatientId, PlanId, Duration, StartDate)`; `RescheduleDateRequest(DayId, NewDate)`; `CancelDayRequest(DayId)`; `ApplyDeliveryResultRequest(Result, PhotoUrl?, SignatureUrl?)`.
- Setting an address is NOT subject to the 48h rule (only reschedule/cancel are).

## Repository test template
Inherit `RepositoryTestBase`; use a **fresh** `IntegrationTestSetup.CreateContext()` for each phase (write, mutate, read) so the read comes from the DB, not the change tracker.
Unique constraints (one calendar per contract, one production order per delivery date) surface as `DbUpdateException`.

## Domain/API facts that cause false failures
- Dates: a 15-day calendar starting D occupies D..D+14. Rescheduling a day onto another day of the same calendar → 400 (duplicate date). Use D+20.
- 48h rule is measured to the **date at 00:00 UTC**: a day dated today/tomorrow is locked; D+3 or later is open.
- Day ids are DB-generated: read them from the create response, never hard-code.
- POST /schedule for an existing contract returns 200 with the existing calendar (not 409) even though the Scalar summary text says 409.
- active-tomorrow lists only `ReadyForProduction` days that were consolidated by resolve-daily-catalog; pass `?date=` to target a date.
- Error mapping: ValidationException 400 (`errors` map), NotFound 404, DeliveryWindowLocked 409, InvalidTransition 422, argument exceptions 422.

## Postman flows
Each request carries `pm.test` assertions; ids chain through collection variables (`calendarId`, `dayId_*`); dates are generated in pre-request scripts.
Keep flows self-contained (create their own contract/patient/plan GUIDs). Add new flows as new top-level folders named `Flow N — <name>`.
