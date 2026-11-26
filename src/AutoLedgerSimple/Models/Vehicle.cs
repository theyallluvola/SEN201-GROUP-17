using System;
using System.Text.Json.Serialization;

namespace AutoLedgerSimple.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "vehicleClass")]
[JsonDerivedType(typeof(PassengerVehicle), "Passenger")]
[JsonDerivedType(typeof(CommercialVehicle), "Commercial")]
[JsonDerivedType(typeof(Motorcycle), "Motorcycle")]
public abstract class Vehicle
{
    [JsonInclude] public Guid Id { get; protected set; }
    [JsonInclude] public string PlateNumber { get; protected set; } = string.Empty;
    [JsonInclude] public string Make { get; protected set; } = string.Empty;
    [JsonInclude] public string Model { get; protected set; } = string.Empty;
    [JsonInclude] public int Year { get; protected set; }
    [JsonInclude] public int CurrentOdometerKm { get; protected set; }
    [JsonInclude] public int? RegisteredOdometerKm { get; protected set; }
    [JsonInclude] public string OwnerName { get; protected set; } = string.Empty;
    [JsonInclude] public DateOnly RegisteredOn { get; protected set; }

    internal void SetMake(string make) => Make = make ?? string.Empty;
    internal void SetModel(string model) => Model = model ?? string.Empty;
    internal void SetYear(int year) => Year = year;
    internal void SetOdometer(int odometer) => CurrentOdometerKm = odometer;
    internal void SetOwnerName(string ownerName) => OwnerName = ownerName ?? string.Empty;

    public abstract VehicleType Type { get; }
    public virtual int DefaultIntervalDays => 180;
    public virtual int DefaultIntervalKm => 5_000;
    public virtual decimal BaseInspectionFee => 15_000m;

    protected Vehicle() { }

    protected Vehicle(Guid id, string plateNumber, string make, string model, int year, int currentOdometerKm, string ownerName, DateOnly registeredOn)
    {
        if (string.IsNullOrWhiteSpace(plateNumber))
            throw new ArgumentException("Plate number cannot be empty.", nameof(plateNumber));
        if (string.IsNullOrWhiteSpace(make))
            throw new ArgumentException("Make cannot be empty.", nameof(make));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model cannot be empty.", nameof(model));
        if (year < 1900 || year > DateTime.Now.Year + 1)
            throw new ArgumentException($"Year must be between 1900 and {DateTime.Now.Year + 1}.", nameof(year));
        if (currentOdometerKm < 0)
            throw new ArgumentException("Odometer cannot be negative.", nameof(currentOdometerKm));
        if (string.IsNullOrWhiteSpace(ownerName))
            throw new ArgumentException("Owner name cannot be empty.", nameof(ownerName));

        Id = id;
        PlateNumber = plateNumber.Trim().ToUpperInvariant();
        Make = make.Trim();
        Model = model.Trim();
        Year = year;
        CurrentOdometerKm = currentOdometerKm;
        RegisteredOdometerKm = currentOdometerKm;
        OwnerName = ownerName.Trim();
        RegisteredOn = registeredOn;
    }

    public static Vehicle Create(VehicleType type, string plateNumber, string make, string model, int year, int currentOdometerKm, string ownerName, DateOnly registeredOn)
    {
        return type switch
        {
            VehicleType.Passenger => new PassengerVehicle(Guid.NewGuid(), plateNumber, make, model, year, currentOdometerKm, ownerName, registeredOn),
            VehicleType.Commercial => new CommercialVehicle(Guid.NewGuid(), plateNumber, make, model, year, currentOdometerKm, ownerName, registeredOn),
            VehicleType.Motorcycle => new Motorcycle(Guid.NewGuid(), plateNumber, make, model, year, currentOdometerKm, ownerName, registeredOn),
            _ => throw new ArgumentException("Invalid vehicle type.", nameof(type))
        };
    }

    public void UpdateOdometer(int newOdometerKm)
    {
        if (newOdometerKm < CurrentOdometerKm)
            throw new ArgumentException($"New odometer ({newOdometerKm}) cannot be lower than current ({CurrentOdometerKm}).", nameof(newOdometerKm));
        if (newOdometerKm < 0)
            throw new ArgumentException("Odometer cannot be negative.", nameof(newOdometerKm));

        CurrentOdometerKm = newOdometerKm;
    }

    public abstract string VehicleCategorySummary();
    public abstract string GetDisplayName();
}

public enum VehicleType
{
    Passenger = 1,
    Commercial = 2,
    Motorcycle = 3
}