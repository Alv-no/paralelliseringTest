using System.Globalization;

namespace AisVoyage;

public static class AisStreamReader
{
    public static async IAsyncEnumerable<AisMessage> ReadAsync(Stream input)
    {
        using var reader = new StreamReader(input, leaveOpen: true);

        while (await reader.ReadLineAsync() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                yield return Parse(line);
        }
    }

    private static AisMessage Parse(string line)
    {
        var fields = line.Split(',');

        return new AisMessage(
            fields[0],
            DateTime.Parse(fields[1], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
            double.Parse(fields[2], CultureInfo.InvariantCulture),
            double.Parse(fields[3], CultureInfo.InvariantCulture),
            double.Parse(fields[4], CultureInfo.InvariantCulture));
    }
}
