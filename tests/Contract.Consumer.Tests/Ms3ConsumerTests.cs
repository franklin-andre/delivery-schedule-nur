using PactNet;
using System.Net;
using PactNet.Matchers;

namespace delivery_schedule_management.Contract.Consumer.Tests;

/// <summary>
/// Consumer side of the MS3 -> MS4 contract (endpoints #1 and #2 of CLAUDE.md §0). Writes
/// pacts/MS3-MS4DeliveryCalendar.json, which MS4 verifies in tests/Contract.Provider.Tests.
/// Three interactions: schedule (ok), schedule (rejected), active-tomorrow.
/// </summary>
public class Ms3ConsumerTests
{
    private const string ContractId = "11111111-1111-1111-1111-111111111111";
    private const string PatientId = "22222222-2222-2222-2222-222222222222";
    private const string PlanId = "33333333-3333-3333-3333-333333333333";
    private const string UuidRegex = "^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$";

    private PactNet.IPactBuilderV4 _pact = null!;

    [SetUp]
    public void SetUp() => _pact = PactSupport.Create(PactSupport.Ms3, PactSupport.Ms4).WithHttpInteractions();

    [Test]
    public async Task CreateSchedule_ShouldReturnCalendarWithEmptyPendingDays()
    {
        _pact.UponReceiving("a request to create a 15-day schedule for a new contract")
            .Given("no calendar exists for contract 11111111-1111-1111-1111-111111111111")
            .WithRequest(HttpMethod.Post, "/api/v1/schedule")
            .WithHeader("Content-Type", "application/json; charset=utf-8")
            .WithJsonBody(new
            {
                contractId = ContractId,
                patientId = PatientId,
                planId = PlanId,
                duration = 15,
                startDate = "2030-01-01"
            })
            .WillRespond()
            .WithStatus(HttpStatusCode.OK)
            .WithHeader("Content-Type", "application/json; charset=utf-8")
            .WithJsonBody(new
            {
                calendarId = Match.Integer(1),
                days = Match.MinType(new
                {
                    id = Match.Integer(1),
                    status = Match.Regex("Pending", "^(Pending|Scheduled|ReadyForProduction|InProduction|Delivered|Failed|Cancelled)$")
                }, 15)
            });

        await _pact.VerifyAsync(async ctx =>
        {
            var client = new Ms3DeliveryCalendarClient(new HttpClient { BaseAddress = ctx.MockServerUri });

            var (status, body) = await client.CreateScheduleAsync(new Ms3DeliveryCalendarClient.ScheduleRequest(
                Guid.Parse(ContractId), Guid.Parse(PatientId), Guid.Parse(PlanId), 15, new DateOnly(2030, 1, 1)));

            status.ShouldBe(HttpStatusCode.OK);
            body.ShouldNotBeNull();
            body.CalendarId.ShouldBeGreaterThan(0);
            body.Days.ShouldNotBeEmpty(); // the mock returns the example element; the "min 15" rule is enforced against MS4
        });
    }

    [Test]
    public async Task CreateSchedule_ShouldBeRejectedWithFieldErrors_WhenDurationIsNot15Or30()
    {
        _pact.UponReceiving("a request to create a schedule with an unsupported duration")
            .WithRequest(HttpMethod.Post, "/api/v1/schedule")
            .WithHeader("Content-Type", "application/json; charset=utf-8")
            .WithJsonBody(new
            {
                contractId = ContractId,
                patientId = PatientId,
                planId = PlanId,
                duration = 20,
                startDate = "2030-01-01"
            })
            .WillRespond()
            .WithStatus(HttpStatusCode.BadRequest)
            .WithHeader("Content-Type", "application/json; charset=utf-8")
            .WithJsonBody(new
            {
                status = Match.Integer(400),
                // Dictionary, not an anonymous type: the key must stay PascalCase ("Duration") —
                // the camelCase naming policy only rewrites property names, not dictionary keys.
                errors = new Dictionary<string, object>
                {
                    ["Duration"] = Match.MinType("Duration must be 15 or 30 days.", 1)
                }
            });

        await _pact.VerifyAsync(async ctx =>
        {
            var client = new Ms3DeliveryCalendarClient(new HttpClient { BaseAddress = ctx.MockServerUri });

            var (status, error) = await client.CreateInvalidScheduleAsync(new Ms3DeliveryCalendarClient.ScheduleRequest(
                Guid.Parse(ContractId), Guid.Parse(PatientId), Guid.Parse(PlanId), 20, new DateOnly(2030, 1, 1)));

            status.ShouldBe(HttpStatusCode.BadRequest);
            error.ShouldNotBeNull();
            error.Errors.ShouldContainKey("Duration");
        });
    }

