namespace AisVoyage;

public record AisMessage(string VesselId, DateTime Timestamp, double Latitude, double Longitude, double SpeedKnots);
