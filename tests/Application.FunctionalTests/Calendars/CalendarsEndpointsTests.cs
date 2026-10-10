using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using delivery_schedule_management.Api.Endpoints;
using delivery_schedule_management.Application.Calendars.Commands.ApplyDeliveryResult;
using delivery_schedule_management.Application.Calendars.Commands.CancelDeliveryDay;
using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Commands.RescheduleDeliveryDay;
using delivery_schedule_management.Application.Calendars.Commands.ResolveDailyCatalog;
using delivery_schedule_management.Application.Calendars.Commands.SetDeliveryAddress;
using delivery_schedule_management.Application.Calendars.Common;
using delivery_schedule_management.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace delivery_schedule_management.Application.FunctionalTests.Calendars;

/// <summary>
/// Exercises the actual HTTP surface (routing, DTO binding, JSON, status codes,
/// <see cref="delivery_schedule_management.Api.Infrastructure.ProblemDetailsExceptionHandler"/> mapping)
/// through <see cref="WebApiFactory"/>'s <c>HttpClient</c>, rather than dispatching MediatR requests
/// directly like <see cref="CalendarAcceptanceTests"/> does. Covers what Stage 5 (API layer) never got
/// automated coverage for.
/// </summary>
public class CalendarsEndpointsTests : TestBase
{
    // Mirrors the server's ConfigureHttpJsonOptions (Web/DependencyInjection.cs): enums serialize as
    // strings, so the client's deserializer needs the same converter or it throws on e.g. "Pending".
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static HttpClient CreateClient() => FunctionalTestSetup.CreateHttpClient();

