# Unit Testing — MS4 Delivery Calendar (Activity 1)

Unit tests cover the **Domain** and **Application** layers. They need no Docker, DB, or network.

## Stack
| Concern | Tool |
|---|---|
| Framework | NUnit 4 |
| Mocking | Moq (interaction tests) + hand-written fakes (state tests) |
| Assertions | Shouldly |
| Validators | FluentValidation.TestHelper |
| Coverage | coverlet.collector + ReportGenerator (`dotnet-tools.json`) |

## Projects
- `tests/Domain.UnitTests` — entities, value objects, domain service, exceptions (56 tests).
- `tests/Application.UnitTests` — handlers, validators, pipeline behaviours, adapters, EF repositories/interceptors/seeder on InMemory (79 tests).

## Where mocks are used
| Test | What is mocked | What it proves |
|---|---|---|
| `ResolveDailyCatalogCommandMockTests` | `ICatalogPort`, `ICalendarRepository`, `IProductionOrderRepository`, `IApplicationDbContext` | catalog called once per `Scheduled` day, never when none; defaults to tomorrow; **no save** if MS2 call throws |
| `PipelineBehavioursTests` | `IValidator<T>`, `ILogger<T>`, `RequestHandlerDelegate` | validation short-circuits `next`; errors are logged and rethrown; slow requests warn |
| `InfrastructureAdaptersTests` | `TimeProvider` | `SystemClock` delegates to the provider |

Handlers for the other use cases use fakes in `tests/Application.UnitTests/Common/Fakes` (`FakeCalendarRepository`, `TestClock`, …).

## Run
```powershell
dotnet test tests/Domain.UnitTests
dotnet test tests/Application.UnitTests
./scripts/coverage.ps1            # HTML report in coverage-report/index.html
```
(`dotnet test` takes one project per call.)

## Coverage (unit tests only, `coverage.runsettings`) — final project gate: >= 80%
`./scripts/coverage.ps1` fails below 80% line coverage (`-Threshold` to change).

| Assembly | Line coverage |
|---|---|
| Application | 99.5% |
| Domain | 99.4% |
| Infrastructure | 96% (EF configurations, interceptors, seeder, repositories, DI, HTTP catalog adapter) |
| **Total** | **98.1%** lines, 89.3% branches |

Scope: Domain + Application + Infrastructure assemblies. EF migrations and generated code are excluded (`coverage.runsettings`).
The `Api` project (Minimal API endpoints, exception handler) is not instrumented here: it is covered end to end by the HTTP integration flows.
Infrastructure runs against the EF **InMemory** provider (no Docker, no native libs); relational behaviour (unique index per contract, Npgsql mapping,
migrations) is proven by `Infrastructure.IntegrationTests` against real Postgres.

## Fixed along the way
`RescheduleDeliveryDayCommandTests.Handle_ShouldUpdateDate_WhenOutsideLockWindow` was failing: it rescheduled to Aug 20,
which is already a day of the 15-day calendar starting Aug 10, so the (correct) duplicate-date rule rejected it. The test now uses Aug 30.

## AI test-writer
See [AI-TEST-ENV.md](AI-TEST-ENV.md): agent `.claude/agents/test-writer.md` with skills `unit-test-conventions` and `run-unit-tests-and-coverage`.
