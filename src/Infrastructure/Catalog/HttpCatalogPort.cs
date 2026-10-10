using System.Net;
using System.Net.Http.Json;
using delivery_schedule_management.Application.Common.Interfaces;

namespace delivery_schedule_management.Infrastructure.Catalog;

/// <summary>
/// Real outbound adapter to MS2: <c>GET /catalog/plans/{planId}/day/{date}</c> (CLAUDE.md §0).
/// Selected with <c>Catalog:UseMock=false</c> and <c>Catalog:BaseUrl</c>. The expected request/response
/// shape is pinned by the Pact consumer test <c>MS4 -> MS2 catalog</c> (see docs/testing/CONTRACT-TESTING.md).
/// </summary>
public class HttpCatalogPort(HttpClient httpClient) : ICatalogPort
{
    public async Task<CatalogDayResolution> GetDayResolutionAsync(Guid planId, DateOnly date, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"catalog/plans/{planId}/day/{date:yyyy-MM-dd}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new NotFoundException($"{planId}/{date:yyyy-MM-dd}", "CatalogPlanDay");
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<CatalogDayResponse>(cancellationToken)
            ?? throw new InvalidOperationException("MS2 returned an empty catalog response.");

        return new CatalogDayResolution(body.RecipeRef, body.Portions);
    }

    private sealed record CatalogDayResponse(string RecipeRef, int Portions);
}
