using System.Collections.Generic;
using AutoLedgerSimple.Models;

namespace AutoLedgerSimple.Data;

public class FleetDatabase
{
    public List<Vehicle> Vehicles { get; set; } = new();
    public List<ServiceRecord> ServiceRecords { get; set; } = new();
    public List<MaintenanceSchedule> Schedules { get; set; } = new();
}