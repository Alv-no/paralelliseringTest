using System.Globalization;
using System.Text;

namespace AisVoyage.Tests;

public static class AisDataGenerator
{
    public const int VesselCount = 20;
    public const int MessagesPerVessel = 500;

    private const int Seed = 42;
    private static readonly DateTime VoyageStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Lazy<byte[]> Csv = new(Generate);

    public static Stream CreateStream() => new MemoryStream(Csv.Value, writable: false);

    private static byte[] Generate()
    {
        var random = new Random(Seed);

        var messages = Enumerable.Range(1, VesselCount)
            .SelectMany(number => GenerateVoyage($"VESSEL-{number:D2}", random))
            .OrderBy(message => message.Timestamp)
            .ToList();

        var csv = new StringBuilder();
        foreach (var message in messages)
            csv.AppendLine(ToCsvLine(message));

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static List<AisMessage> GenerateVoyage(string vesselId, Random random)
    {
        var reportIntervalSeconds = random.Next(2, 60);
        var timestamp = VoyageStart.AddSeconds(random.Next(0, 600));
        var latitude = 58 + random.NextDouble() * 10;
        var longitude = 2 + random.NextDouble() * 10;
        var heading = random.NextDouble() * 360;
        var speed = 8 + random.NextDouble() * 10;
        var voyage = new List<AisMessage>(MessagesPerVessel);

        for (var i = 0; i < MessagesPerVessel; i++)
        {
            voyage.Add(new AisMessage(vesselId, timestamp, latitude, longitude, Math.Round(speed, 1)));

            var seconds = reportIntervalSeconds + random.Next(0, 3);
            var distanceNm = speed * seconds / 3600;
            var headingRadians = heading * Math.PI / 180;

            latitude += distanceNm * Math.Cos(headingRadians) / 60;
            longitude += distanceNm * Math.Sin(headingRadians) / (60 * Math.Cos(latitude * Math.PI / 180));
            timestamp = timestamp.AddSeconds(seconds);
            heading = (heading + random.NextDouble() * 6 - 3 + 360) % 360;
            speed = Math.Clamp(speed + random.NextDouble() - 0.5, 6, 20);
        }

        return voyage;
    }

    private static string ToCsvLine(AisMessage message) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{message.VesselId},{message.Timestamp:s}Z,{message.Latitude:F6},{message.Longitude:F6},{message.SpeedKnots:F1}");
}
