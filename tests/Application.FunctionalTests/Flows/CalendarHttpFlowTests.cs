using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using delivery_schedule_management.Api.Endpoints;
using delivery_schedule_management.Application.Calendars.Commands.ApplyDeliveryResult;
using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Common;
using delivery_schedule_management.Application.Calendars.Queries.GetActiveTomorrow;
using delivery_schedule_management.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace delivery_schedule_management.Application.FunctionalTests.Flows;

/// <summary>
/// FLOW A — "Production and delivery" (MS3/MS5-facing, endpoints #1, #2, #4, #7 + ops trigger). Run: dotnet test --filter "TestCategory=FlowA".
/// End-to-end integration flows driven ONLY through the public HTTP API against a real Postgres
/// (Aspire + Respawn). One correct (happy) flow and several incorrect (unhappy) flows.
/// </summary>
[Category("FlowA")]
public class CalendarHttpFlowTests : TestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<CreateScheduleResult> CreateCalendarAsync(HttpClient client, DateOnly startDate, Guid? contractId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/schedule",
            new CreateScheduleRequest(contractId ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, startDate));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions);
        body.ShouldNotBeNull();
        return body;
    }

    // ───────────────────────── HAPPY FLOW ─────────────────────────

    [Test]
    public async Task HappyFlow_Schedule_Address_Resolve_ActiveTomorrow_DeliveryResult()
    {
        using var client = FunctionalTestSetup.CreateHttpClient();
        var tomorrow = Today.AddDays(1);

        // 1. MS3 creates the calendar (day 0 lands on tomorrow)
        var created = await CreateCalendarAsync(client, tomorrow);
        var dayId = created.Days[0].Id;
        created.Days.ShouldAllBe(d => d.Status == DayStatus.Pending);

        // 2. Patient sets the address -> day becomes Scheduled
        var addressResponse = await client.PostAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/addresses",
            new SetAddressRequest(dayId, "742 Evergreen Terrace", "Springfield", 10.5, -20.25, "Blue door", null));
        addressResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 3. Ops trigger resolves catalog (mocked MS2) -> ReadyForProduction
        var resolveResponse = await client.PostAsync($"/api/v1/calendars/resolve-daily-catalog?date={tomorrow:yyyy-MM-dd}", null);
        resolveResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // 4. MS3 reads tomorrow's consolidated production order
        var lines = await client.GetFromJsonAsync<List<ActiveTomorrowLineDto>>(
            $"/api/v1/calendars/active-tomorrow?date={tomorrow:yyyy-MM-dd}", JsonOptions);
        lines.ShouldNotBeNull();
        var line = lines.ShouldHaveSingleItem();
        line.PackId.ShouldBe(dayId);
        line.Address.ShouldBe("742 Evergreen Terrace, Springfield");
        line.Latitude.ShouldBe(10.5);
        line.Longitude.ShouldBe(-20.25);

        // 5. MS5 reports the delivery result
        var deliveredResponse = await client.PatchAsJsonAsync($"/api/v1/calendars/days/{dayId}/delivery-result",
            new ApplyDeliveryResultRequest(DeliveryResult.Delivered, "https://example.com/proof.jpg", null));
        deliveredResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 6. The calendar read model reflects the final state
        var calendar = await client.GetFromJsonAsync<CalendarVm>($"/api/v1/calendars/{created.CalendarId}", JsonOptions);
        calendar.ShouldNotBeNull();
        calendar.Days.Single(d => d.Id == dayId).Status.ShouldBe(DayStatus.Delivered);
    }

    // ───────────────────────── UNHAPPY FLOWS ─────────────────────────

    [Test]
    public async Task UnhappyFlow_DuplicateContract_ShouldReturnSameCalendar_AndNotCreateSecond()
    {
        using var client = FunctionalTestSetup.CreateHttpClient();
        var contractId = Guid.NewGuid();

        var first = await CreateCalendarAsync(client, Today.AddDays(10), contractId);
        var second = await CreateCalendarAsync(client, Today.AddDays(30), contractId);

        second.CalendarId.ShouldBe(first.CalendarId);
        (await TestApp.CountAsync<Domain.Entities.DeliveryCalendar>()).ShouldBe(1);
    }

    [Test]
    public async Task UnhappyFlow_InvalidPayload_ShouldReturn400_AndPersistNothing()
    {
        using var client = FunctionalTestSetup.CreateHttpClient();

        var response = await client.PostAsJsonAsync("/api/v1/schedule",
            new CreateScheduleRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20, Today.AddDays(10)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The body must say WHICH field failed, not just that something did.
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Errors.ShouldContainKey("Duration");

        (await TestApp.CountAsync<Domain.Entities.DeliveryCalendar>()).ShouldBe(0);
    }

    [Test]
    public async Task UnhappyFlow_ActiveTomorrow_ShouldNotListDaysThatAreNotReadyForProduction()
    {
        using var client = FunctionalTestSetup.CreateHttpClient();
        var tomorrow = Today.AddDays(1);

        // Calendar exists for tomorrow but the day is still Pending: no address, no resolution.
        await CreateCalendarAsync(client, tomorrow);

        var lines = await client.GetFromJsonAsync<List<ActiveTomorrowLineDto>>(
            $"/api/v1/calendars/active-tomorrow?date={tomorrow:yyyy-MM-dd}", JsonOptions);

        lines.ShouldNotBeNull();
        lines.ShouldBeEmpty();
    }

    [Test]
    public async Task UnhappyFlow_DeliveryResult_ShouldBeIdempotent_ButRejectConflictingResult()
    {
        using var client = FunctionalTestSetup.CreateHttpClient();
        var tomorrow = Today.AddDays(1);

        var created = await CreateCalendarAsync(client, tomorrow);
        var dayId = created.Days[0].Id;
        await client.PostAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/addresses",
            new SetAddressRequest(dayId, "1 Main St", "Metropolis", 5, 5, null, null));
        await client.PostAsync($"/api/v1/calendars/resolve-daily-catalog?date={tomorrow:yyyy-MM-dd}", null);

        var url = $"/api/v1/calendars/days/{dayId}/delivery-result";

        (await client.PatchAsJsonAsync(url, new ApplyDeliveryResultRequest(DeliveryResult.Delivered, null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Same result again: no-op, still 200 Delivered
        var repeat = await client.PatchAsJsonAsync(url, new ApplyDeliveryResultRequest(DeliveryResult.Delivered, null, null));
        repeat.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await repeat.Content.ReadFromJsonAsync<ApplyDeliveryResultResult>(JsonOptions))!.Status.ShouldBe(DayStatus.Delivered);

        // Contradicting result after the cycle closed: invalid transition
        var conflicting = await client.PatchAsJsonAsync(url, new ApplyDeliveryResultRequest(DeliveryResult.Failed, null, null));
        conflicting.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await conflicting.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(422);
    }

    [Test]
    public async Task UnhappyFlow_DeliveryResult_UnknownDay_ShouldReturn404()
    {
        using var client = FunctionalTestSetup.CreateHttpClient();

        var response = await client.PatchAsJsonAsync("/api/v1/calendars/days/999999/delivery-result",
            new ApplyDeliveryResultRequest(DeliveryResult.Delivered, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
