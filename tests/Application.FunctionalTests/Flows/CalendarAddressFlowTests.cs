using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using delivery_schedule_management.Api.Endpoints;
using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Common;
using delivery_schedule_management.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace delivery_schedule_management.Application.FunctionalTests.Flows;

/// <summary>
/// HTTP flows for POST /api/v1/calendars/{id}/addresses against a real Postgres.
/// </summary>
public class CalendarAddressFlowTests : TestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<CreateScheduleResult> CreateCalendarAsync(HttpClient client, DateOnly startDate)
    {
        var response = await client.PostAsJsonAsync("/api/v1/schedule",
            new CreateScheduleRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, startDate));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions);
        body.ShouldNotBeNull();
        return body;
    }

    [Test]
    public async Task HappyFlow_SetAddress_ShouldScheduleDay_AndBeVisibleInCalendar()
    {
        using var client = FunctionalTestSetup.CreateHttpClient();
        var created = await CreateCalendarAsync(client, Today.AddDays(10));
        var dayId = created.Days[0].Id;

        var response = await client.PostAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/addresses",
            new SetAddressRequest(dayId, "742 Evergreen Terrace", "Springfield", 10.5, -20.25, "Blue door", null));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var calendar = await client.GetFromJsonAsync<CalendarVm>($"/api/v1/calendars/{created.CalendarId}", JsonOptions);
        calendar.ShouldNotBeNull();
        var day = calendar.Days.Single(d => d.Id == dayId);
        day.Status.ShouldBe(DayStatus.Scheduled);
        day.Address.ShouldNotBeNull();
        day.Address.Street.ShouldBe("742 Evergreen Terrace");
        day.Address.City.ShouldBe("Springfield");
        day.Address.Latitude.ShouldBe(10.5);
        day.Address.Longitude.ShouldBe(-20.25);
        calendar.Days.Where(d => d.Id != dayId).ShouldAllBe(d => d.Status == DayStatus.Pending && d.Address == null);
    }

    [Test]
    public async Task UnhappyFlow_SetAddress_WithLatitude95_ShouldReturn400_AndDayStaysPending()
    {
        using var client = FunctionalTestSetup.CreateHttpClient();
        var created = await CreateCalendarAsync(client, Today.AddDays(10));
        var dayId = created.Days[0].Id;

        var response = await client.PostAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/addresses",
            new SetAddressRequest(dayId, "742 Evergreen Terrace", "Springfield", 95, -20.25, null, null));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(400);
        problem.Errors.ShouldContainKey("Latitude");

        var calendar = await client.GetFromJsonAsync<CalendarVm>($"/api/v1/calendars/{created.CalendarId}", JsonOptions);
        calendar.ShouldNotBeNull();
        var day = calendar.Days.Single(d => d.Id == dayId);
        day.Status.ShouldBe(DayStatus.Pending);
        day.Address.ShouldBeNull();
    }
}
