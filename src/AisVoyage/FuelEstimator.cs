namespace AisVoyage;

public static class FuelEstimator
{
    private const int StepsPerNauticalMile = 80_000;
    private const double DisplacementTonnes = 40_000;
    private const double TonnesPerHourPerKnotCubed = 0.00058;

    public static double Estimate(double distanceNm, double speedKnots)
    {
        if (distanceNm <= 0 || speedKnots <= 0)
            return 0;

        var steps = (int)Math.Ceiling(distanceNm * StepsPerNauticalMile);
        var hoursPerStep = distanceNm / speedKnots / steps;
        var baseRate = TonnesPerHourPerKnotCubed * Math.Pow(speedKnots, 3);
        var displacement = DisplacementTonnes;
        var fuel = 0.0;

        for (var step = 0; step < steps; step++)
        {
            var loadFactor = Math.Pow(displacement / DisplacementTonnes, 2.0 / 3.0);
            var burned = baseRate * loadFactor * hoursPerStep;
            fuel += burned;
            displacement -= burned;
        }

        return fuel;
    }
}
