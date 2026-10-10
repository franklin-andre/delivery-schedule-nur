using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace delivery_schedule_management.Contract.Consumer.Tests;

/// <summary>
/// Minimal stand-in for how MS3 (scheduling engine) calls MS4. MS3's code is not in this repository, so the
/// client here models ONLY what MS3 reads from MS4's responses: that is what the contract pins down.
/// </summary>
public sealed class Ms3DeliveryCalendarClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record ScheduleRequest(Guid ContractId, Guid PatientId, Guid PlanId, int Duration, DateOnly StartDate);

    public sealed record ScheduledDay(int Id, string Status);

    public sealed record ScheduleResponse(int CalendarId, List<ScheduledDay> Days);

    public sealed record CalendarResponse(int Id, string Status, List<ScheduledDay> Days);

    public sealed record ProductionLine(int PackId, Guid PatientId, Guid PlanId, string Address, double Latitude, double Longitude);

    public sealed record ValidationError(int Status, Dictionary<string, string[]> Errors);

    public async Task<(HttpStatusCode Status, ScheduleResponse? Body)> CreateScheduleAsync(ScheduleRequest request)
    {
        using var response = await http.PostAsJsonAsync("/api/v1/schedule", request, Json);
        var body = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ScheduleResponse>(Json) : null;
        return (response.StatusCode, body);
    }

    public async Task<(HttpStatusCode Status, ValidationError? Error)> CreateInvalidScheduleAsync(ScheduleRequest request)
    {
        using var response = await http.PostAsJsonAsync("/api/v1/schedule", request, Json);
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<ValidationError>(Json));
    }

    public async Task<List<ProductionLine>> GetActiveTomorrowAsync()
    {
        using var response = await http.GetAsync("/api/v1/calendars/active-tomorrow");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<ProductionLine>>(Json))!;
    }

    public async Task<HttpStatusCode> GetCalendarStatusAsync(int id)
    {
        using var response = await http.GetAsync($"/api/v1/calendars/{id}");
        return response.StatusCode;
    }

    public async Task<CalendarResponse> GetCalendarAsync(int id)
    {
        using var response = await http.GetAsync($"/api/v1/calendars/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CalendarResponse>(Json))!;
    }
}
