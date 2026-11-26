using System;
using System.Text.Json.Serialization;

namespace AutoLedgerSimple.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServiceCategory
{
    RoutineOilChange = 1,
    BrakeService = 2,
    TireRotation = 3,
    BatteryReplacement = 4,
    EngineDiagnostics = 5,
    TransmissionService = 6,
    EmergencyRepair = 7
}

public class ServiceRecord
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateOnly ServiceDate { get; set; }
    public int OdometerAtServiceKm { get; set; }
    public ServiceCategory Category { get; set; }
    public string Description { get; set; }
    public decimal PartsCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal DiscountPercent { get; set; }
    public bool ApplyVat { get; set; }
    [JsonInclude] public decimal TotalCost { get; private set; }
    [JsonInclude] public decimal VatAmount { get; private set; }
    [JsonInclude] public decimal DiscountAmount { get; private set; }
    [JsonInclude] public decimal SubTotal { get; private set; }

    public ServiceRecord()
    {
        Description = string.Empty;
    }

    public ServiceRecord(Guid id, Guid vehicleId, DateOnly serviceDate, int odometerAtServiceKm,
        ServiceCategory category, string description, decimal partsCost, decimal laborCost,
        decimal discountPercent, bool applyVat)
    {
        Id = id;
        VehicleId = vehicleId;
        ServiceDate = serviceDate;
        OdometerAtServiceKm = odometerAtServiceKm;
        Category = category;
        Description = description ?? string.Empty;
        PartsCost = partsCost;
        LaborCost = laborCost;
        DiscountPercent = discountPercent;
        ApplyVat = applyVat;
        RecalculateCosts();
    }

    public void RecalculateCosts()
    {
        SubTotal = PartsCost + LaborCost;
        DiscountAmount = Math.Round(SubTotal * (DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero);
        var afterDiscount = SubTotal - DiscountAmount;
        VatAmount = ApplyVat ? Math.Round(afterDiscount * 0.075m, 2, MidpointRounding.AwayFromZero) : 0m;
        TotalCost = afterDiscount + VatAmount;
    }

    public static string FormatNaira(decimal amount)
        => $"₦{amount:N2}";
}