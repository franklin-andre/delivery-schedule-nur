---
name: unit-test-conventions
description: Conventions and templates for MS4 unit tests (NUnit 4, Moq, Shouldly, FluentValidation.TestHelper). Use before writing or reviewing any unit test in tests/Domain.UnitTests or tests/Application.UnitTests.
---

# MS4 unit-test conventions

## Stack and layout
| Layer under test | Project | Mirror path |
|---|---|---|
| Domain (entities, VOs, services) | `tests/Domain.UnitTests` | `Entities/`, `ValueObjects/`, `Services/` |
| Application handlers | `tests/Application.UnitTests/Calendars/Commands|Queries` | one file per use case |
| Validators / behaviours | `tests/Application.UnitTests/Common/Validators|Behaviours` | |
| Infrastructure adapters without a DB | `tests/Application.UnitTests/Infrastructure` | |

Packages: NUnit 4, Moq, Shouldly, `FluentValidation.TestHelper`. No xUnit, no FluentAssertions.

## Naming and shape
- `public class <Subject>Tests`; method `Method_ShouldOutcome_WhenCondition` (e.g. `Cancel_ShouldThrow_WhenInsideFortyEightHourWindow`).
- Arrange / Act / Assert, separated by blank lines. Use Shouldly: `x.ShouldBe(..)`, `await Should.ThrowAsync<T>(() => ...)`.
- Parameterised cases: `[TestCase(...)]`, not loops.

## Moq vs hand-written fakes
- **State-based** (assert on returned/persisted result): use fakes in `Common/Fakes` (`FakeCalendarRepository`, `FakeApplicationDbContext`, `FakeCatalogPort`, `TestClock`).
- **Interaction-based** (assert a port was/wasn't called, how many times, with what): use `Mock<T>` + `Verify(..., Times.X)`.
  Typical: `Mock<ICatalogPort>`, `Mock<IApplicationDbContext>` (`SaveChangesAsync` never called on failure), `Mock<ILogger<T>>`.
- Mocks are created in `[SetUp]` (NUnit reuses the fixture instance; field-initialised mocks leak invocations between tests).
- Verify logger calls via `logger.Verify(l => l.Log(level, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times)`.

## Domain rules to always cover
- 48h rule: exact boundary (`Date - Now == 48h` allowed / `< 48h` throws `DeliveryWindowLockedException`).
- Invalid transitions throw `InvalidDeliveryDayTransitionException`.
- VOs: each constructor guard, equality by value, immutability (`AddDays` returns a new instance).
- Calendar creation yields N days (15 | 30), all `Pending`.

## Handler template
```csharp
public class CancelDeliveryDayCommandTests
{
    private Mock<IApplicationDbContext> _context = null!;

    [SetUp]
    public void SetUp() => _context = new Mock<IApplicationDbContext>();

    [Test]
    public async Task Handle_ShouldNotSave_WhenInsideLockWindow()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));
        // arrange repo/calendar ...
        await Should.ThrowAsync<DeliveryWindowLockedException>(() => handler.Handle(cmd, CancellationToken.None));
        _context.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

## Validator template
```csharp
var result = new CreateScheduleCommandValidator().TestValidate(cmd);
result.ShouldHaveValidationErrorFor(x => x.Duration);
```

## EF-backed unit tests (repositories, interceptors, seeder)
Use `Infrastructure/InMemoryDb` (EF InMemory provider, real model/configurations, no Docker). Pass interceptors to its constructor. Do NOT add SQLite
(its native package is flagged by NuGet audit and warnings are errors). InMemory cannot test unique indexes or `MigrateAsync` — that is integration-test territory.
`IMediator.Publish(BaseEvent)` binds to the generic overload: verify with `m.Publish(It.IsAny<BaseEvent>(), It.IsAny<CancellationToken>())`.
Coverage gate: `./scripts/coverage.ps1` must stay >= 80%.

## Layout, namespaces, naming details
- Mirror `src/Application/Common/<X>` under `tests/Application.UnitTests/Common/<X>` (e.g. `Models/`, `Exceptions/`).
- Namespace = `delivery_schedule_management.<Project>.UnitTests.<folder path>` (e.g. `delivery_schedule_management.Application.UnitTests.Common.Models`).
- Constructors/factories/exceptions: `Constructor_ShouldThrow_WhenX`, `Success_ShouldBeTrue_WhenX`, `Throw_ShouldBeCatchable_WhenX` — same `Method_ShouldOutcome_WhenCondition` shape.
- Some types have `internal` constructors and the repo has no `InternalsVisibleTo`: test them through their public factories (e.g. `Result.Success()` / `Result.Failure()`).
- "Prove a test can fail": reason about the assertion, or temporarily mutate the **test's** inputs. Never edit `src/` to do it.

## Pitfalls seen in this repo
- Domain state a test needs (e.g. day `Scheduled`) must be reached through public methods (`SetAddress`) — setters are private.
- A 15-day calendar starting Aug 10 occupies Aug 10–24: rescheduling onto one of those dates fails with `ValidationException` (duplicate date). Pick a date outside the range.
- `DeliveryDay.Id` is 0 until saved; fakes assign ids in `Add`.
- `TreatWarningsAsErrors` is on; nullable annotations apply to tests (`null!` for `[SetUp]` fields).
