namespace AisVoyage;

public record VesselState(
    (double Latitude, double Longitude) LastPosition,
    DateTime LastTimestamp,
    double TotalDistanceNm,
    double TotalFuelTonnes);
