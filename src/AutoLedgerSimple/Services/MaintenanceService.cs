using AutoLedgerSimple.Data;
using AutoLedgerSimple.Models;

namespace AutoLedgerSimple.Services;

public class MaintenanceService
{
    private readonly FleetDatabase _database;

    public MaintenanceService(FleetDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public ServiceRecord RecordService(
        Guid vehicleId,
        DateOnly serviceDate,
        int odometerAtServiceKm,
        ServiceCategory category,
        string description,
        decimal partsCost,
        decimal laborCost,
        decimal discountPercent = 0m,
        bool applyVat = false,
        int? customNextIntervalDays = null,
        int? customNextIntervalKm = null,
        DateOnly? today = null)
    {
        Vehicle vehicle = _database.Vehicles.FirstOrDefault(v => v.Id == vehicleId)
            ?? throw new KeyNotFoundException($"Vehicle '{vehicleId}' was not found.");

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Service description cannot be empty.", nameof(description));

        if (!Enum.IsDefined(category))
            throw new ArgumentException("Invalid service category.", nameof(category));

        DateOnly currentDay = today ?? DateOnly.FromDateTime(DateTime.Today);
        if (serviceDate > currentDay)
            throw new ArgumentException("Service date cannot be in the future.", nameof(serviceDate));

        if (odometerAtServiceKm < 0)
            throw new ArgumentException("Odometer reading cannot be negative.", nameof(odometerAtServiceKm));

        if (partsCost < 0m)
            throw new ArgumentException("Parts cost cannot be negative.", nameof(partsCost));

        if (laborCost < 0m)
            throw new ArgumentException("Labor cost cannot be negative.", nameof(laborCost));

        if (discountPercent < 0m || discountPercent > 100m)
            throw new ArgumentException("Discount percentage must be between 0 and 100.", nameof(discountPercent));

        if (customNextIntervalDays.HasValue && customNextIntervalDays.Value <= 0)
            throw new ArgumentException("Custom next interval days must be positive.", nameof(customNextIntervalDays));

        if (customNextIntervalKm.HasValue && customNextIntervalKm.Value <= 0)
            throw new ArgumentException("Custom next interval km must be positive.", nameof(customNextIntervalKm));

        var vehicleRecords = _database.ServiceRecords
            .Where(r => r.VehicleId == vehicle.Id)
            .ToList();

        DateOnly? latestExistingServiceDate = vehicleRecords
            .Select(r => (DateOnly?)r.ServiceDate)
            .Max();

        bool isHistoricalEntry = serviceDate < vehicle.RegisteredOn
            || (latestExistingServiceDate.HasValue && serviceDate < latestExistingServiceDate.Value);

        if (isHistoricalEntry)
        {
            int? registrationOdometerKm = GetRegistrationOdometerKm(vehicle);

            int? earlierMaxKm = vehicleRecords
                .Where(r => r.ServiceDate < serviceDate)
                .Select(r => (int?)r.OdometerAtServiceKm)
                .Max();

            int? lowerBoundKm = earlierMaxKm;
            if (registrationOdometerKm.HasValue && serviceDate >= vehicle.RegisteredOn)
            {
                lowerBoundKm = lowerBoundKm.HasValue
                    ? Math.Max(lowerBoundKm.Value, registrationOdometerKm.Value)
                    : registrationOdometerKm.Value;
            }

            if (lowerBoundKm.HasValue && odometerAtServiceKm < lowerBoundKm.Value)
                throw new ArgumentException(
                    $"Historical service odometer ({odometerAtServiceKm:N0} km) cannot be lower than an earlier recorded reading ({lowerBoundKm.Value:N0} km).",
                    nameof(odometerAtServiceKm));

            int laterMinKm = vehicleRecords
                .Where(r => r.ServiceDate > serviceDate)
                .Select(r => r.OdometerAtServiceKm)
                .DefaultIfEmpty(vehicle.CurrentOdometerKm)
                .Min();

            int upperBoundKm = Math.Min(laterMinKm, vehicle.CurrentOdometerKm);
            if (registrationOdometerKm.HasValue && serviceDate < vehicle.RegisteredOn)
                upperBoundKm = Math.Min(upperBoundKm, registrationOdometerKm.Value);

            if (odometerAtServiceKm > upperBoundKm)
                throw new ArgumentException(
                    $"Historical service odometer ({odometerAtServiceKm:N0} km) cannot exceed a later recorded reading ({upperBoundKm:N0} km).",
                    nameof(odometerAtServiceKm));
        }
        else if (odometerAtServiceKm < vehicle.CurrentOdometerKm)
        {
            throw new ArgumentException(
                $"Service odometer ({odometerAtServiceKm:N0} km) cannot be lower than current odometer ({vehicle.CurrentOdometerKm:N0} km).",
                nameof(odometerAtServiceKm));
        }

        MaintenanceSchedule? nextSchedule = null;
        if (!isHistoricalEntry)
        {
            int nextDays = customNextIntervalDays ?? vehicle.DefaultIntervalDays;
            int nextKm = customNextIntervalKm ?? vehicle.DefaultIntervalKm;

            if ((long)serviceDate.DayNumber + nextDays > DateOnly.MaxValue.DayNumber)
                throw new ArgumentOutOfRangeException(
                    nameof(customNextIntervalDays),
                    "Next service due date exceeds the maximum supported date.");

            long nextDueOdometer = (long)odometerAtServiceKm + nextKm;
            if (nextDueOdometer > int.MaxValue)
                throw new ArgumentOutOfRangeException(
                    nameof(customNextIntervalKm),
                    "Next service due odometer exceeds the maximum supported mileage.");

            nextSchedule = new MaintenanceSchedule(
                Guid.NewGuid(),
                vehicle.Id,
                serviceDate.AddDays(nextDays),
                (int)nextDueOdometer);
        }

        var record = new ServiceRecord(
            Guid.NewGuid(),
            vehicle.Id,
            serviceDate,
            odometerAtServiceKm,
            category,
            description.Trim(),
            partsCost,
            laborCost,
            discountPercent,
            applyVat);

        ValidateFleetTotalsDoNotOverflow(record);

        if (!isHistoricalEntry && odometerAtServiceKm > vehicle.CurrentOdometerKm)
            vehicle.UpdateOdometer(odometerAtServiceKm);

        _database.ServiceRecords.Add(record);

        if (nextSchedule is not null)
        {
            foreach (var activeSchedule in _database.Schedules
                .Where(s => s.VehicleId == vehicle.Id && s.Status != ScheduleStatus.Completed))
            {
                activeSchedule.MarkCompleted(serviceDate);
            }

            _database.Schedules.Add(nextSchedule);
        }

        return record;
    }

    public List<ServiceRecord> GetServiceHistory(Guid? vehicleId = null)
    {
        IEnumerable<ServiceRecord> query = _database.ServiceRecords;

        if (vehicleId.HasValue)
            query = query.Where(r => r.VehicleId == vehicleId.Value);

        return query
            .OrderByDescending(r => r.ServiceDate)
            .ThenByDescending(r => r.OdometerAtServiceKm)
            .ToList();
    }

    public FleetCostSummary GetCostSummary()
        => BuildCostSummary(_database.Vehicles, _database.ServiceRecords);

    private static FleetCostSummary BuildCostSummary(
        IReadOnlyCollection<Vehicle> vehicles,
        IReadOnlyCollection<ServiceRecord> records)
    {
        decimal totalParts = records.Sum(r => r.PartsCost);
        decimal totalLabor = records.Sum(r => r.LaborCost);
        decimal totalDiscount = records.Sum(r => r.DiscountAmount);
        decimal totalVat = records.Sum(r => r.VatAmount);
        decimal totalSpend = records.Sum(r => r.TotalCost);
        decimal average = records.Count > 0
            ? Math.Round(totalSpend / records.Count, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var byVehicle = vehicles
            .Select(v =>
            {
                var vRecords = records.Where(r => r.VehicleId == v.Id).ToList();
                return new VehicleCostSummary(
                    v.Id,
                    v.PlateNumber,
                    $"{v.Year} {v.Make} {v.Model}",
                    v.Type,
                    vRecords.Count,
                    vRecords.Sum(r => r.PartsCost),
                    vRecords.Sum(r => r.LaborCost),
                    vRecords.Sum(r => r.DiscountAmount),
                    vRecords.Sum(r => r.VatAmount),
                    vRecords.Sum(r => r.TotalCost));
            })
            .OrderByDescending(s => s.TotalSpend)
            .ThenBy(s => s.PlateNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var byCategory = records
            .GroupBy(r => r.Category)
            .Select(g => new CategoryCostSummary(
                g.Key,
                g.Count(),
                g.Sum(r => r.TotalCost)))
            .OrderByDescending(c => c.TotalSpend)
            .ToList();

        return new FleetCostSummary(
            vehicles.Count,
            records.Count,
            totalParts,
            totalLabor,
            totalDiscount,
            totalVat,
            totalSpend,
            average,
            byVehicle,
            byCategory);
    }

    public List<ServiceAlertItem> GetServiceAlerts(DateOnly? today = null)
    {
        DateOnly currentDay = today ?? DateOnly.FromDateTime(DateTime.Today);
        var vehiclesById = _database.Vehicles.ToDictionary(v => v.Id);
        var alerts = new List<ServiceAlertItem>();

        foreach (var schedule in _database.Schedules.Where(s => s.Status != ScheduleStatus.Completed))
        {
            if (!vehiclesById.TryGetValue(schedule.VehicleId, out var vehicle))
                continue;

            ScheduleStatus status = schedule.GetStatus(currentDay, vehicle.CurrentOdometerKm);
            int daysRemaining = schedule.DueDate.DayNumber - currentDay.DayNumber;
            int kmRemaining = schedule.DueOdometerKm - vehicle.CurrentOdometerKm;

            alerts.Add(new ServiceAlertItem(
                schedule.Id,
                vehicle.Id,
                vehicle.PlateNumber,
                $"{vehicle.Year} {vehicle.Make} {vehicle.Model}",
                schedule.DueDate,
                schedule.DueOdometerKm,
                vehicle.CurrentOdometerKm,
                daysRemaining,
                kmRemaining,
                status));
        }

        return alerts
            .OrderBy(a => GetPriorityRank(a.Status))
            .ThenBy(a => a.DaysRemaining)
            .ThenBy(a => a.KmRemaining)
            .ToList();
    }

    public ScheduleStatus? GetVehicleScheduleStatus(Guid vehicleId, DateOnly? today = null)
    {
        DateOnly currentDay = today ?? DateOnly.FromDateTime(DateTime.Today);
        Vehicle? vehicle = _database.Vehicles.FirstOrDefault(v => v.Id == vehicleId);
        if (vehicle is null)
            return null;

        MaintenanceSchedule? active = _database.Schedules
            .Where(s => s.VehicleId == vehicleId && s.Status != ScheduleStatus.Completed)
            .OrderBy(s => s.DueDate)
            .FirstOrDefault();

        if (active is not null)
            return active.GetStatus(currentDay, vehicle.CurrentOdometerKm);

        return _database.Schedules.Any(s => s.VehicleId == vehicleId)
            ? ScheduleStatus.Completed
            : null;
    }

    public bool SeedSampleDataIfEmpty(DateOnly? today = null)
    {
        if (_database.Vehicles.Count > 0)
            return false;

        DateOnly currentDay = today ?? DateOnly.FromDateTime(DateTime.Today);
        var vehicleService = new VehicleService(_database);

        // 1. Passenger vehicle -> Due Soon (due in 7 days)
        var corolla = vehicleService.RegisterVehicle(
            VehicleType.Passenger,
            "LAG-482-KJ",
            "Toyota",
            "Corolla",
            2021,
            44_000,
            "PAU Admin Pool",
            currentDay.AddDays(-200));

        RecordService(
            corolla.Id,
            currentDay.AddDays(-173),
            48_200,
            ServiceCategory.RoutineOilChange,
            "Synthetic engine oil change, filter replacement, and brake inspection",
            partsCost: 65_000m,
            laborCost: 20_000m,
            discountPercent: 5m,
            applyVat: true,
            today: currentDay);

        // 2. Commercial vehicle -> Overdue (due 10 days ago)
        var hiace = vehicleService.RegisterVehicle(
            VehicleType.Commercial,
            "EKY-714-XT",
            "Toyota",
            "HiAce Bus",
            2019,
            82_000,
            "PAU Campus Shuttle",
            currentDay.AddDays(-150));

        RecordService(
            hiace.Id,
            currentDay.AddDays(-100),
            88_500,
            ServiceCategory.BrakeService,
            "Front and rear brake pad replacement and caliper servicing",
            partsCost: 110_000m,
            laborCost: 35_000m,
            discountPercent: 0m,
            applyVat: true,
            today: currentDay);

        // 3. Motorcycle -> Upcoming (due in 45 days)
        var boxer = vehicleService.RegisterVehicle(
            VehicleType.Motorcycle,
            "LSD-209-QM",
            "Bajaj",
            "Boxer 150",
            2023,
            9_200,
            "Campus Mail Dispatch",
            currentDay.AddDays(-40));

        RecordService(
            boxer.Id,
            currentDay.AddDays(-15),
            10_400,
            ServiceCategory.RoutineOilChange,
            "Oil change, chain tensioning, and spark plug check",
            partsCost: 12_000m,
            laborCost: 5_000m,
            discountPercent: 0m,
            applyVat: false,
            today: currentDay);

        return true;
    }

    private void ValidateFleetTotalsDoNotOverflow(ServiceRecord candidate)
    {
        var prospectiveRecords = new List<ServiceRecord>(_database.ServiceRecords.Count + 1);
        prospectiveRecords.AddRange(_database.ServiceRecords);
        prospectiveRecords.Add(candidate);

        try
        {
            _ = BuildCostSummary(_database.Vehicles, prospectiveRecords);
        }
        catch (OverflowException ex)
        {
            throw new OverflowException(
                "Recording this service would cause fleet cost totals to exceed the maximum supported amount.",
                ex);
        }
    }

    private int? GetRegistrationOdometerKm(Vehicle vehicle)
    {
        if (vehicle.RegisteredOdometerKm.HasValue)
            return vehicle.RegisteredOdometerKm.Value;

        if ((long)vehicle.RegisteredOn.DayNumber + vehicle.DefaultIntervalDays > DateOnly.MaxValue.DayNumber)
            return null;

        DateOnly initialDueDate = vehicle.RegisteredOn.AddDays(vehicle.DefaultIntervalDays);
        var initialSchedule = _database.Schedules
            .FirstOrDefault(s => s.VehicleId == vehicle.Id && s.DueDate == initialDueDate);

        if (initialSchedule is null || initialSchedule.DueOdometerKm < vehicle.DefaultIntervalKm)
            return null;

        return initialSchedule.DueOdometerKm - vehicle.DefaultIntervalKm;
    }

    private static int GetPriorityRank(ScheduleStatus status) => status switch
    {
        ScheduleStatus.Overdue => 0,
        ScheduleStatus.DueSoon => 1,
        ScheduleStatus.Upcoming => 2,
        ScheduleStatus.Completed => 3,
        _ => 4
    };
}
