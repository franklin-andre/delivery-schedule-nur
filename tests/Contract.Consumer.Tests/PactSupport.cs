using System.Text.Json;
using PactNet;

namespace delivery_schedule_management.Contract.Consumer.Tests;

internal static class PactSupport
{
    public const string Ms4 = "MS4DeliveryCalendar";
    public const string Ms3 = "MS3";
    public const string Ms2Catalog = "MS2Catalog";

    /// <summary>Pact files are written to &lt;repo&gt;/pacts so the provider verification (and reviewers) can read them.</summary>
    public static string PactDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !dir.GetFiles("*.slnx").Any())
            {
                dir = dir.Parent;
            }

            return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Repository root (*.slnx) not found."), "pacts");
        }
    }

    public static IPactV4 Create(string consumer, string provider) =>
        Pact.V4(consumer, provider, new PactConfig
        {
            PactDir = PactDir,
            DefaultJsonSettings = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        });
}
