using System;
using System.Text.Json.Serialization;

namespace AutoLedgerSimple.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScheduleStatus
{
    Upcoming = 1,
    DueSoon = 2,
    Overdue = 3,
    Completed = 4
}

public class MaintenanceSchedule
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateOnly DueDate { get; set; }
    public int DueOdometerKm { get; set; }
    [JsonInclude] public ScheduleStatus Status { get; private set; }
    [JsonInclude] public DateOnly? CompletedOn { get; private set; }

    public MaintenanceSchedule()
    {
        Status = ScheduleStatus.Upcoming;
    }

    public MaintenanceSchedule(Guid id, Guid vehicleId, DateOnly dueDate, int dueOdometerKm)
    {
        Id = id;
        VehicleId = vehicleId;
        DueDate = dueDate;
        DueOdometerKm = dueOdometerKm;
        Status = ScheduleStatus.Upcoming;
    }

    public ScheduleStatus GetStatus(DateOnly today)
    {
        if (Status == ScheduleStatus.Completed)
            return ScheduleStatus.Completed;

        var daysDiff = (today.DayNumber - DueDate.DayNumber);
        
        if (daysDiff > 0)
            return ScheduleStatus.Overdue;
        if (daysDiff >= -14)
            return ScheduleStatus.DueSoon;
        
        return ScheduleStatus.Upcoming;
    }

    public ScheduleStatus GetStatus(DateOnly today, int currentOdometerKm)
    {
        if (Status == ScheduleStatus.Completed)
            return ScheduleStatus.Completed;

        var daysDiff = (today.DayNumber - DueDate.DayNumber);
        var kmDiff = currentOdometerKm - DueOdometerKm;

        if (daysDiff > 0 || kmDiff > 0)
            return ScheduleStatus.Overdue;
        if (daysDiff >= -14 || kmDiff >= -500)
            return ScheduleStatus.DueSoon;

        return ScheduleStatus.Upcoming;
    }

    public void MarkCompleted(DateOnly completionDate)
    {
        Status = ScheduleStatus.Completed;
        CompletedOn = completionDate;
    }

    public static string FormatStatus(ScheduleStatus status)
        => status switch
        {
            ScheduleStatus.Upcoming => "Upcoming",
            ScheduleStatus.DueSoon => "Due Soon",
            ScheduleStatus.Overdue => "Overdue",
            ScheduleStatus.Completed => "Completed",
            _ => status.ToString()
        };
}