    [Test]
    public async Task PostSchedule_ShouldReturn200_WithCalendarIdAndDays()
    {
        using var client = CreateClient();

        var request = new CreateScheduleRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10));

        var response = await client.PostAsJsonAsync("/api/v1/schedule", request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions);
        body.ShouldNotBeNull();
        body.CalendarId.ShouldBeGreaterThan(0);
        body.Days.Count.ShouldBe(15);
    }

    [Test]
    public async Task GetCalendar_ShouldReturn404_WithProblemDetails_WhenMissing()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/calendars/999999");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Test]
    public async Task CancelDay_ShouldReturn409_WithProblemDetails_WhenLessThan48HoursRemain()
    {
        using var client = CreateClient();

        var createRequest = new CreateScheduleRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, DateOnly.FromDateTime(DateTime.UtcNow));

        var createResponse = await client.PostAsJsonAsync("/api/v1/schedule", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions);
        created.ShouldNotBeNull();

        var dayId = created.Days[0].Id;

        var cancelResponse = await client.PostAsJsonAsync(
            $"/api/v1/calendars/{created.CalendarId}/cancel-day", new CancelDayRequest(dayId));

        cancelResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await cancelResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status409Conflict);
    }

    [Test]
    public async Task SetAddress_ShouldReturn422_WithProblemDetails_WhenDayAlreadyCancelled()
    {
        using var client = CreateClient();

        // Day 29 (index) lands 29 days after start = today + 9, safely outside the 48h lock window.
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20);
        var createRequest = new CreateScheduleRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 30, startDate);

        var createResponse = await client.PostAsJsonAsync("/api/v1/schedule", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions);
        created.ShouldNotBeNull();

        var dayId = created.Days[29].Id;

        var firstCancel = await client.PostAsJsonAsync(
            $"/api/v1/calendars/{created.CalendarId}/cancel-day", new CancelDayRequest(dayId));
        firstCancel.StatusCode.ShouldBe(HttpStatusCode.OK);

        var setAddressResponse = await client.PostAsJsonAsync(
            $"/api/v1/calendars/{created.CalendarId}/addresses",
            new SetAddressRequest(dayId, "1 Main St", "Metropolis", 5, 5, null, null));

        setAddressResponse.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await setAddressResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status422UnprocessableEntity);
    }

    // ── Endpoint 3: PUT /api/v1/calendars/{id}/dates ──────────────────────────

    [Test]
    public async Task RescheduleDate_ShouldReturn200_WithUnchangedStatus()
    {
        using var client = CreateClient();

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        var createResponse = await client.PostAsJsonAsync("/api/v1/schedule",
            new CreateScheduleRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, startDate));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions);
        created.ShouldNotBeNull();

        var dayId = created.Days[0].Id;

        var response = await client.PutAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/dates",
            new RescheduleDateRequest(dayId, startDate.AddDays(20)));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RescheduleDeliveryDayResult>(JsonOptions);
        result.ShouldNotBeNull();
        result.DayId.ShouldBe(dayId);
        result.Status.ShouldBe(DayStatus.Pending);
    }

    [Test]
    public async Task RescheduleDate_ShouldReturn409_WithProblemDetails_WhenLocked()
    {
        using var client = CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/schedule",
            new CreateScheduleRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15,
                DateOnly.FromDateTime(DateTime.UtcNow)));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions);
        created.ShouldNotBeNull();

        var response = await client.PutAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/dates",
            new RescheduleDateRequest(created.Days[0].Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20)));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status409Conflict);
    }

    [Test]
    public async Task RescheduleDate_ShouldReturn422_WithProblemDetails_WhenDayAlreadyCancelled()
    {
        using var client = CreateClient();

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20);
        var createResponse = await client.PostAsJsonAsync("/api/v1/schedule",
            new CreateScheduleRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 30, startDate));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions);
        created.ShouldNotBeNull();

        // Day 29 = today + 9, well outside the lock window — cancel succeeds, then reschedule → 422.
        var dayId = created.Days[29].Id;

        await client.PostAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/cancel-day",
            new CancelDayRequest(dayId));

        var response = await client.PutAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/dates",
            new RescheduleDateRequest(dayId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20)));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status422UnprocessableEntity);
    }

    // ── Endpoint 5: POST /api/v1/calendars/{id}/cancel-day ────────────────────

    [Test]
    public async Task CancelDay_ShouldReturn200_WithCancelledStatus()
    {
        using var client = CreateClient();

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        var createResponse = await client.PostAsJsonAsync("/api/v1/schedule",
            new CreateScheduleRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, startDate));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateScheduleResult>(JsonOptions);
        created.ShouldNotBeNull();

        var dayId = created.Days[0].Id;

        var response = await client.PostAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/cancel-day",
            new CancelDayRequest(dayId));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CancelDeliveryDayResult>(JsonOptions);
        result.ShouldNotBeNull();
        result.DayId.ShouldBe(dayId);
        result.Status.ShouldBe(DayStatus.Cancelled);
    }

    // ── Endpoint 7: PATCH /api/v1/calendars/days/{dayId}/delivery-result ──────

    [Test]
    public async Task ApplyDeliveryResult_ShouldReturn200_WithDeliveredStatus()
    {
        using var client = CreateClient();

        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        var created = await TestApp.SendAsync(new CreateScheduleCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, targetDate));
        var dayId = created.Days[0].Id;

        await TestApp.SendAsync(new SetDeliveryAddressCommand(
            created.CalendarId, dayId, "742 Evergreen Terrace", "Springfield", 10.5, -20.25, null, null));

        await TestApp.SendAsync(new ResolveDailyCatalogCommand(targetDate));

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/calendars/days/{dayId}/delivery-result",
            new ApplyDeliveryResultRequest(DeliveryResult.Delivered, "https://example.com/photo.jpg", null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApplyDeliveryResultResult>(JsonOptions);
        result.ShouldNotBeNull();
        result.DayId.ShouldBe(dayId);
        result.Status.ShouldBe(DayStatus.Delivered);
    }

    [Test]
    public async Task ApplyDeliveryResult_ShouldReturn422_WithProblemDetails_WhenDayNotReadyForDelivery()
    {
        using var client = CreateClient();

        var created = await TestApp.SendAsync(new CreateScheduleCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10)));
        var dayId = created.Days[0].Id; // Pending — no address set, never resolved

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/calendars/days/{dayId}/delivery-result",
            new ApplyDeliveryResultRequest(DeliveryResult.Delivered, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status422UnprocessableEntity);
    }

    // ── Endpoint 4: 404 paths ──────────────────────────────────────────────────

    [Test]
    public async Task SetAddress_ShouldReturn404_WhenCalendarMissing()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/calendars/999999/addresses",
            new SetAddressRequest(1, "1 Main St", "Springfield", 10.5, -20.25, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Test]
    public async Task SetAddress_ShouldReturn404_WhenDayMissing()
    {
        using var client = CreateClient();

        var created = await TestApp.SendAsync(new CreateScheduleCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10)));

        var response = await client.PostAsJsonAsync($"/api/v1/calendars/{created.CalendarId}/addresses",
            new SetAddressRequest(999999, "1 Main St", "Springfield", 10.5, -20.25, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status404NotFound);
    }
}
