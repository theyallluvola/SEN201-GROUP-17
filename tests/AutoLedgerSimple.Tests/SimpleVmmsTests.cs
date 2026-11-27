using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using AutoLedgerSimple.Data;
using AutoLedgerSimple.Models;
using AutoLedgerSimple.Services;
using AutoLedgerSimple.UI;
using Xunit;

namespace AutoLedgerSimple.Tests;

/// <summary>Fake store that keeps the database in memory and counts saves.</summary>
internal sealed class InMemoryFleetStore : IFleetStore
{
    private FleetDatabase _database;

    public InMemoryFleetStore(FleetDatabase? seed = null)
    {
        _database = seed ?? new FleetDatabase();
    }

    public string FilePath => "memory://fleet";

    public int SaveCount { get; private set; }

    public FleetDatabase Load() => _database;

    public void Save(FleetDatabase database)
    {
        _database = database;
        SaveCount++;
    }
}

public class SimpleVmmsTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private sealed class Fixture
    {
        public FleetDatabase Database { get; } = new();
        public InMemoryFleetStore Store { get; }
        public VehicleService Vehicles { get; }
        public MaintenanceService Maintenance { get; }

        public Fixture(bool seedSampleData)
        {
            Store = new InMemoryFleetStore(Database);
            Vehicles = new VehicleService(Database);
            Maintenance = new MaintenanceService(Database);

            if (seedSampleData)
                Maintenance.SeedSampleDataIfEmpty(Today);
        }

        public MainForm CreateForm() => new(Store, Database, Vehicles, Maintenance);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static T Find<T>(Control root, string name) where T : Control
        => root.Controls.Find(name, true).OfType<T>().Single();

    private static Vehicle RegisterCamryWithCurrentService(Fixture fixture)
    {
        Vehicle vehicle = fixture.Vehicles.RegisterVehicle(
            VehicleType.Passenger, "HIS-100-AA", "Toyota", "Camry", 2020, 20_000, "Test Owner", Today.AddDays(-100));

        fixture.Maintenance.RecordService(
            vehicle.Id, Today.AddDays(-10), 21_000, ServiceCategory.RoutineOilChange,
            "Oil change", 10_000m, 5_000m, today: Today);

        return vehicle;
    }

    // ===================================================================== Unit

    [Fact]
    public void TryParseInt_AcceptsPlainAndThousandsSeparatedNumbers()
    {
        Assert.True(UiValidationHelper.TryParseInt("44,000", out int a));
        Assert.Equal(44_000, a);

        Assert.True(UiValidationHelper.TryParseInt("  12 ", out int b));
        Assert.Equal(12, b);
    }

    [Fact]
    public void TryParseInt_RejectsBlankGarbageAndOverflow()
    {
        Assert.False(UiValidationHelper.TryParseInt(null, out _));
        Assert.False(UiValidationHelper.TryParseInt("   ", out _));
        Assert.False(UiValidationHelper.TryParseInt("12.5", out _));
        Assert.False(UiValidationHelper.TryParseInt("abc", out _));
        Assert.False(UiValidationHelper.TryParseInt("99999999999", out _));
    }

    [Fact]
    public void TryParseDecimal_AcceptsNairaSymbolAndCommas()
    {
        Assert.True(UiValidationHelper.TryParseDecimal("₦65,000.50", out decimal a));
        Assert.Equal(65_000.50m, a);

        Assert.True(UiValidationHelper.TryParseDecimal(" 20000 ", out decimal b));
        Assert.Equal(20_000m, b);
    }

    [Fact]
    public void TryParseDecimal_RejectsGarbage()
    {
        Assert.False(UiValidationHelper.TryParseDecimal(null, out _));
        Assert.False(UiValidationHelper.TryParseDecimal("", out _));
        Assert.False(UiValidationHelper.TryParseDecimal("₦", out _));
        Assert.False(UiValidationHelper.TryParseDecimal("abc", out _));
        Assert.False(UiValidationHelper.TryParseDecimal("12.5.3", out _));
    }

    [Fact]
    public void TryParseDateOnly_AcceptsIsoAndDayFirstFormats()
    {
        Assert.True(UiValidationHelper.TryParseDateOnly("2026-11-26", out DateOnly iso));
        Assert.Equal(new DateOnly(2026, 11, 26), iso);

        Assert.True(UiValidationHelper.TryParseDateOnly("26/11/2026", out DateOnly dayFirst));
        Assert.Equal(new DateOnly(2026, 11, 26), dayFirst);

        Assert.True(UiValidationHelper.TryParseDateOnly("6/1/2026", out DateOnly shortForm));
        Assert.Equal(new DateOnly(2026, 1, 6), shortForm);
    }

    [Fact]
    public void TryParseDateOnly_RejectsImpossibleOrUnknownDates()
    {
        Assert.False(UiValidationHelper.TryParseDateOnly(null, out _));
        Assert.False(UiValidationHelper.TryParseDateOnly("", out _));
        Assert.False(UiValidationHelper.TryParseDateOnly("2026-02-30", out _));
        Assert.False(UiValidationHelper.TryParseDateOnly("31/04/2026", out _));
        Assert.False(UiValidationHelper.TryParseDateOnly("yesterday", out _));
    }

    [Fact]
    public void FormatNaira_UsesSymbolSeparatorsAndTwoDecimals()
    {
        Assert.Equal("₦1,234.50", UiValidationHelper.FormatNaira(1234.5m));
        Assert.Equal("₦0.00", UiValidationHelper.FormatNaira(0m));
    }

    [Fact]
    public void FormatCategory_SplitsPascalCaseIntoWords()
    {
        Assert.Equal("Routine Oil Change", UiValidationHelper.FormatCategory(ServiceCategory.RoutineOilChange));
        Assert.Equal("Brake Service", UiValidationHelper.FormatCategory(ServiceCategory.BrakeService));
        Assert.Equal("99", UiValidationHelper.FormatCategory((ServiceCategory)99));
    }

    [Fact]
    public void ServiceRecord_AppliesDiscountBeforeSevenPointFivePercentVat()
    {
        var record = new ServiceRecord(
            Guid.NewGuid(), Guid.NewGuid(), Today, 1_000, ServiceCategory.RoutineOilChange,
            "Oil", 65_000m, 20_000m, 5m, true);

        Assert.Equal(85_000m, record.SubTotal);
        Assert.Equal(4_250m, record.DiscountAmount);
        Assert.Equal(6_056.25m, record.VatAmount);
        Assert.Equal(86_806.25m, record.TotalCost);
    }

    // ===================================================================== Integration

    [Fact]
    public void JsonFleetStore_RoundTripsVehiclesRecordsAndSchedules()
    {
        string directory = Path.Combine(Path.GetTempPath(), "autoledger-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonFleetStore(Path.Combine(directory, "fleet.json"));
            Assert.Empty(store.Load().Vehicles);

            var fixture = new Fixture(seedSampleData: true);
            store.Save(fixture.Database);
            FleetDatabase loaded = store.Load();

            Assert.Equal(3, loaded.Vehicles.Count);
            Assert.Single(loaded.Vehicles.OfType<PassengerVehicle>());
            Assert.Single(loaded.Vehicles.OfType<CommercialVehicle>());
            Assert.Single(loaded.Vehicles.OfType<Motorcycle>());
            Assert.Equal(44_000, loaded.Vehicles.OfType<PassengerVehicle>().Single().RegisteredOdometerKm);

            Assert.Equal(3, loaded.ServiceRecords.Count);
            Assert.Equal(
                fixture.Database.ServiceRecords.Sum(r => r.TotalCost),
                loaded.ServiceRecords.Sum(r => r.TotalCost));

            Assert.Equal(6, loaded.Schedules.Count);
            Assert.Equal(3, loaded.Schedules.Count(s => s.Status == ScheduleStatus.Completed));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RegisterVehicle_CreatesInitialScheduleFromTypeDefaults()
    {
        var fixture = new Fixture(seedSampleData: false);

        Vehicle vehicle = fixture.Vehicles.RegisterVehicle(
            VehicleType.Passenger, "ABC-123-XY", "Honda", "Civic", 2022, 10_000, "Fleet Office", Today);

        Assert.IsType<PassengerVehicle>(vehicle);
        Assert.Equal(10_000, vehicle.RegisteredOdometerKm);

        MaintenanceSchedule schedule = Assert.Single(fixture.Database.Schedules);
        Assert.Equal(vehicle.Id, schedule.VehicleId);
        Assert.Equal(Today.AddDays(180), schedule.DueDate);
        Assert.Equal(15_000, schedule.DueOdometerKm);
    }

    [Fact]
    public void RegisterVehicle_RejectsDuplicatePlateIgnoringCaseAndSpaces()
    {
        var fixture = new Fixture(seedSampleData: false);
        fixture.Vehicles.RegisterVehicle(
            VehicleType.Passenger, "ABC-123-XY", "Honda", "Civic", 2022, 10_000, "Fleet Office", Today);

        Assert.Throws<InvalidOperationException>(() => fixture.Vehicles.RegisterVehicle(
            VehicleType.Commercial, " abc-123-xy ", "Toyota", "Hilux", 2021, 5_000, "Other Owner", Today));
    }

    [Fact]
    public void RecordService_CompletesOldScheduleAndRollsNextOneForward()
    {
        var fixture = new Fixture(seedSampleData: false);
        Vehicle vehicle = fixture.Vehicles.RegisterVehicle(
            VehicleType.Passenger, "TST-001-AA", "Toyota", "Corolla", 2021, 10_000, "Test Owner", Today.AddDays(-30));

        fixture.Maintenance.RecordService(
            vehicle.Id, Today.AddDays(-5), 10_800, ServiceCategory.BrakeService,
            "Brake pads", 20_000m, 5_000m, today: Today);

        List<MaintenanceSchedule> schedules = fixture.Database.Schedules.Where(s => s.VehicleId == vehicle.Id).ToList();
        Assert.Equal(2, schedules.Count);

        MaintenanceSchedule completed = Assert.Single(schedules, s => s.Status == ScheduleStatus.Completed);
        Assert.Equal(Today.AddDays(-5), completed.CompletedOn);

        MaintenanceSchedule active = Assert.Single(schedules, s => s.Status != ScheduleStatus.Completed);
        Assert.Equal(Today.AddDays(-5).AddDays(180), active.DueDate);
        Assert.Equal(15_800, active.DueOdometerKm);
        Assert.Equal(10_800, vehicle.CurrentOdometerKm);
    }

    [Fact]
    public void RecordService_HistoricalEntryKeepsMileageAndScheduleUntouched()
    {
        var fixture = new Fixture(seedSampleData: false);
        Vehicle vehicle = RegisterCamryWithCurrentService(fixture);
        MaintenanceSchedule activeBefore = fixture.Database.Schedules.Single(
            s => s.VehicleId == vehicle.Id && s.Status != ScheduleStatus.Completed);
        DateOnly dueDateBefore = activeBefore.DueDate;

        fixture.Maintenance.RecordService(
            vehicle.Id, Today.AddDays(-50), 20_500, ServiceCategory.TireRotation,
            "Late-entered tyre rotation", 2_000m, 1_000m, today: Today);

        Assert.Equal(21_000, vehicle.CurrentOdometerKm);
        Assert.Equal(2, fixture.Database.Schedules.Count(s => s.VehicleId == vehicle.Id));

        MaintenanceSchedule activeAfter = fixture.Database.Schedules.Single(
            s => s.VehicleId == vehicle.Id && s.Status != ScheduleStatus.Completed);
        Assert.Equal(activeBefore.Id, activeAfter.Id);
        Assert.Equal(dueDateBefore, activeAfter.DueDate);

        List<ServiceRecord> history = fixture.Maintenance.GetServiceHistory(vehicle.Id);
        Assert.Equal(2, history.Count);
        Assert.Equal(Today.AddDays(-10), history[0].ServiceDate);
    }

    [Fact]
    public void RecordService_HistoricalOdometerAboveLaterReadingIsRejected()
    {
        var fixture = new Fixture(seedSampleData: false);
        Vehicle vehicle = RegisterCamryWithCurrentService(fixture);

        Assert.Throws<ArgumentException>(() => fixture.Maintenance.RecordService(
            vehicle.Id, Today.AddDays(-50), 25_000, ServiceCategory.TireRotation,
            "Impossible mileage", 2_000m, 1_000m, today: Today));

        Assert.Single(fixture.Maintenance.GetServiceHistory(vehicle.Id));
    }

    [Fact]
    public void RecordService_ServiceBeforeRegistrationCannotExceedRegistrationMileage()
    {
        var fixture = new Fixture(seedSampleData: false);
        Vehicle vehicle = fixture.Vehicles.RegisterVehicle(
            VehicleType.Passenger, "OLD-777-ZZ", "Toyota", "Avensis", 2018, 20_000, "Test Owner", Today.AddDays(-30));

        Assert.Throws<ArgumentException>(() => fixture.Maintenance.RecordService(
            vehicle.Id, Today.AddDays(-60), 20_500, ServiceCategory.EngineDiagnostics,
            "Too many km", 1_000m, 1_000m, today: Today));

        fixture.Maintenance.RecordService(
            vehicle.Id, Today.AddDays(-60), 19_000, ServiceCategory.EngineDiagnostics,
            "Before registration", 1_000m, 1_000m, today: Today);

        Assert.Equal(20_000, vehicle.CurrentOdometerKm);
        Assert.Single(fixture.Maintenance.GetServiceHistory(vehicle.Id));
    }

    [Fact]
    public void Schedule_IsOverdueWhenMileageIsExceededEvenIfDateIsFarAway()
    {
        var schedule = new MaintenanceSchedule(Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(100), 10_000);

        Assert.Equal(ScheduleStatus.Overdue, schedule.GetStatus(Today, 10_001));
        Assert.Equal(ScheduleStatus.DueSoon, schedule.GetStatus(Today, 9_600));
        Assert.Equal(ScheduleStatus.Upcoming, schedule.GetStatus(Today, 5_000));
    }

    // ===================================================================== Acceptance (service level)

    [Fact]
    public void Acceptance_SampleDataCostTotalsAreCorrect()
    {
        var fixture = new Fixture(seedSampleData: true);

        FleetCostSummary summary = fixture.Maintenance.GetCostSummary();

        Assert.Equal(3, summary.TotalVehicles);
        Assert.Equal(3, summary.TotalServices);
        Assert.Equal(259_681.25m, summary.TotalSpend);
        Assert.Equal(86_560.42m, summary.AverageCostPerService);
        Assert.Equal("EKY-714-XT", summary.ByVehicle[0].PlateNumber);

        Assert.Equal(ServiceCategory.BrakeService, summary.ByCategory[0].Category);
        Assert.Equal(155_875m, summary.ByCategory[0].TotalSpend);
        Assert.Equal(ServiceCategory.RoutineOilChange, summary.ByCategory[1].Category);
        Assert.Equal(103_806.25m, summary.ByCategory[1].TotalSpend);
    }

    [Fact]
    public void Acceptance_FirstRunSampleDataShowsOverdueDueSoonAndUpcoming()
    {
        var fixture = new Fixture(seedSampleData: true);

        List<ServiceAlertItem> alerts = fixture.Maintenance.GetServiceAlerts(Today);

        Assert.Equal(3, alerts.Count);
        Assert.Equal(("EKY-714-XT", ScheduleStatus.Overdue), (alerts[0].PlateNumber, alerts[0].Status));
        Assert.Equal(("LAG-482-KJ", ScheduleStatus.DueSoon), (alerts[1].PlateNumber, alerts[1].Status));
        Assert.Equal(("LSD-209-QM", ScheduleStatus.Upcoming), (alerts[2].PlateNumber, alerts[2].Status));
    }

    // ===================================================================== System (MainForm on an STA thread)

    [Fact]
    public void MainForm_ShowsSeededFleetAndColourCodedAlertsOnStartup()
    {
        RunSta(() =>
        {
            var fixture = new Fixture(seedSampleData: true);
            using MainForm form = fixture.CreateForm();

            Assert.Equal(3, Find<DataGridView>(form, "gridVehicles").Rows.Count);

            DataGridView alerts = Find<DataGridView>(form, "gridAlerts");
            Assert.Equal(3, alerts.Rows.Count);
            Assert.Equal("Overdue", alerts.Rows[0].Cells[0].Value?.ToString());
            Assert.Equal("Due Soon", alerts.Rows[1].Cells[0].Value?.ToString());
            Assert.Equal("Upcoming", alerts.Rows[2].Cells[0].Value?.ToString());
            Assert.NotEqual(alerts.Rows[0].DefaultCellStyle.BackColor, alerts.Rows[2].DefaultCellStyle.BackColor);
        });
    }

    [Fact]
    public void MainForm_RegisteringAVehicleAddsARowAndSavesToTheStore()
    {
        RunSta(() =>
        {
            var fixture = new Fixture(seedSampleData: true);
            using MainForm form = fixture.CreateForm();

            Find<TextBox>(form, "txtPlate").Text = "NEW-001-AA";
            Find<TextBox>(form, "txtMake").Text = "Honda";
            Find<TextBox>(form, "txtModel").Text = "Civic";
            Find<TextBox>(form, "txtYear").Text = "2022";
            Find<TextBox>(form, "txtOdometer").Text = "12,500";
            Find<TextBox>(form, "txtOwner").Text = "Fleet Office";

            Assert.True(form.TryRegisterVehicle());

            Assert.Equal(4, Find<DataGridView>(form, "gridVehicles").Rows.Count);
            Assert.Equal(1, fixture.Store.SaveCount);
            Assert.Contains(fixture.Vehicles.GetAllVehicles(), v => v.PlateNumber == "NEW-001-AA");
        });
    }

    [Fact]
    public void MainForm_InvalidYearShowsStatusErrorAndChangesNothing()
    {
        RunSta(() =>
        {
            var fixture = new Fixture(seedSampleData: true);
            using MainForm form = fixture.CreateForm();

            Find<TextBox>(form, "txtPlate").Text = "BAD-001-AA";
            Find<TextBox>(form, "txtMake").Text = "Honda";
            Find<TextBox>(form, "txtModel").Text = "Civic";
            Find<TextBox>(form, "txtYear").Text = "abcd";
            Find<TextBox>(form, "txtOdometer").Text = "1000";
            Find<TextBox>(form, "txtOwner").Text = "Fleet Office";

            Assert.False(form.TryRegisterVehicle());

            Assert.Contains("year", form.StatusMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(3, Find<DataGridView>(form, "gridVehicles").Rows.Count);
            Assert.Equal(0, fixture.Store.SaveCount);
        });
    }

    [Fact]
    public void MainForm_LivePreviewShowsDiscountAndSevenPointFivePercentVat()
    {
        RunSta(() =>
        {
            var fixture = new Fixture(seedSampleData: false);
            using MainForm form = fixture.CreateForm();

            Find<TextBox>(form, "txtParts").Text = "65000";
            Find<TextBox>(form, "txtLabor").Text = "20000";
            Find<TextBox>(form, "txtDiscount").Text = "5";
            Find<CheckBox>(form, "chkVat").Checked = true;

            string preview = Find<Label>(form, "lblPreview").Text;
            Assert.Contains("4,250.00", preview);
            Assert.Contains("6,056.25", preview);
            Assert.Contains("86,806.25", preview);
        });
    }

    // ===================================================================== Acceptance (full user journey)

    [Fact]
    public void Acceptance_ManagerRecordsServiceAndSeesCostsHistoryAndAlertsUpdate()
    {
        RunSta(() =>
        {
            var fixture = new Fixture(seedSampleData: true);
            using MainForm form = fixture.CreateForm();

            // Vehicle 0 is the overdue HiAce (plates are sorted alphabetically).
            Find<ComboBox>(form, "cboServiceVehicle").SelectedIndex = 0;
            Find<TextBox>(form, "txtServiceOdometer").Text = "89,000";
            Find<ComboBox>(form, "cboCategory").SelectedIndex = 1;
            Find<TextBox>(form, "txtDescription").Text = "Rotor resurfacing";
            Find<TextBox>(form, "txtParts").Text = "10000";
            Find<TextBox>(form, "txtLabor").Text = "5000";
            Find<TextBox>(form, "txtDiscount").Text = "0";

            Assert.True(form.TryRecordService());

            Assert.Equal(4, Find<DataGridView>(form, "gridHistory").Rows.Count);
            Assert.Equal("4", Find<Label>(form, "kpiServices").Text);
            Assert.Equal("₦274,681.25", Find<Label>(form, "kpiTotalSpend").Text);

            string?[] statuses = Find<DataGridView>(form, "gridAlerts").Rows
                .Cast<DataGridViewRow>()
                .Select(r => r.Cells[0].Value?.ToString())
                .ToArray();
            Assert.DoesNotContain("Overdue", statuses);
            Assert.True(fixture.Store.SaveCount >= 1);
        });
    }

    [Fact]
    public void RecordService_BackdatedEntryBelowRegistrationMileageIsRejected()
    {
        var fixture = new Fixture(seedSampleData: false);
        Vehicle vehicle = fixture.Vehicles.RegisterVehicle(
            VehicleType.Passenger, "REG-100-AA", "Toyota", "Corolla", 2021, 1_000, "Fleet Office", Today.AddDays(-100));

        fixture.Maintenance.RecordService(
            vehicle.Id, Today, 1_200, ServiceCategory.RoutineOilChange,
            "Latest service", 5_000m, 2_000m, today: Today);

        Assert.Throws<ArgumentException>(() => fixture.Maintenance.RecordService(
            vehicle.Id, Today.AddDays(-6), 500, ServiceCategory.TireRotation,
            "Below registration mileage", 1_000m, 1_000m, today: Today));
    }

    [Fact]
    public void MainForm_OverflowingCostsDoNotCrashPreviewOrRecordService()
    {
        RunSta(() =>
        {
            var fixture = new Fixture(seedSampleData: true);
            using MainForm form = fixture.CreateForm();

            Find<ComboBox>(form, "cboServiceVehicle").SelectedIndex = 0;
            Find<TextBox>(form, "txtServiceOdometer").Text = "89,000";
            Find<TextBox>(form, "txtDescription").Text = "Overflow check";
            Find<TextBox>(form, "txtParts").Text = decimal.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Find<TextBox>(form, "txtLabor").Text = "100";

            Assert.Contains("too large", Find<Label>(form, "lblPreview").Text, StringComparison.OrdinalIgnoreCase);
            Assert.False(form.TryRecordService());
            Assert.Contains("too large", form.StatusMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, fixture.Store.SaveCount);
        });
    }
}
