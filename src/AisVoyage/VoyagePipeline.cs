using System.Threading.Channels;

namespace AisVoyage;

public static class VoyagePipeline
{
    private const int ChannelCapacity = 256;

    public static async Task<VoyageReport> RunAsync(Stream input, int workerCount)
    {
        var channel = Channel.CreateBounded<AisMessage>(ChannelCapacity);
        var tracker = new VesselTracker();

        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(() => ProcessAsync(channel.Reader, tracker)))
            .ToArray();

        await foreach (var message in AisStreamReader.ReadAsync(input))
            await channel.Writer.WriteAsync(message);

        channel.Writer.Complete();
        await Task.WhenAll(workers);

        return VoyageReport.From(tracker.States);
    }

    private static async Task ProcessAsync(ChannelReader<AisMessage> reader, VesselTracker tracker)
    {
        await foreach (var message in reader.ReadAllAsync())
            tracker.Apply(message);
    }
}
