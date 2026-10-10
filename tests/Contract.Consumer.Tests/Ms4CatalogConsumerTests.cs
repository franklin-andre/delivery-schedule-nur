using PactNet;
using System.Net;
using delivery_schedule_management.Infrastructure.Catalog;
using PactNet.Matchers;

namespace delivery_schedule_management.Contract.Consumer.Tests;

/// <summary>
/// Consumer side of the MS4 -> MS2 contract: <c>GET /catalog/plans/{id}/day/{date}</c>.
/// The consumer code under test is the REAL <see cref="HttpCatalogPort"/>. Writes
/// pacts/MS4DeliveryCalendar-MS2Catalog.json, which the MS2 team must verify before real integration.
/// </summary>
public class Ms4CatalogConsumerTests
{
    private static readonly Guid PlanId = new("33333333-3333-3333-3333-333333333333");
    private static readonly DateOnly Date = new(2030, 1, 15);

    private PactNet.IPactBuilderV4 _pact = null!;

    [SetUp]
    public void SetUp() => _pact = PactSupport.Create(PactSupport.Ms4, PactSupport.Ms2Catalog).WithHttpInteractions();

    [Test]
    public async Task GetDayResolution_ShouldReturnRecipeAndPortions_WhenPlanHasThatDay()
    {
        _pact.UponReceiving("a request for the recipe and portions of a plan day")
            .Given("plan 33333333-3333-3333-3333-333333333333 has a recipe for 2030-01-15")
            .WithRequest(HttpMethod.Get, "/catalog/plans/33333333-3333-3333-3333-333333333333/day/2030-01-15")
            .WillRespond()
            .WithStatus(HttpStatusCode.OK)
            .WithHeader("Content-Type", "application/json; charset=utf-8")
            .WithJsonBody(new
            {
                recipeRef = Match.Type("recipe-lentil-stew"),
                portions = Match.Integer(2)
            });

        await _pact.VerifyAsync(async ctx =>
        {
            var port = new HttpCatalogPort(new HttpClient { BaseAddress = ctx.MockServerUri });

            var result = await port.GetDayResolutionAsync(PlanId, Date, CancellationToken.None);

            result.RecipeRef.ShouldNotBeNullOrWhiteSpace();
            result.Portions.ShouldBeGreaterThan(0);
        });
    }

    [Test]
    public async Task GetDayResolution_ShouldThrowNotFound_WhenPlanDoesNotExist()
    {
        _pact.UponReceiving("a request for a plan day of an unknown plan")
            .Given("plan 33333333-3333-3333-3333-333333333333 does not exist")
            .WithRequest(HttpMethod.Get, "/catalog/plans/33333333-3333-3333-3333-333333333333/day/2030-01-15")
            .WillRespond()
            .WithStatus(HttpStatusCode.NotFound);

        await _pact.VerifyAsync(async ctx =>
        {
            var port = new HttpCatalogPort(new HttpClient { BaseAddress = ctx.MockServerUri });

            await Should.ThrowAsync<Ardalis.GuardClauses.NotFoundException>(() =>
                port.GetDayResolutionAsync(PlanId, Date, CancellationToken.None));
        });
    }
}
