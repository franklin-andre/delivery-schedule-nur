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
- `tests/Domain.UnitTests` — entities, value objects, domain service, exceptions (50 tests).
- `tests/Application.UnitTests` — handlers, validators, pipeline behaviours, adapters without a DB (56 tests).

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

## Coverage (unit tests only, `coverage.runsettings`)
| Assembly | Line coverage |
|---|---|
| Application | 94.6% |
| Domain | 76.8% (BaseEntity/ValueObject helpers unused by unit paths) |
| Infrastructure | 2.1% — EF/repositories are covered by Docker-based integration tests (Activity 2) |
| **Total** | **53.6%** line, 68.8% branch |

Business-critical classes (`DeliveryDay`, `DeliveryCalendar`, all handlers, all validators, all behaviours) are at 90–100%.

## Fixed along the way
`RescheduleDeliveryDayCommandTests.Handle_ShouldUpdateDate_WhenOutsideLockWindow` was failing: it rescheduled to Aug 20,
which is already a day of the 15-day calendar starting Aug 10, so the (correct) duplicate-date rule rejected it. The test now uses Aug 30.

## AI test-writer
See [AI-TEST-ENV.md](AI-TEST-ENV.md): agent `.claude/agents/test-writer.md` with skills `unit-test-conventions` and `run-unit-tests-and-coverage`.
