using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using delivery_schedule_management.Api.Endpoints;
using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Common;
using delivery_schedule_management.Application.Calendars.Queries.GetActiveTomorrow;
using delivery_schedule_management.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace delivery_schedule_management.Application.FunctionalTests.Flows;

/// <summary>
/// FLOW B — "Patient manages the calendar" (frontend-facing endpoints #3, #4, #5, #6) end to end,
/// with the 48h lock rule enforced across the whole lifecycle. Together with
/// <see cref="CalendarHttpFlowTests"/> (FLOW A — "Production and delivery", MS3/MS5-facing) these are the
/// two complete flows of the integration layer. Run one flow with: dotnet test --filter "TestCategory=FlowB".
/// </summary>
[Category("FlowB")]
public class CalendarLifecycleFlowTests : TestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Test]
    public async Task FlowB_Happy_PatientSetsAddresses_Reschedules_AndCancels_ThenCalendarReflectsEverything()
    {
        using var client = FunctionalTestSetup.CreateHttpClient();
        var start = Today.AddDays(10); // well outside the 48h lock

        // 1. MS3 creates the calendar
        var create = await client.PostAsJsonAsync("/api/v1/schedule",
            new CreateScheduleRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, start));
        var created = (await create.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions))!;
        var addressDay = created.Days[0].Id;
        var rescheduleDay = created.Days[1].Id;
        var cancelDay = created.Days[2].Id;

        // 2. Patient sets an address on day 0
        (await client.PostAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/addresses",
            new SetAddressRequest(addressDay, "1 Main St", "Quito", -0.18, -78.47, "Blue door", null)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // 3. Patient moves day 1 to a free date after the calendar
        var newDate = start.AddDays(20);
        (await client.PutAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/dates",
            new RescheduleDateRequest(rescheduleDay, newDate))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // 4. Patient cancels day 2
        (await client.PostAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/cancel-day", new CancelDayRequest(cancelDay)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // 5. Read model reflects all three changes
        var calendar = (await client.GetFromJsonAsync<CalendarVm>($"/api/v1/calendars/{created.CalendarId}", JsonOptions))!;
        var addressed = calendar.Days.Single(d => d.Id == addressDay);
        addressed.Status.ShouldBe(DayStatus.Scheduled);
        addressed.Address!.Street.ShouldBe("1 Main St");
        calendar.Days.Single(d => d.Id == rescheduleDay).Date.ShouldBe(newDate);
        calendar.Days.Single(d => d.Id == cancelDay).Status.ShouldBe(DayStatus.Cancelled);

        // 6. A cancelled day never reaches production: resolve for its date and check it is absent
        await client.PostAsync($"/api/v1/calendars/resolve-daily-catalog?date={start.AddDays(2):yyyy-MM-dd}", null);
        var lines = (await client.GetFromJsonAsync<List<ActiveTomorrowLineDto>>(
            $"/api/v1/calendars/active-tomorrow?date={start.AddDays(2):yyyy-MM-dd}", JsonOptions))!;
        lines.ShouldBeEmpty();
    }

    [Test]
    public async Task FlowB_Unhappy_InsideLockWindow_RescheduleAndCancelAreRejected_AndNothingChanges()
    {
        using var client = FunctionalTestSetup.CreateHttpClient();

        // Calendar starting today: day 0 is inside the 48h window
        var create = await client.PostAsJsonAsync("/api/v1/schedule",
            new CreateScheduleRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, Today));
        var created = (await create.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions))!;
        var day = created.Days[0];

        var reschedule = await client.PutAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/dates",
            new RescheduleDateRequest(day.Id, Today.AddDays(40)));
        var cancel = await client.PostAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/cancel-day", new CancelDayRequest(day.Id));

        reschedule.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        cancel.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await cancel.Content.ReadFromJsonAsync<ProblemDetails>())!.Status.ShouldBe(409);

        var calendar = (await client.GetFromJsonAsync<CalendarVm>($"/api/v1/calendars/{created.CalendarId}", JsonOptions))!;
        var unchanged = calendar.Days.Single(d => d.Id == day.Id);
        unchanged.Status.ShouldBe(DayStatus.Pending);
        unchanged.Date.ShouldBe(Today);
    }

    [Test]
    public async Task FlowB_Unhappy_CancelledDayCannotBeAddressedOrRescheduled_And_DuplicateDateIsRejected()
    {
        using var client = FunctionalTestSetup.CreateHttpClient();
        var start = Today.AddDays(10);

        var create = await client.PostAsJsonAsync("/api/v1/schedule",
            new CreateScheduleRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, start));
        var created = (await create.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions))!;
        var dayId = created.Days[0].Id;

        (await client.PostAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/cancel-day", new CancelDayRequest(dayId)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // cancelled day: address and reschedule are invalid transitions (422)
        (await client.PostAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/addresses",
            new SetAddressRequest(dayId, "S", "C", 1, 1, null, null))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await client.PutAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/dates",
            new RescheduleDateRequest(dayId, start.AddDays(30)))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // moving another day onto an already used date of the same calendar: 400
        var clash = await client.PutAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/dates",
            new RescheduleDateRequest(created.Days[1].Id, start.AddDays(5)));
        clash.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
