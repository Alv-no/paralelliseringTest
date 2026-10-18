namespace AisVoyage;

public record VoyageReport(IReadOnlyDictionary<string, (double DistanceNm, double FuelTonnes)> Vessels)
{
    private const int Decimals = 6;

    public static VoyageReport From(IReadOnlyDictionary<string, VesselState> states)
    {
        var vessels = new SortedDictionary<string, (double DistanceNm, double FuelTonnes)>(StringComparer.Ordinal);

        foreach (var (vesselId, state) in states)
            vessels[vesselId] = (Math.Round(state.TotalDistanceNm, Decimals), Math.Round(state.TotalFuelTonnes, Decimals));

        return new VoyageReport(vessels);
    }
}
