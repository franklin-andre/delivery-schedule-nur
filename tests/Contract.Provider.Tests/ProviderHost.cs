using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Respawn;

namespace delivery_schedule_management.Contract.Provider.Tests;

/// <summary>
/// Boots the REAL MS4 API on a real TCP port (Pact verification needs a URL, not an in-memory TestServer)
/// against a Postgres container from Aspire, plus a small "provider states" endpoint that arranges data
/// the pact's <c>Given(...)</c> clauses describe. State setup goes through MS4's own public HTTP API, so the
/// verified behaviour is exactly what a real consumer would see.
/// </summary>
[SetUpFixture]
public class ProviderHost
{
    private static DistributedApplication? _aspire;
    private static ApiFactory? _factory;
    private static WebApplication? _stateServer;
    private static string _connectionString = null!;
    private static Respawner _respawner = null!;

    public static Uri ProviderUri { get; private set; } = null!;

    public static Uri StatesUri { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.TestAppHost>(
            args: [], configureBuilder: (options, _) => options.DisableDashboard = true);
        builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";

        _aspire = await builder.BuildAsync(cts.Token).WaitAsync(cts.Token);
        await _aspire.StartAsync(cts.Token).WaitAsync(cts.Token);
        await _aspire.ResourceNotifications.WaitForResourceHealthyAsync(Shared.Services.Database, cts.Token);

        _connectionString = (await _aspire.GetConnectionStringAsync(Shared.Services.Database))!;

        _factory = new ApiFactory(_connectionString);
        _factory.UseKestrel(0);
        _factory.StartServer();
        ProviderUri = new Uri(_factory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First());

        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync(cts.Token);
            _respawner = await Respawner.CreateAsync(connection);
        }

        await StartStateServerAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_stateServer is not null) await _stateServer.DisposeAsync();
        if (_factory is not null) await _factory.DisposeAsync();
        if (_aspire is not null) await _aspire.DisposeAsync();
    }

    private static async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);

        // Respawn only deletes rows; identity sequences keep counting. Restart them so DB-generated ids
        // are deterministic (the first calendar created after a reset gets id 1, matching the pact path).
        await using var restart = new NpgsqlCommand(
            "DO $$ DECLARE s record; BEGIN FOR s IN SELECT n.nspname AS sch, c.relname AS seq FROM pg_class c " +
            "JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind = 'S' AND n.nspname = 'public' LOOP " +
            "EXECUTE format('ALTER SEQUENCE %I.%I RESTART', s.sch, s.seq); END LOOP; END $$;", connection);
        await restart.ExecuteNonQueryAsync();
    }

    private static async Task StartStateServerAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _stateServer = builder.Build();

        _stateServer.MapPost("/provider-states", async (ProviderStateRequest request) =>
        {
            if (request.Action == "setup")
            {
                try
                {
                    await ArrangeAsync(request.State);
                }
                catch (Exception ex)
                {
                    // The verifier only reports "state change handler failed"; print the real reason.
                    TestContext.Progress.WriteLine($"Provider state '{request.State}' failed: {ex}");
                    throw;
                }
            }

            return Results.Ok();
        });

        await _stateServer.StartAsync();
        StatesUri = new Uri(_stateServer.Urls.First());
    }

    // ── Provider states: names must match the consumers' Given(...) strings ──

    private static async Task ArrangeAsync(string? state)
    {
        await ResetDatabaseAsync();

        // Interactions without Given(...) arrive with an empty state; an empty database is also the state
        // for "no calendar exists for contract ...".
        if (string.IsNullOrEmpty(state) || state.StartsWith("no calendar exists for contract", StringComparison.Ordinal)
            || state.StartsWith("no calendar with id", StringComparison.Ordinal))
        {
            return;
        }

        if (state == "tomorrow has one day ready for production")
        {
            await SeedReadyDayForTomorrowAsync();
            return;
        }

        if (state == "a calendar with id 1 exists")
        {
            await SeedCalendarAsync();
            return;
        }

        throw new InvalidOperationException($"Unknown provider state: '{state}'. Add it to ProviderHost.ArrangeAsync.");
    }

    private static async Task SeedCalendarAsync()
    {
        using var client = new HttpClient { BaseAddress = ProviderUri };
        var created = await client.PostAsJsonAsync("/api/v1/schedule", new
        {
            contractId = Guid.NewGuid(),
            patientId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            planId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            duration = 15,
            startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10)
        });
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<ScheduleBody>();
        if (body!.CalendarId != 1)
        {
            throw new InvalidOperationException($"Expected the seeded calendar to get id 1 but got {body.CalendarId}; sequence reset failed.");
        }
    }

    private static async Task SeedReadyDayForTomorrowAsync()
    {
        using var client = new HttpClient { BaseAddress = ProviderUri };
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        var created = await client.PostAsJsonAsync("/api/v1/schedule", new
        {
            contractId = Guid.NewGuid(),
            patientId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            planId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            duration = 15,
            startDate = tomorrow
        });
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<ScheduleBody>();

        (await client.PostAsJsonAsync($"/api/v1/calendars/{body!.CalendarId}/addresses", new
        {
            dayId = body.Days[0].Id,
            street = "742 Evergreen Terrace",
            city = "Springfield",
            latitude = 10.5,
            longitude = -20.25
        })).EnsureSuccessStatusCode();

        (await client.PostAsync($"/api/v1/calendars/resolve-daily-catalog?date={tomorrow:yyyy-MM-dd}", null))
            .EnsureSuccessStatusCode();
    }

    private sealed record ProviderStateRequest(string? State, string? Action);

    private sealed record ScheduleBody(int CalendarId, List<DayBody> Days);

    private sealed record DayBody(int Id);

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ConnectionStrings:delivery-schedule-management-db", connectionString);
    }
}
