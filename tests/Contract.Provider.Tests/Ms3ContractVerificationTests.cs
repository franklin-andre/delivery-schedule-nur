using PactNet.Infrastructure.Outputters;
using PactNet.Verifier;

namespace delivery_schedule_management.Contract.Provider.Tests;

/// <summary>
/// Provider side: MS4 verifies the pact published by consumer MS3
/// (pacts/MS3-MS4DeliveryCalendar.json, produced by tests/Contract.Consumer.Tests).
/// Every interaction is replayed against the real API; a mismatch fails the build.
/// </summary>
public class Ms3ContractVerificationTests
{
    private sealed class NUnitOutput : IOutput
    {
        public void WriteLine(string line) => TestContext.Progress.WriteLine(line);
    }

    private static FileInfo PactFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.slnx").Any())
        {
            dir = dir.Parent;
        }

        var file = new FileInfo(Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Repository root not found."), "pacts", name));
        file.Exists.ShouldBeTrue($"Pact file {file.FullName} not found. Run the consumer tests first (scripts/run-pact.ps1).");
        return file;
    }

    [Test]
    public void MS4_ShouldHonourContract_WithMS3()
    {
        var config = new PactVerifierConfig { Outputters = [new NUnitOutput()], LogLevel = PactNet.PactLogLevel.Information };

        using var verifier = new PactVerifier("MS4DeliveryCalendar", config);

        verifier
            .WithHttpEndpoint(ProviderHost.ProviderUri)
            .WithFileSource(PactFile("MS3-MS4DeliveryCalendar.json"))
            .WithProviderStateUrl(new Uri(ProviderHost.StatesUri, "/provider-states"))
            .Verify();
    }
}
