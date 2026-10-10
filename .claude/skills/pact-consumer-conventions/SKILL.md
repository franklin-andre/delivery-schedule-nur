---
name: pact-consumer-conventions
description: How to write PactNet 5 consumer tests in MS4 (tests/Contract.Consumer.Tests) - setup, interactions, matchers, JSON casing pitfalls, where pact files go. Use before adding or editing a consumer interaction.
---

# Pact consumer conventions (PactNet 5.0.1, V4 spec)

## Files
| File | Role |
|---|---|
| `tests/Contract.Consumer.Tests/PactSupport.cs` | `PactSupport.Create(consumer, provider)`; pact dir = `<repo>/pacts`; camelCase default JSON |
| `Ms3ConsumerTests.cs` + `Ms3DeliveryCalendarClient.cs` | MS3 -> MS4: tiny client modelling only what MS3 reads |
| `Ms4CatalogConsumerTests.cs` | MS4 -> MS2: exercises the REAL `HttpCatalogPort` |
Pact file name = `<Consumer>-<Provider>.json`.

## Template
```csharp
using PactNet;            // required: WithHttpInteractions is an extension in this namespace
using PactNet.Matchers;

private PactNet.IPactBuilderV4 _pact = null!;
[SetUp] public void SetUp() => _pact = PactSupport.Create("MS3", "MS4DeliveryCalendar").WithHttpInteractions();  // fresh per test

[Test]
public async Task X_ShouldY_WhenZ()
{
    _pact.UponReceiving("a request to ...")
        .Given("provider state name")                       // must be handled by ProviderHost.ArrangeAsync
        .WithRequest(HttpMethod.Get, "/api/v1/...")
        .WillRespond().WithStatus(HttpStatusCode.OK)
        .WithHeader("Content-Type", "application/json; charset=utf-8")
        .WithJsonBody(new { id = Match.Integer(1) });

    await _pact.VerifyAsync(async ctx =>
    {
        var client = new Ms3DeliveryCalendarClient(new HttpClient { BaseAddress = ctx.MockServerUri });
        var result = await client.DoAsync();                  // real client code against the Pact mock server
        result.ShouldNotBeNull();
    });
}
```

## Matching rules
- Match by type, not value: `Match.Type`, `Match.Integer`, `Match.Decimal`, `Match.Regex(example, regex)`, `Match.MinType(example, min)` for arrays.
- Pin exactly only what the consumer depends on: status code, field names, enum spellings (`"Pending"`, not `"PENDING"` — MS4 serialises enums as PascalCase strings).
- Response bodies may contain extra provider fields; consumers must ignore them.
- `MinType(example, 15)`: the Pact mock returns ONE example element, so consumer assertions must be `ShouldNotBeEmpty()`, not `Count >= 15`; the min rule is enforced against the real provider.

## Path ids
Ids are DB generated but the provider restarts sequences per state, so pin a literal id and make the state say it: `Given("a calendar with id 1 exists")` + `GET /api/v1/calendars/1`; for 404 use `Given("no calendar with id 999 exists")`.
Enums: calendar `status` is `Active|Completed`; day `status` is `Pending|Scheduled|ReadyForProduction|InProduction|Delivered|Failed|Cancelled`.
Pin only the fields the consumer reads (e.g. `CalendarVm` also has patientId, contractId, planId, duration, startDate — ignore unless needed).

## Pitfalls
1. **JSON casing.** `DefaultJsonSettings` camelCases anonymous-object property names. Provider keys that are PascalCase (the 400 `errors` map: `Duration`, `Latitude`) must be written as `new Dictionary<string, object> { ["Duration"] = ... }`.
2. Request body casing/dates: `startDate` is an ISO date string `"2030-01-01"`; ids are GUID strings.
3. Interactions without `Given` produce an empty provider state — the provider must tolerate it.
4. Every interaction description must be unique within a pact file.
5. The MS2 contract is not yet agreed upstream (CLAUDE.md §5 gap 2: three conflicting shapes). The MS4->MS2 pact documents MS4's expectation; it must be shared with MS2 before `Catalog:UseMock=false` is used.

## Run
`./scripts/run-pact.ps1 -ConsumerOnly` (no Docker) — then check `git diff pacts/`.
