namespace AutoLedgerSimple.Models;

public sealed class PassengerVehicle : Vehicle
{
    public override VehicleType Type => VehicleType.Passenger;
    public override int DefaultIntervalDays => 180;
    public override int DefaultIntervalKm => 5_000;
    public override decimal BaseInspectionFee => 15_000m;

    public PassengerVehicle() { }

    public PassengerVehicle(Guid id, string plateNumber, string make, string model, int year, int currentOdometerKm, string ownerName, DateOnly registeredOn)
        : base(id, plateNumber, make, model, year, currentOdometerKm, ownerName, registeredOn) { }

    public override string VehicleCategorySummary()
        => $"Passenger Vehicle - {Make} {Model} ({Year}) - Interval: {DefaultIntervalDays} days / {DefaultIntervalKm:N0} km - Base Fee: {FormatNaira(BaseInspectionFee)}";

    public override string GetDisplayName()
        => $"{PlateNumber} - {Make} {Model} ({Year}) [Passenger]";

    private static string FormatNaira(decimal amount)
        => $"₦{amount:N2}";
}

public sealed class CommercialVehicle : Vehicle
{
    public override VehicleType Type => VehicleType.Commercial;
    public override int DefaultIntervalDays => 90;
    public override int DefaultIntervalKm => 10_000;
    public override decimal BaseInspectionFee => 35_000m;

    public CommercialVehicle() { }

    public CommercialVehicle(Guid id, string plateNumber, string make, string model, int year, int currentOdometerKm, string ownerName, DateOnly registeredOn)
        : base(id, plateNumber, make, model, year, currentOdometerKm, ownerName, registeredOn) { }

    public override string VehicleCategorySummary()
        => $"Commercial Vehicle - {Make} {Model} ({Year}) - Interval: {DefaultIntervalDays} days / {DefaultIntervalKm:N0} km - Base Fee: {FormatNaira(BaseInspectionFee)}";

    public override string GetDisplayName()
        => $"{PlateNumber} - {Make} {Model} ({Year}) [Commercial]";

    private static string FormatNaira(decimal amount)
        => $"₦{amount:N2}";
}

public sealed class Motorcycle : Vehicle
{
    public override VehicleType Type => VehicleType.Motorcycle;
    public override int DefaultIntervalDays => 60;
    public override int DefaultIntervalKm => 3_000;
    public override decimal BaseInspectionFee => 8_000m;

    public Motorcycle() { }

    public Motorcycle(Guid id, string plateNumber, string make, string model, int year, int currentOdometerKm, string ownerName, DateOnly registeredOn)
        : base(id, plateNumber, make, model, year, currentOdometerKm, ownerName, registeredOn) { }

    public override string VehicleCategorySummary()
        => $"Motorcycle - {Make} {Model} ({Year}) - Interval: {DefaultIntervalDays} days / {DefaultIntervalKm:N0} km - Base Fee: {FormatNaira(BaseInspectionFee)}";

    public override string GetDisplayName()
        => $"{PlateNumber} - {Make} {Model} ({Year}) [Motorcycle]";

    private static string FormatNaira(decimal amount)
        => $"₦{amount:N2}";
}