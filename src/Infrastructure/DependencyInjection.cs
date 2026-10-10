using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Infrastructure.Catalog;
using delivery_schedule_management.Infrastructure.Common;
using delivery_schedule_management.Infrastructure.Data;
using delivery_schedule_management.Infrastructure.Data.Interceptors;
using delivery_schedule_management.Infrastructure.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(Services.Database);
        Guard.Against.Null(connectionString, message: $"Connection string '{Services.Database}' not found.");

        builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();

        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            options.UseNpgsql(connectionString);
            options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });

        builder.EnrichNpgsqlDbContext<ApplicationDbContext>();

        builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        builder.Services.AddScoped<ApplicationDbContextInitialiser>();

        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddScoped<ICalendarRepository, CalendarRepository>();
        builder.Services.AddScoped<IProductionOrderRepository, ProductionOrderRepository>();
        builder.Services.AddScoped<IClock, SystemClock>();

        // Catalog (MS2): mocked by default; set Catalog:UseMock=false and Catalog:BaseUrl to call the real service.
        if (builder.Configuration.GetValue("Catalog:UseMock", true))
        {
            builder.Services.AddSingleton<ICatalogPort, MockCatalogPort>();
        }
        else
        {
            var baseUrl = builder.Configuration["Catalog:BaseUrl"];
            Guard.Against.NullOrWhiteSpace(baseUrl, message: "Catalog:BaseUrl is required when Catalog:UseMock is false.");

            builder.Services.AddHttpClient<ICatalogPort, HttpCatalogPort>(client => client.BaseAddress = new Uri(baseUrl));
        }
    }
}
