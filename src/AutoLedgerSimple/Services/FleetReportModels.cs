using AutoLedgerSimple.Models;

namespace AutoLedgerSimple.Services;

public record VehicleCostSummary(
    Guid VehicleId,
    string PlateNumber,
    string VehicleLabel,
    VehicleType Type,
    int ServiceCount,
    decimal TotalPartsCost,
    decimal TotalLaborCost,
    decimal TotalDiscountAmount,
    decimal TotalVatAmount,
    decimal TotalSpend);

public record CategoryCostSummary(
    ServiceCategory Category,
    int ServiceCount,
    decimal TotalSpend);

public record FleetCostSummary(
    int TotalVehicles,
    int TotalServices,
    decimal TotalPartsCost,
    decimal TotalLaborCost,
    decimal TotalDiscountAmount,
    decimal TotalVatAmount,
    decimal TotalSpend,
    decimal AverageCostPerService,
    List<VehicleCostSummary> ByVehicle,
    List<CategoryCostSummary> ByCategory);

public record ServiceAlertItem(
    Guid ScheduleId,
    Guid VehicleId,
    string PlateNumber,
    string VehicleLabel,
    DateOnly DueDate,
    int DueOdometerKm,
    int CurrentOdometerKm,
    int DaysRemaining,
    int KmRemaining,
    ScheduleStatus Status);
