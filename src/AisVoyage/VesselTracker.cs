using System.Collections.Concurrent;

namespace AisVoyage;

public class VesselTracker
{
    private const double EarthRadiusNm = 3440.065;

    private readonly ConcurrentDictionary<string, VesselState> _states = new();

    public IReadOnlyDictionary<string, VesselState> States => _states;

    public void Apply(AisMessage message)
    {
        var position = (message.Latitude, message.Longitude);

        if (!_states.TryGetValue(message.VesselId, out var previous))
        {
            _states[message.VesselId] = new VesselState(position, message.Timestamp, 0, 0);
            return;
        }

        var distance = DistanceNm(previous.LastPosition, position);
        var fuel = FuelEstimator.Estimate(distance, message.SpeedKnots);

        _states[message.VesselId] = new VesselState(
            position,
            message.Timestamp,
            previous.TotalDistanceNm + distance,
            previous.TotalFuelTonnes + fuel);
    }

    private static double DistanceNm((double Latitude, double Longitude) from, (double Latitude, double Longitude) to)
    {
        var lat1 = ToRadians(from.Latitude);
        var lat2 = ToRadians(to.Latitude);
        var deltaLat = lat2 - lat1;
        var deltaLon = ToRadians(to.Longitude - from.Longitude);

        var a = Math.Pow(Math.Sin(deltaLat / 2), 2)
              + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(deltaLon / 2), 2);

        return 2 * EarthRadiusNm * Math.Asin(Math.Sqrt(a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