    [Test]
    public async Task GetActiveTomorrow_ShouldReturnConsolidatedProductionOrderLines()
    {
        _pact.UponReceiving("a request for tomorrow's consolidated production order")
            .Given("tomorrow has one day ready for production")
            .WithRequest(HttpMethod.Get, "/api/v1/calendars/active-tomorrow")
            .WillRespond()
            .WithStatus(HttpStatusCode.OK)
            .WithHeader("Content-Type", "application/json; charset=utf-8")
            .WithJsonBody(Match.MinType(new
            {
                packId = Match.Integer(1),
                patientId = Match.Regex(PatientId, UuidRegex),
                planId = Match.Regex(PlanId, UuidRegex),
                address = Match.Type("742 Evergreen Terrace, Springfield"),
                latitude = Match.Decimal(10.5),
                longitude = Match.Decimal(-20.25)
            }, 1));

        await _pact.VerifyAsync(async ctx =>
        {
            var client = new Ms3DeliveryCalendarClient(new HttpClient { BaseAddress = ctx.MockServerUri });

            var lines = await client.GetActiveTomorrowAsync();

            lines.ShouldNotBeEmpty();
            lines[0].PackId.ShouldBeGreaterThan(0);
            lines[0].Address.ShouldNotBeNullOrWhiteSpace();
        });
    }

    [Test]
    public async Task GetCalendar_ShouldReturnCalendarWithDays()
    {
        _pact.UponReceiving("a request for an existing calendar by id")
            .Given("a calendar with id 1 exists")
            .WithRequest(HttpMethod.Get, "/api/v1/calendars/1")
            .WillRespond()
            .WithStatus(HttpStatusCode.OK)
            .WithHeader("Content-Type", "application/json; charset=utf-8")
            .WithJsonBody(new
            {
                id = Match.Integer(1),
                status = Match.Regex("Active", "^(Active|Completed)$"),
                days = Match.MinType(new
                {
                    id = Match.Integer(1),
                    status = Match.Regex("Pending", "^(Pending|Scheduled|ReadyForProduction|InProduction|Delivered|Failed|Cancelled)$")
                }, 1)
            });

        await _pact.VerifyAsync(async ctx =>
        {
            var client = new Ms3DeliveryCalendarClient(new HttpClient { BaseAddress = ctx.MockServerUri });

            var calendar = await client.GetCalendarAsync(1);

            calendar.Id.ShouldBeGreaterThan(0);
            calendar.Status.ShouldNotBeNullOrWhiteSpace();
            calendar.Days.ShouldNotBeEmpty();
        });
    }

    [Test]
    public async Task GetCalendar_ShouldReturnNotFound_WhenCalendarDoesNotExist()
    {
        _pact.UponReceiving("a request for a calendar id that does not exist")
            .Given("no calendar with id 999 exists")
            .WithRequest(HttpMethod.Get, "/api/v1/calendars/999")
            .WillRespond()
            .WithStatus(HttpStatusCode.NotFound)
            .WithHeader("Content-Type", "application/json; charset=utf-8")
            .WithJsonBody(new { status = Match.Integer(404) });

        await _pact.VerifyAsync(async ctx =>
        {
            var client = new Ms3DeliveryCalendarClient(new HttpClient { BaseAddress = ctx.MockServerUri });

            (await client.GetCalendarStatusAsync(999)).ShouldBe(HttpStatusCode.NotFound);
        });
    }
}
