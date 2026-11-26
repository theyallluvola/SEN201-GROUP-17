using AutoLedgerSimple.Data;
using AutoLedgerSimple.Models;

namespace AutoLedgerSimple.Services;

public class VehicleService
{
    private readonly FleetDatabase _database;

    public VehicleService(FleetDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Vehicle RegisterVehicle(
        VehicleType type,
        string plateNumber,
        string make,
        string model,
        int year,
        int currentOdometerKm,
        string ownerName,
        DateOnly? registeredOn = null)
    {
        if (string.IsNullOrWhiteSpace(plateNumber))
            throw new ArgumentException("Plate number cannot be empty.", nameof(plateNumber));

        string normalizedPlate = plateNumber.Trim().ToUpperInvariant();
        bool duplicateExists = _database.Vehicles.Any(
            v => string.Equals(v.PlateNumber, normalizedPlate, StringComparison.OrdinalIgnoreCase));

        if (duplicateExists)
            throw new InvalidOperationException($"A vehicle with plate number '{normalizedPlate}' is already registered.");

        DateOnly registrationDate = registeredOn ?? DateOnly.FromDateTime(DateTime.Today);
        Vehicle vehicle = Vehicle.Create(
            type,
            normalizedPlate,
            make,
            model,
            year,
            currentOdometerKm,
            ownerName,
            registrationDate);

        _database.Vehicles.Add(vehicle);

        var initialSchedule = new MaintenanceSchedule(
            Guid.NewGuid(),
            vehicle.Id,
            registrationDate.AddDays(vehicle.DefaultIntervalDays),
            vehicle.CurrentOdometerKm + vehicle.DefaultIntervalKm);

        _database.Schedules.Add(initialSchedule);
        return vehicle;
    }

    public List<Vehicle> GetAllVehicles()
        => _database.Vehicles
            .OrderBy(v => v.PlateNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public Vehicle? FindByPlate(string plateNumber)
    {
        if (string.IsNullOrWhiteSpace(plateNumber))
            return null;

        string normalized = plateNumber.Trim().ToUpperInvariant();
        return _database.Vehicles.FirstOrDefault(
            v => string.Equals(v.PlateNumber, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public Vehicle? FindById(Guid vehicleId)
        => _database.Vehicles.FirstOrDefault(v => v.Id == vehicleId);

    public Vehicle UpdateOdometer(string plateNumber, int newOdometerKm)
    {
        Vehicle vehicle = FindByPlate(plateNumber)
            ?? throw new KeyNotFoundException($"No vehicle found with plate number '{plateNumber}'.");

        vehicle.UpdateOdometer(newOdometerKm);
        return vehicle;
    }
}
