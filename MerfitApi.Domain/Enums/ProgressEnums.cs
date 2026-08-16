namespace Merfit.Domain.Enums;

/// <summary>
/// Extensible-by-design: new metric types can be added here without touching the WeightEntry
/// table. Weight is intentionally also trackable through the dedicated WeightEntry entity because
/// it is the single most frequently queried metric (goal tracking, dashboard, progress chart).
/// </summary>
public enum MetricType
{
    Weight,
    BodyFat,
    Chest,
    Waist,
    Arm,
    Hip,
    Thigh
}
