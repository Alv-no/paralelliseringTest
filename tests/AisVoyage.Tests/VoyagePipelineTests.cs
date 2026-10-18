using System.Globalization;
using System.Text;

namespace AisVoyage.Tests;

public class VoyagePipelineTests
{
    private const int ParallelRunCount = 5;

    private static readonly int ParallelWorkerCount = Math.Max(Environment.ProcessorCount, 4);

    [Fact]
    public async Task SingleWorker_ProducesBaseline()
    {
        var report = await RunAsync(workerCount: 1);

        Assert.Equal(AisDataGenerator.VesselCount, report.Vessels.Count);
        Assert.All(report.Vessels.Values, vessel =>
        {
            Assert.True(vessel.DistanceNm > 0);
            Assert.True(vessel.FuelTonnes > 0);
        });
    }

    [Fact]
    public async Task ParallelRun_MatchesSequentialResult()
    {
        var expected = await RunAsync(workerCount: 1);

        var actual = await RunAsync(ParallelWorkerCount);

        AssertSameReport(expected, actual, "sekvensiell kjøring",$"parallell kjøring med {ParallelWorkerCount} workers");
    }

    [Fact]
    public async Task ParallelRuns_AreDeterministic()
    {
        var reports = new List<VoyageReport>();
        for (var run = 0; run < ParallelRunCount; run++)
            reports.Add(await RunAsync(ParallelWorkerCount));

        for (var run = 1; run < ParallelRunCount; run++)
            AssertSameReport(reports[0], reports[run], "kjøring 1", $"kjøring {run + 1}");
    }

    private static Task<VoyageReport> RunAsync(int workerCount) =>
        VoyagePipeline.RunAsync(AisDataGenerator.CreateStream(), workerCount);

    private static void AssertSameReport(VoyageReport expected, VoyageReport actual, string expectedName, string actualName)
    {
        var deviations = new StringBuilder();

        foreach (var (vesselId, expectedVessel) in expected.Vessels)
        {
            if (!actual.Vessels.TryGetValue(vesselId, out var actualVessel))
            {
                deviations.AppendLine($"{vesselId}: mangler i {actualName}");
                continue;
            }

            if (expectedVessel != actualVessel)
                deviations.AppendLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{vesselId}: distanse {expectedVessel.DistanceNm:F6} vs {actualVessel.DistanceNm:F6} nm ({actualVessel.DistanceNm - expectedVessel.DistanceNm:+0.000000;-0.000000}), " +
                    $"drivstoff {expectedVessel.FuelTonnes:F6} vs {actualVessel.FuelTonnes:F6} t ({actualVessel.FuelTonnes - expectedVessel.FuelTonnes:+0.000000;-0.000000})"));
        }

        foreach (var vesselId in actual.Vessels.Keys.Except(expected.Vessels.Keys))
            deviations.AppendLine($"{vesselId}: finnes bare i {actualName}");

        if (deviations.Length > 0)
            Assert.Fail($"{actualName} avviker fra {expectedName}:{Environment.NewLine}{deviations}");
    }
}
