using System.Net;
using delivery_schedule_management.Infrastructure.Catalog;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Infrastructure;

public class HttpCatalogPortTests
{
    private static readonly Guid PlanId = new("33333333-3333-3333-3333-333333333333");
    private static readonly DateOnly Date = new(2030, 1, 15);

    private static (HttpCatalogPort Port, Mock<HttpMessageHandler> Handler) Create(HttpResponseMessage response)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return (new HttpCatalogPort(new HttpClient(handler.Object) { BaseAddress = new Uri("http://ms2.local/") }), handler);
    }

    [Test]
    public async Task GetDayResolution_ShouldCallExpectedPath_AndMapBody()
    {
        var (port, handler) = Create(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"recipeRef\":\"recipe-1\",\"portions\":3}", System.Text.Encoding.UTF8, "application/json")
        });

        var result = await port.GetDayResolutionAsync(PlanId, Date, CancellationToken.None);

        result.RecipeRef.ShouldBe("recipe-1");
        result.Portions.ShouldBe(3);
        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Get &&
                r.RequestUri!.AbsolutePath == "/catalog/plans/33333333-3333-3333-3333-333333333333/day/2030-01-15"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task GetDayResolution_ShouldThrowNotFound_On404()
    {
        var (port, _) = Create(new HttpResponseMessage(HttpStatusCode.NotFound));

        await Should.ThrowAsync<Ardalis.GuardClauses.NotFoundException>(() =>
            port.GetDayResolutionAsync(PlanId, Date, CancellationToken.None));
    }

    [Test]
    public async Task GetDayResolution_ShouldThrowHttpRequestException_OnServerError()
    {
        var (port, _) = Create(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Should.ThrowAsync<HttpRequestException>(() =>
            port.GetDayResolutionAsync(PlanId, Date, CancellationToken.None));
    }

    [Test]
    public async Task GetDayResolution_ShouldThrow_WhenBodyIsNull()
    {
        var (port, _) = Create(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json")
        });

        await Should.ThrowAsync<InvalidOperationException>(() =>
            port.GetDayResolutionAsync(PlanId, Date, CancellationToken.None));
    }
}
