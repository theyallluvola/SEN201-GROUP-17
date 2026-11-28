using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using AutoLedgerSimple.Data;
using AutoLedgerSimple.Models;
using AutoLedgerSimple.Services;

namespace AutoLedgerSimple.UI;

/// <summary>
/// Four-view desktop front end: Fleet Vehicles, Servicing History, Maintenance Costs
/// and Service Alerts. Built in 100% pure C# WinForms (no designer file or third-party
/// UI libraries) using the AutoLedger Carbon Slate and Forged Copper palette.
/// All feedback goes to the status bar rather than modal dialogs.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // AutoLedger brand palette (Carbon Slate / Forged Copper / Signal Amber)
    private static readonly Color SidebarBg = Color.FromArgb(15, 34, 51);          // #0F2233
    private static readonly Color SidebarSurface = Color.FromArgb(24, 48, 70);     // #183046
    private static readonly Color SidebarActiveBg = Color.FromArgb(27, 53, 77);    // #1B354D
    private static readonly Color SidebarHoverBg = Color.FromArgb(22, 45, 66);     // #162D42
    private static readonly Color SidebarDivider = Color.FromArgb(32, 60, 86);     // #203C56
    private static readonly Color SidebarText = Color.FromArgb(203, 213, 225);     // #CBD5E1
    private static readonly Color SidebarMuted = Color.FromArgb(138, 159, 180);    // #8A9FB4

    private static readonly Color CopperPrimary = Color.FromArgb(200, 82, 14);     // #C8520E
    private static readonly Color CopperHover = Color.FromArgb(178, 70, 10);       // #B2460A
    private static readonly Color CopperPressed = Color.FromArgb(154, 59, 7);      // #9A3B07
    private static readonly Color SignalAmber = Color.FromArgb(242, 133, 63);      // #F2853F

    private static readonly Color CanvasBg = Color.FromArgb(244, 246, 249);        // #F4F6F9
    private static readonly Color CardBg = Color.White;                            // #FFFFFF
    private static readonly Color CardBorder = Color.FromArgb(226, 232, 240);      // #E2E8F0
    private static readonly Color SubtleSurface = Color.FromArgb(248, 250, 252);   // #F8FAFC
    private static readonly Color GridLineColor = Color.FromArgb(238, 242, 246);   // #EEF2F6
    private static readonly Color GridAltRow = Color.FromArgb(250, 251, 253);      // #FAFBFD
    private static readonly Color GridSelectBg = Color.FromArgb(235, 243, 252);    // #EBF3FC

    private static readonly Color InkPrimary = Color.FromArgb(15, 23, 42);         // #0F172A
    private static readonly Color InkSecondary = Color.FromArgb(71, 85, 105);      // #475569
    private static readonly Color InkMuted = Color.FromArgb(100, 116, 139);        // #64748B

    // Refined semantic status tints
    private static readonly Color OverdueBg = Color.FromArgb(254, 226, 226);       // #FEE2E2
    private static readonly Color OverdueFg = Color.FromArgb(153, 27, 27);         // #991B1B
    private static readonly Color OverdueSelectBg = Color.FromArgb(252, 206, 206);

    private static readonly Color DueSoonBg = Color.FromArgb(254, 243, 199);       // #FEF3C7
    private static readonly Color DueSoonFg = Color.FromArgb(146, 64, 14);         // #92400E
    private static readonly Color DueSoonSelectBg = Color.FromArgb(253, 230, 168);

    private static readonly Color UpcomingBg = Color.FromArgb(220, 252, 231);      // #DCFCE7
    private static readonly Color UpcomingFg = Color.FromArgb(22, 101, 52);        // #166534
    private static readonly Color UpcomingSelectBg = Color.FromArgb(196, 244, 213);

    private static readonly Color StatusOkFg = Color.FromArgb(21, 128, 61);        // #15803D
    private static readonly Color StatusErrFg = Color.FromArgb(185, 28, 28);       // #B91C1C

    private sealed record VehicleChoice(Guid Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record CategoryChoice(ServiceCategory Value)
    {
        public override string ToString() => UiValidationHelper.FormatCategory(Value);
    }

    private sealed record NavSection(
        string Key,
        string NavLabel,
        string Title,
        string Subtitle,
        Panel View,
        Button NavButton);

    private readonly IFleetStore _store;
    private readonly FleetDatabase _database;
    private readonly VehicleService _vehicles;
    private readonly MaintenanceService _maintenance;
    private readonly Icon? _appIcon;
    private readonly List<Font> _ownedFonts = new();
    private readonly List<NavSection> _sections = new();
    private bool _suppressOdometerPrefill;
    private string _activeSectionKey = "fleet";
    private int _overdueCount;
    private int _dueSoonCount;

    // Shared typography (disposed with form)
    private readonly Font _baseFont;
    private readonly Font _semiboldFont;
    private readonly Font _compactGridFont;
    private readonly Font _brandTitleFont;
    private readonly Font _brandSubFont;
    private readonly Font _sectionTagFont;
    private readonly Font _pageTitleFont;
    private readonly Font _pageSubFont;
    private readonly Font _cardTitleFont;
    private readonly Font _fieldLabelFont;
    private readonly Font _gridHeaderFont;
    private readonly Font _compactHeaderFont;
    private readonly Font _kpiCaptionFont;
    private readonly Font _kpiValueFont;
    private readonly Font _badgeFont;
    private readonly Font _previewFont;

    // Shell controls
    private readonly Panel _contentHost = new() { Dock = DockStyle.Fill, BackColor = CanvasBg };
    private readonly Label _pageTitle = new() { AutoSize = true, ForeColor = InkPrimary, UseMnemonic = false };
    private readonly Label _pageSubtitle = new() { AutoSize = true, ForeColor = InkMuted, UseMnemonic = false };
    private readonly Button _headerAlertPill = new()
    {
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        Padding = new Padding(10, 4, 10, 4),
        Margin = new Padding(0),
        TabStop = false
    };
    private readonly Label _sidebarMeta = new() { AutoSize = true, ForeColor = SidebarText };
    private readonly StatusStrip _statusStrip = new()
    {
        SizingGrip = false,
        BackColor = CardBg,
        Padding = new Padding(16, 4, 16, 4)
    };
    private readonly ToolStripStatusLabel _statusLabel = new()
    {
        Text = "Ready.",
        ForeColor = InkMuted
    };

    private Button? _alertsNavButton;

    // Fleet Vehicles controls
    private readonly ComboBox _vehicleType = new() { Name = "cboVehicleType", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _plate = new() { Name = "txtPlate", PlaceholderText = "e.g. KJA-312-AB" };
    private readonly TextBox _make = new() { Name = "txtMake", PlaceholderText = "e.g. Toyota" };
    private readonly TextBox _model = new() { Name = "txtModel", PlaceholderText = "e.g. Corolla" };
    private readonly TextBox _year = new() { Name = "txtYear", PlaceholderText = "e.g. 2022" };
    private readonly TextBox _odometer = new() { Name = "txtOdometer", PlaceholderText = "e.g. 42,500" };
    private readonly TextBox _owner = new() { Name = "txtOwner", PlaceholderText = "e.g. PAU Transport Unit" };
    private readonly TextBox _registeredOn = new() { Name = "txtRegisteredOn", PlaceholderText = "yyyy-MM-dd (blank = today)" };
    private readonly Button _registerButton = new() { Name = "btnRegister", Text = "Register vehicle" };
    private readonly DataGridView _vehicleGrid;

    // Servicing History controls
    private readonly ComboBox _serviceVehicle = new() { Name = "cboServiceVehicle", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _serviceDate = new() { Name = "txtServiceDate", PlaceholderText = "yyyy-MM-dd" };
    private readonly TextBox _serviceOdometer = new() { Name = "txtServiceOdometer" };
    private readonly ComboBox _category = new() { Name = "cboCategory", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _description = new() { Name = "txtDescription", PlaceholderText = "Brief description of maintenance performed" };
    private readonly TextBox _parts = new() { Name = "txtParts", PlaceholderText = "0.00" };
    private readonly TextBox _labor = new() { Name = "txtLabor", PlaceholderText = "0.00" };
    private readonly TextBox _discount = new() { Name = "txtDiscount", Text = "0" };
    private readonly CheckBox _applyVat = new() { Name = "chkVat", Text = "Apply VAT (7.5%)", AutoSize = true, ForeColor = InkPrimary };
    private readonly Label _preview = new()
    {
        Name = "lblPreview",
        AutoSize = false,
        Dock = DockStyle.Fill,
        BackColor = SubtleSurface,
        ForeColor = InkPrimary,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 4, 12, 4)
    };
    private readonly Button _recordButton = new() { Name = "btnRecord", Text = "Record service" };
    private readonly DataGridView _historyGrid;

    // Maintenance Costs controls
    private readonly Label _kpiTotalSpend = new() { Name = "kpiTotalSpend", Text = "₦0.00" };
    private readonly Label _kpiServices = new() { Name = "kpiServices", Text = "0" };
    private readonly Label _kpiAverage = new() { Name = "kpiAverage", Text = "₦0.00" };
    private readonly Label _kpiParts = new() { Name = "kpiParts", Text = "₦0.00" };
    private readonly Label _kpiLabor = new() { Name = "kpiLabor", Text = "₦0.00" };
    private readonly Label _kpiDiscount = new() { Name = "kpiDiscount", Text = "₦0.00" };
    private readonly Label _kpiVat = new() { Name = "kpiVat", Text = "₦0.00" };
    private readonly Label _kpiVehicles = new() { Name = "kpiVehicles", Text = "0" };
    private readonly DataGridView _costByVehicleGrid;
    private readonly DataGridView _costByCategoryGrid;

    // Alerts controls
    private readonly Label _alertSummary = new() { Name = "lblAlertSummary", AutoSize = true, ForeColor = InkPrimary };
    private readonly DataGridView _alertGrid;

    public MainForm(
        IFleetStore store,
        FleetDatabase database,
        VehicleService vehicleService,
        MaintenanceService maintenanceService)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _vehicles = vehicleService ?? throw new ArgumentNullException(nameof(vehicleService));
        _maintenance = maintenanceService ?? throw new ArgumentNullException(nameof(maintenanceService));

        _baseFont = Own(new Font("Segoe UI", 9.25F, FontStyle.Regular));
        _semiboldFont = Own(new Font("Segoe UI Semibold", 9.25F, FontStyle.Regular));
        _compactGridFont = Own(new Font("Segoe UI", 8.5F, FontStyle.Regular));
        _brandTitleFont = Own(new Font("Segoe UI Semibold", 13F, FontStyle.Regular));
        _brandSubFont = Own(new Font("Segoe UI", 8.25F, FontStyle.Regular));
        _sectionTagFont = Own(new Font("Segoe UI Semibold", 7.5F, FontStyle.Regular));
        _pageTitleFont = Own(new Font("Segoe UI Semibold", 14.5F, FontStyle.Regular));
        _pageSubFont = Own(new Font("Segoe UI", 9F, FontStyle.Regular));
        _cardTitleFont = Own(new Font("Segoe UI Semibold", 10F, FontStyle.Regular));
        _fieldLabelFont = Own(new Font("Segoe UI Semibold", 8.75F, FontStyle.Regular));
        _gridHeaderFont = Own(new Font("Segoe UI Semibold", 8.5F, FontStyle.Regular));
        _compactHeaderFont = Own(new Font("Segoe UI Semibold", 8.25F, FontStyle.Regular));
        _kpiCaptionFont = Own(new Font("Segoe UI Semibold", 8.25F, FontStyle.Regular));
        _kpiValueFont = Own(new Font("Segoe UI Semibold", 14.5F, FontStyle.Regular));
        _badgeFont = Own(new Font("Segoe UI Semibold", 8F, FontStyle.Regular));
        _previewFont = Own(new Font("Segoe UI Semibold", 9F, FontStyle.Regular));

        _vehicleGrid = CreateGrid(
            "gridVehicles",
            new[] { "Plate", "Type", "Vehicle", "Year", "Odometer (km)", "Owner", "Registered", "Next service" },
            new[] { 3, 4 },
            fillWeights: new[] { 96f, 88f, 145f, 56f, 96f, 152f, 92f, 95f });

        _historyGrid = CreateGrid(
            "gridHistory",
            new[] { "Date", "Plate", "Category", "Description", "Km", "Parts", "Labor", "Discount", "VAT", "Total" },
            new[] { 4, 5, 6, 7, 8, 9 },
            fillWeights: new[] { 86f, 96f, 130f, 178f, 64f, 88f, 82f, 74f, 80f, 92f },
            compact: true);

        _costByVehicleGrid = CreateGrid(
            "gridCostByVehicle",
            new[] { "Plate", "Vehicle", "Type", "Services", "Parts", "Labor", "Discount", "VAT", "Total" },
            new[] { 3, 4, 5, 6, 7, 8 },
            fillWeights: new[] { 96f, 165f, 92f, 68f, 96f, 88f, 84f, 86f, 100f });

        _costByCategoryGrid = CreateGrid(
            "gridCostByCategory",
            new[] { "Category", "Services", "Total" },
            new[] { 1, 2 },
            fillWeights: new[] { 220f, 90f, 140f });

        _alertGrid = CreateGrid(
            "gridAlerts",
            new[] { "Status", "Plate", "Vehicle", "Due date", "Time left", "Due at (km)", "Current (km)", "Distance left" },
            new[] { 5, 6 },
            fillWeights: new[] { 86f, 94f, 158f, 92f, 116f, 88f, 88f, 98f });

        Text = "AutoLedger — Vehicle Maintenance Management";
        _appIcon = LoadAppIcon();
        if (_appIcon is not null)
            Icon = _appIcon;

        Font = _baseFont;
        BackColor = CanvasBg;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 780);
        MinimumSize = new Size(1080, 660);

        StylePrimaryButton(_registerButton);
        StylePrimaryButton(_recordButton);
        StyleInput(_vehicleType);
        StyleInput(_plate);
        StyleInput(_make);
        StyleInput(_model);
        StyleInput(_year);
        StyleInput(_odometer);
        StyleInput(_owner);
        StyleInput(_registeredOn);
        StyleInput(_serviceVehicle);
        StyleInput(_serviceDate);
        StyleInput(_serviceOdometer);
        StyleInput(_category);
        StyleInput(_description);
        StyleInput(_parts);
        StyleInput(_labor);
        StyleInput(_discount);
        _preview.Font = _previewFont;

        _serviceDate.Text = Today.ToString("yyyy-MM-dd", Inv);

        Panel fleetView = BuildFleetView();
        Panel serviceView = BuildServiceView();
        Panel costsView = BuildCostsView();
        Panel alertsView = BuildAlertsView();

        Panel sidebar = BuildSidebar(fleetView, serviceView, costsView, alertsView);
        Panel workspace = BuildWorkspace(fleetView, serviceView, costsView, alertsView);

        Controls.Add(workspace);
        Controls.Add(sidebar);

        ActivateSection("fleet");

        RefreshAll();
        PrefillOdometer();
        UpdatePreview();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>The text currently shown in the status bar.</summary>
    public string StatusMessage => _statusLabel.Text ?? string.Empty;

    // ------------------------------------------------------------------ actions

    public bool TryRegisterVehicle()
    {
        if (_vehicleType.SelectedItem is not VehicleType type)
            return Fail("Choose a vehicle type.");

        if (!UiValidationHelper.TryParseInt(_year.Text, out int year))
            return Fail("Year must be a whole number.");

        if (!UiValidationHelper.TryParseInt(_odometer.Text, out int odometerKm))
            return Fail("Odometer must be a whole number of kilometres.");

        DateOnly registeredOn = Today;
        if (!string.IsNullOrWhiteSpace(_registeredOn.Text)
            && !UiValidationHelper.TryParseDateOnly(_registeredOn.Text, out registeredOn))
            return Fail("Registered date must look like 2026-11-26 or 26/11/2026.");

        string snapshot = JsonFleetStore.SerializeSnapshot(_database);
        Vehicle vehicle;
        try
        {
            vehicle = _vehicles.RegisterVehicle(
                type, _plate.Text, _make.Text, _model.Text, year, odometerKm, _owner.Text, registeredOn);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Fail(CleanMessage(ex.Message));
        }

        if (!TrySave())
        {
            string enteredOdometer = _serviceOdometer.Text;
            JsonFleetStore.RestoreSnapshot(_database, snapshot);
            RefreshAll();
            _serviceOdometer.Text = enteredOdometer;
            return false;
        }

        _plate.Clear();
        _make.Clear();
        _model.Clear();
        _year.Clear();
        _odometer.Clear();
        _owner.Clear();
        _registeredOn.Clear();

        RefreshAll();
        SetStatus($"Registered {vehicle.GetDisplayName()}.", isError: false);

        return true;
    }

    public bool TryRecordService()
    {
        if (_serviceVehicle.SelectedItem is not VehicleChoice vehicle)
            return Fail("Select a vehicle first.");

        if (_category.SelectedItem is not CategoryChoice category)
            return Fail("Select a service category.");

        if (!UiValidationHelper.TryParseDateOnly(_serviceDate.Text, out DateOnly serviceDate))
            return Fail("Service date must look like 2026-11-26 or 26/11/2026.");

        if (!UiValidationHelper.TryParseInt(_serviceOdometer.Text, out int odometerKm))
            return Fail("Odometer must be a whole number of kilometres.");

        if (string.IsNullOrWhiteSpace(_description.Text))
            return Fail("Enter a short service description.");

        if (!TryReadAmount(_parts, out decimal parts))
            return Fail("Parts cost must be a valid amount.");

        if (!TryReadAmount(_labor, out decimal labor))
            return Fail("Labor cost must be a valid amount.");

        if (!TryReadAmount(_discount, out decimal discount) || discount < 0m || discount > 100m)
            return Fail("Discount must be a number between 0 and 100.");

        string snapshot = JsonFleetStore.SerializeSnapshot(_database);
        ServiceRecord record;
        try
        {
            record = _maintenance.RecordService(
                vehicle.Id,
                serviceDate,
                odometerKm,
                category.Value,
                _description.Text,
                parts,
                labor,
                discount,
                _applyVat.Checked);
        }
        catch (OverflowException)
        {
            return Fail("Entered costs are too large to calculate.");
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or InvalidOperationException)
        {
            return Fail(CleanMessage(ex.Message));
        }

        if (!TrySave())
        {
            string enteredOdometer = _serviceOdometer.Text;
            JsonFleetStore.RestoreSnapshot(_database, snapshot);
            RefreshAll();
            _serviceOdometer.Text = enteredOdometer;
            return false;
        }

        _description.Clear();
        _parts.Clear();
        _labor.Clear();
        _discount.Text = "0";

        RefreshAll();
        PrefillOdometer();
        UpdatePreview();

        SetStatus($"Service recorded for {vehicle.Label}. Total {UiValidationHelper.FormatNaira(record.TotalCost)}.", isError: false);

        return true;
    }

    public void RefreshAll()
    {
        RefreshFleet();
        RefreshServiceVehicleChoices();
        RefreshHistory();
        RefreshCosts();
        RefreshAlerts();
        RefreshChromeMeta();
    }

    // ------------------------------------------------------------------ refresh

    private void RefreshFleet()
    {
        _vehicleGrid.Rows.Clear();
        DateOnly today = Today;

        foreach (Vehicle v in _vehicles.GetAllVehicles())
        {
            ScheduleStatus? status = _maintenance.GetVehicleScheduleStatus(v.Id, today);

            int row = _vehicleGrid.Rows.Add(
                v.PlateNumber,
                v.Type.ToString(),
                $"{v.Make} {v.Model}",
                v.Year.ToString(Inv),
                v.CurrentOdometerKm.ToString("N0", Inv),
                v.OwnerName,
                v.RegisteredOn.ToString("yyyy-MM-dd", Inv),
                status.HasValue ? MaintenanceSchedule.FormatStatus(status.Value) : "-");

            if (status.HasValue)
            {
                DataGridViewCellStyle statusStyle = _vehicleGrid.Rows[row].Cells[7].Style;
                ApplyStatusCellStyle(statusStyle, status.Value);
            }
        }

        _vehicleGrid.ClearSelection();
    }

    private void RefreshServiceVehicleChoices()
    {
        Guid? previous = (_serviceVehicle.SelectedItem as VehicleChoice)?.Id;

        _suppressOdometerPrefill = true;
        try
        {
            _serviceVehicle.Items.Clear();
            foreach (Vehicle v in _vehicles.GetAllVehicles())
                _serviceVehicle.Items.Add(new VehicleChoice(v.Id, v.GetDisplayName()));

            int selectedIndex = _serviceVehicle.Items.Count > 0 ? 0 : -1;
            for (int i = 0; i < _serviceVehicle.Items.Count; i++)
            {
                if (_serviceVehicle.Items[i] is VehicleChoice choice && choice.Id == previous)
                {
                    selectedIndex = i;
                    break;
                }
            }

            _serviceVehicle.SelectedIndex = selectedIndex;
        }
        finally
        {
            _suppressOdometerPrefill = false;
        }
    }

    private void RefreshHistory()
    {
        _historyGrid.Rows.Clear();

        foreach (ServiceRecord r in _maintenance.GetServiceHistory())
        {
            string plate = _vehicles.FindById(r.VehicleId)?.PlateNumber ?? "?";

            _historyGrid.Rows.Add(
                r.ServiceDate.ToString("yyyy-MM-dd", Inv),
                plate,
                UiValidationHelper.FormatCategory(r.Category),
                r.Description,
                r.OdometerAtServiceKm.ToString("N0", Inv),
                UiValidationHelper.FormatNaira(r.PartsCost),
                UiValidationHelper.FormatNaira(r.LaborCost),
                UiValidationHelper.FormatNaira(r.DiscountAmount),
                UiValidationHelper.FormatNaira(r.VatAmount),
                UiValidationHelper.FormatNaira(r.TotalCost));
        }

        _historyGrid.ClearSelection();
    }

    private void RefreshCosts()
    {
        FleetCostSummary summary = _maintenance.GetCostSummary();

        _kpiTotalSpend.Text = UiValidationHelper.FormatNaira(summary.TotalSpend);
        _kpiServices.Text = summary.TotalServices.ToString(Inv);
        _kpiAverage.Text = UiValidationHelper.FormatNaira(summary.AverageCostPerService);
        _kpiParts.Text = UiValidationHelper.FormatNaira(summary.TotalPartsCost);
        _kpiLabor.Text = UiValidationHelper.FormatNaira(summary.TotalLaborCost);
        _kpiDiscount.Text = UiValidationHelper.FormatNaira(summary.TotalDiscountAmount);
        _kpiVat.Text = UiValidationHelper.FormatNaira(summary.TotalVatAmount);
        _kpiVehicles.Text = summary.TotalVehicles.ToString(Inv);

        _costByVehicleGrid.Rows.Clear();
        foreach (VehicleCostSummary v in summary.ByVehicle)
        {
            _costByVehicleGrid.Rows.Add(
                v.PlateNumber,
                v.VehicleLabel,
                v.Type.ToString(),
                v.ServiceCount.ToString(Inv),
                UiValidationHelper.FormatNaira(v.TotalPartsCost),
                UiValidationHelper.FormatNaira(v.TotalLaborCost),
                UiValidationHelper.FormatNaira(v.TotalDiscountAmount),
                UiValidationHelper.FormatNaira(v.TotalVatAmount),
                UiValidationHelper.FormatNaira(v.TotalSpend));
        }
        _costByVehicleGrid.ClearSelection();

        _costByCategoryGrid.Rows.Clear();
        foreach (CategoryCostSummary c in summary.ByCategory)
        {
            _costByCategoryGrid.Rows.Add(
                UiValidationHelper.FormatCategory(c.Category),
                c.ServiceCount.ToString(Inv),
                UiValidationHelper.FormatNaira(c.TotalSpend));
        }
        _costByCategoryGrid.ClearSelection();
    }

    private void RefreshAlerts()
    {
        _alertGrid.Rows.Clear();
        List<ServiceAlertItem> alerts = _maintenance.GetServiceAlerts(Today);

        foreach (ServiceAlertItem a in alerts)
        {
            int row = _alertGrid.Rows.Add(
                MaintenanceSchedule.FormatStatus(a.Status),
                a.PlateNumber,
                a.VehicleLabel,
                a.DueDate.ToString("yyyy-MM-dd", Inv),
                DescribeDays(a.DaysRemaining),
                a.DueOdometerKm.ToString("N0", Inv),
                a.CurrentOdometerKm.ToString("N0", Inv),
                DescribeKm(a.KmRemaining));

            ApplyStatusCellStyle(_alertGrid.Rows[row].DefaultCellStyle, a.Status, boldText: false);
            _alertGrid.Rows[row].Cells[0].Style.Font = _semiboldFont;
        }

        _alertGrid.ClearSelection();

        _overdueCount = alerts.Count(a => a.Status == ScheduleStatus.Overdue);
        _dueSoonCount = alerts.Count(a => a.Status == ScheduleStatus.DueSoon);
        int upcoming = alerts.Count(a => a.Status == ScheduleStatus.Upcoming);
        _alertSummary.Text = $"{_overdueCount} overdue   ·   {_dueSoonCount} due soon   ·   {upcoming} upcoming";
    }

    private void RefreshChromeMeta()
    {
        int vehicleCount = _database.Vehicles.Count;
        int serviceCount = _database.ServiceRecords.Count;
        string vWord = vehicleCount == 1 ? "vehicle" : "vehicles";
        string sWord = serviceCount == 1 ? "service" : "services";
        _sidebarMeta.Text = $"{vehicleCount} {vWord} · {serviceCount} {sWord}";

        if (_overdueCount > 0)
        {
            _headerAlertPill.Text = _dueSoonCount > 0
                ? $"●  {_overdueCount} overdue · {_dueSoonCount} due soon"
                : $"●  {_overdueCount} overdue";
            _headerAlertPill.BackColor = OverdueBg;
            _headerAlertPill.ForeColor = OverdueFg;
            _headerAlertPill.FlatAppearance.MouseOverBackColor = OverdueSelectBg;
        }
        else if (_dueSoonCount > 0)
        {
            _headerAlertPill.Text = $"●  {_dueSoonCount} due soon";
            _headerAlertPill.BackColor = DueSoonBg;
            _headerAlertPill.ForeColor = DueSoonFg;
            _headerAlertPill.FlatAppearance.MouseOverBackColor = DueSoonSelectBg;
        }
        else
        {
            _headerAlertPill.Text = "●  All schedules on track";
            _headerAlertPill.BackColor = UpcomingBg;
            _headerAlertPill.ForeColor = UpcomingFg;
            _headerAlertPill.FlatAppearance.MouseOverBackColor = UpcomingSelectBg;
        }

        _alertsNavButton?.Invalidate();
    }

    // ------------------------------------------------------------------ live helpers

    private void PrefillOdometer()
    {
        if (_serviceVehicle.SelectedItem is not VehicleChoice choice)
            return;

        Vehicle? vehicle = _vehicles.FindById(choice.Id);
        if (vehicle is not null)
            _serviceOdometer.Text = vehicle.CurrentOdometerKm.ToString(Inv);
    }

    private void UpdatePreview()
    {
        if (!TryReadAmount(_parts, out decimal parts)
            || !TryReadAmount(_labor, out decimal labor)
            || !TryReadAmount(_discount, out decimal discount)
            || parts < 0m || labor < 0m || discount < 0m || discount > 100m)
        {
            _preview.Text = "Enter valid amounts (discount between 0 and 100%) to see the cost preview.";
            return;
        }

        ServiceRecord sample;
        try
        {
            sample = new ServiceRecord(
                Guid.Empty, Guid.Empty, Today, 0, ServiceCategory.RoutineOilChange,
                string.Empty, parts, labor, discount, _applyVat.Checked);
        }
        catch (OverflowException)
        {
            _preview.Text = "Entered amounts are too large to calculate.";
            return;
        }

        string vatLabel = _applyVat.Checked ? "VAT (7.5%)" : "VAT (not applied)";
        string discountText = discount.ToString("0.##", Inv);

        _preview.Text =
            $"Subtotal {UiValidationHelper.FormatNaira(sample.SubTotal)}   ·   " +
            $"Discount ({discountText}%) -{UiValidationHelper.FormatNaira(sample.DiscountAmount)}{Environment.NewLine}" +
            $"{vatLabel} {UiValidationHelper.FormatNaira(sample.VatAmount)}   ·   " +
            $"Total payable {UiValidationHelper.FormatNaira(sample.TotalCost)}";
    }

    private static bool TryReadAmount(TextBox box, out decimal amount)
    {
        if (string.IsNullOrWhiteSpace(box.Text))
        {
            amount = 0m;
            return true;
        }

        return UiValidationHelper.TryParseDecimal(box.Text, out amount);
    }

    private bool TrySave()
    {
        try
        {
            _store.Save(_database);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Could not save changes to disk: {ex.Message}", isError: true);
            return false;
        }
    }

    private bool Fail(string message)
    {
        SetStatus(message, isError: true);
        return false;
    }

    private void SetStatus(string message, bool isError)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = isError ? StatusErrFg : StatusOkFg;
    }

    private static string CleanMessage(string message)
    {
        int cut = message.IndexOf(" (Parameter", StringComparison.Ordinal);
        return cut > 0 ? message[..cut] : message;
    }

    private static string DescribeDays(int daysRemaining)
        => daysRemaining >= 0 ? $"{daysRemaining} days" : $"{-daysRemaining} days overdue";

    private static string DescribeKm(int kmRemaining)
        => kmRemaining >= 0
            ? $"{kmRemaining.ToString("N0", Inv)} km"
            : $"{(-kmRemaining).ToString("N0", Inv)} km over";

    private void ApplyStatusCellStyle(DataGridViewCellStyle style, ScheduleStatus status, bool boldText = true)
    {
        switch (status)
        {
            case ScheduleStatus.Overdue:
                style.BackColor = OverdueBg;
                style.ForeColor = OverdueFg;
                style.SelectionBackColor = OverdueSelectBg;
                style.SelectionForeColor = OverdueFg;
                break;
            case ScheduleStatus.DueSoon:
                style.BackColor = DueSoonBg;
                style.ForeColor = DueSoonFg;
                style.SelectionBackColor = DueSoonSelectBg;
                style.SelectionForeColor = DueSoonFg;
                break;
            case ScheduleStatus.Upcoming:
                style.BackColor = UpcomingBg;
                style.ForeColor = UpcomingFg;
                style.SelectionBackColor = UpcomingSelectBg;
                style.SelectionForeColor = UpcomingFg;
                break;
        }

        if (boldText)
            style.Font = _semiboldFont;
    }

    // ------------------------------------------------------------------ shell layout

    private Panel BuildSidebar(Panel fleetView, Panel serviceView, Panel costsView, Panel alertsView)
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 228,
            BackColor = SidebarBg
        };

        // Brand header with custom vector Ledger Apex emblem
        var brandPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = SidebarBg
        };
        brandPanel.Paint += (_, e) =>
        {
            PaintBrandMark(e.Graphics, new Rectangle(20, 22, 36, 36));
            using var dividerPen = new Pen(SidebarDivider);
            e.Graphics.DrawLine(dividerPen, 20, brandPanel.Height - 1, brandPanel.Width - 20, brandPanel.Height - 1);
        };

        var brandTitle = new Label
        {
            Text = "AutoLedger",
            Font = _brandTitleFont,
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(66, 20)
        };
        var brandSub = new Label
        {
            Text = "Fleet Maintenance",
            Font = _brandSubFont,
            ForeColor = SidebarMuted,
            AutoSize = true,
            Location = new Point(67, 43)
        };
        brandPanel.Controls.Add(brandTitle);
        brandPanel.Controls.Add(brandSub);

        // Navigation section
        var navHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SidebarBg,
            Padding = new Padding(0, 14, 0, 12)
        };

        var sectionLabel = new Label
        {
            Text = "WORKSPACE",
            Font = _sectionTagFont,
            ForeColor = SidebarMuted,
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(22, 6, 0, 0)
        };

        Button btnFleet = CreateNavButton("fleet", "Fleet Vehicles");
        Button btnService = CreateNavButton("service", "Servicing History");
        Button btnCosts = CreateNavButton("costs", "Maintenance Costs");
        Button btnAlerts = CreateNavButton("alerts", "Service Alerts", showAlertBadge: true);
        _alertsNavButton = btnAlerts;

        _sections.Add(new NavSection(
            "fleet",
            "Fleet Vehicles",
            "Fleet Vehicles",
            "Register vehicles and track current odometer readings and next service status.",
            fleetView,
            btnFleet));

        _sections.Add(new NavSection(
            "service",
            "Servicing History",
            "Servicing History",
            "Log completed maintenance with automatic discount and 7.5% VAT calculation.",
            serviceView,
            btnService));

        _sections.Add(new NavSection(
            "costs",
            "Maintenance Costs",
            "Maintenance Costs",
            "Monitor total fleet expenditure across vehicles and service categories.",
            costsView,
            btnCosts));

        _sections.Add(new NavSection(
            "alerts",
            "Service Alerts",
            "Upcoming & Overdue Alerts",
            "Prioritised schedule alerts sorted by urgency across date and mileage thresholds.",
            alertsView,
            btnAlerts));

        // Add in reverse order for DockStyle.Top stacking
        navHost.Controls.Add(btnAlerts);
        navHost.Controls.Add(btnCosts);
        navHost.Controls.Add(btnService);
        navHost.Controls.Add(btnFleet);
        navHost.Controls.Add(sectionLabel);

        // Footer meta block
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 66,
            BackColor = SidebarBg,
            Padding = new Padding(20, 14, 16, 12)
        };
        footer.Paint += (_, e) =>
        {
            using var dividerPen = new Pen(SidebarDivider);
            e.Graphics.DrawLine(dividerPen, 20, 0, footer.Width - 20, 0);
        };

        _sidebarMeta.Font = _fieldLabelFont;
        _sidebarMeta.Location = new Point(20, 14);

        var storageNote = new Label
        {
            Text = "Local JSON · SEN 201 Group 17",
            Font = _brandSubFont,
            ForeColor = SidebarMuted,
            AutoSize = true,
            Location = new Point(20, 35)
        };
        footer.Controls.Add(_sidebarMeta);
        footer.Controls.Add(storageNote);

        sidebar.Controls.Add(navHost);
        sidebar.Controls.Add(brandPanel);
        sidebar.Controls.Add(footer);
        return sidebar;
    }

    private Panel BuildWorkspace(Panel fleetView, Panel serviceView, Panel costsView, Panel alertsView)
    {
        var workspace = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CanvasBg
        };

        // Crisp page header bar
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
            BackColor = CardBg,
            Padding = new Padding(24, 12, 24, 12)
        };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(CardBorder);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = CardBg
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var titleStack = new Panel { Dock = DockStyle.Fill, BackColor = CardBg };
        _pageTitle.Font = _pageTitleFont;
        _pageTitle.Location = new Point(0, 0);
        _pageSubtitle.Font = _pageSubFont;
        _pageSubtitle.Location = new Point(1, 26);
        titleStack.Controls.Add(_pageTitle);
        titleStack.Controls.Add(_pageSubtitle);

        _headerAlertPill.Font = _badgeFont;
        _headerAlertPill.FlatAppearance.BorderSize = 0;
        _headerAlertPill.Anchor = AnchorStyles.Right;
        _headerAlertPill.Click += (_, _) => ActivateSection("alerts");

        headerLayout.Controls.Add(titleStack, 0, 0);
        headerLayout.Controls.Add(_headerAlertPill, 1, 0);
        header.Controls.Add(headerLayout);

        // Add all views to the content host so Controls.Find(name, true) always locates every control
        _contentHost.Controls.Add(fleetView);
        _contentHost.Controls.Add(serviceView);
        _contentHost.Controls.Add(costsView);
        _contentHost.Controls.Add(alertsView);

        // Clean status footer
        _statusStrip.Paint += (_, e) =>
        {
            using var pen = new Pen(CardBorder);
            e.Graphics.DrawLine(pen, 0, 0, _statusStrip.Width, 0);
        };
        _statusStrip.Items.Add(_statusLabel);

        workspace.Controls.Add(_contentHost);
        workspace.Controls.Add(header);
        workspace.Controls.Add(_statusStrip);
        return workspace;
    }

    private Button CreateNavButton(string key, string text, bool showAlertBadge = false)
    {
        var button = new Button
        {
            Name = $"nav_{key}",
            Text = text,
            Dock = DockStyle.Top,
            Height = 44,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(22, 0, 14, 0),
            BackColor = SidebarBg,
            ForeColor = SidebarText,
            Font = _baseFont,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = SidebarHoverBg;
        button.FlatAppearance.MouseDownBackColor = SidebarActiveBg;

        button.Click += (_, _) => ActivateSection(key);
        button.Paint += (_, e) =>
        {
            bool isActive = string.Equals(_activeSectionKey, key, StringComparison.Ordinal);
            if (isActive)
            {
                using var accent = new SolidBrush(SignalAmber);
                e.Graphics.FillRectangle(accent, 0, 6, 4, button.Height - 12);
            }

            if (showAlertBadge && (_overdueCount > 0 || _dueSoonCount > 0))
            {
                int count = _overdueCount > 0 ? _overdueCount : _dueSoonCount;
                Color pillBg = _overdueCount > 0 ? Color.FromArgb(220, 38, 38) : CopperPrimary;
                string badgeText = count.ToString(Inv);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Size textSize = TextRenderer.MeasureText(badgeText, _badgeFont);
                int pillWidth = Math.Max(22, textSize.Width + 8);
                int pillHeight = 20;
                var pillRect = new Rectangle(
                    button.Width - pillWidth - 16,
                    (button.Height - pillHeight) / 2,
                    pillWidth,
                    pillHeight);

                using GraphicsPath path = CreateRoundedRect(pillRect, 10);
                using var brush = new SolidBrush(pillBg);
                e.Graphics.FillPath(brush, path);

                TextRenderer.DrawText(
                    e.Graphics,
                    badgeText,
                    _badgeFont,
                    pillRect,
                    Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        };

        return button;
    }

    private void ActivateSection(string key)
    {
        _activeSectionKey = key;

        foreach (NavSection section in _sections)
        {
            bool active = string.Equals(section.Key, key, StringComparison.Ordinal);
            section.View.Visible = active;
            if (active)
            {
                section.View.BringToFront();
                _pageTitle.Text = section.Title;
                _pageSubtitle.Text = section.Subtitle;
            }

            section.NavButton.BackColor = active ? SidebarActiveBg : SidebarBg;
            section.NavButton.ForeColor = active ? Color.White : SidebarText;
            section.NavButton.Font = active ? _semiboldFont : _baseFont;
            section.NavButton.FlatAppearance.MouseOverBackColor = active ? SidebarActiveBg : SidebarHoverBg;
            section.NavButton.Invalidate();
        }
    }

    // ------------------------------------------------------------------ views

    private Panel BuildFleetView()
    {
        var view = CreateViewContainer("tabFleet");

        foreach (VehicleType type in Enum.GetValues<VehicleType>())
            _vehicleType.Items.Add(type);
        _vehicleType.SelectedIndex = 0;

        TableLayoutPanel form = NewFormTable(5);
        AddField(form, "Vehicle type", _vehicleType, 0, 0);
        AddField(form, "Plate number", _plate, 0, 2);
        AddField(form, "Make", _make, 1, 0);
        AddField(form, "Model", _model, 1, 2);
        AddField(form, "Year", _year, 2, 0);
        AddField(form, "Odometer (km)", _odometer, 2, 2);
        AddField(form, "Owner / department", _owner, 3, 0);
        AddField(form, "Registered on", _registeredOn, 3, 2);

        _registerButton.Anchor = AnchorStyles.Right;
        _registerButton.Margin = new Padding(0, 8, 16, 0);
        form.Controls.Add(_registerButton, 3, 4);

        _registerButton.Click += (_, _) => TryRegisterVehicle();

        Panel formCard = CreateSectionCard("Register New Vehicle", form, DockStyle.Top);
        Panel spacer = CreateVerticalSpacer(14);
        Panel gridCard = CreateSectionCard("Registered Fleet", _vehicleGrid, DockStyle.Fill);

        view.Controls.Add(gridCard);
        view.Controls.Add(spacer);
        view.Controls.Add(formCard);
        return view;
    }

    private Panel BuildServiceView()
    {
        var view = CreateViewContainer("tabService");

        foreach (ServiceCategory category in Enum.GetValues<ServiceCategory>())
            _category.Items.Add(new CategoryChoice(category));
        _category.SelectedIndex = 0;

        TableLayoutPanel form = NewFormTable(6);
        AddField(form, "Vehicle", _serviceVehicle, 0, 0);
        AddField(form, "Service date", _serviceDate, 0, 2);
        AddField(form, "Odometer (km)", _serviceOdometer, 1, 0);
        AddField(form, "Category", _category, 1, 2);
        AddField(form, "Description", _description, 2, 0, inputSpan: 3);
        AddField(form, "Parts cost (₦)", _parts, 3, 0);
        AddField(form, "Labor cost (₦)", _labor, 3, 2);
        AddField(form, "Discount (%)", _discount, 4, 0);
        AddField(form, string.Empty, _applyVat, 4, 2);

        var previewFrame = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 56,
            BackColor = SubtleSurface,
            Padding = new Padding(4, 0, 0, 0),
            Margin = new Padding(0, 8, 16, 0)
        };
        previewFrame.Paint += (_, e) =>
        {
            using var accent = new SolidBrush(CopperPrimary);
            e.Graphics.FillRectangle(accent, 0, 0, 4, previewFrame.Height);
            using var border = new Pen(CardBorder);
            e.Graphics.DrawRectangle(border, 0, 0, previewFrame.Width - 1, previewFrame.Height - 1);
        };
        previewFrame.Controls.Add(_preview);

        form.Controls.Add(previewFrame, 0, 5);
        form.SetColumnSpan(previewFrame, 3);

        _recordButton.Anchor = AnchorStyles.Right;
        _recordButton.Margin = new Padding(0, 14, 16, 0);
        form.Controls.Add(_recordButton, 3, 5);

        _serviceVehicle.SelectedIndexChanged += (_, _) =>
        {
            if (!_suppressOdometerPrefill)
                PrefillOdometer();
        };
        _parts.TextChanged += (_, _) => UpdatePreview();
        _labor.TextChanged += (_, _) => UpdatePreview();
        _discount.TextChanged += (_, _) => UpdatePreview();
        _applyVat.CheckedChanged += (_, _) => UpdatePreview();
        _recordButton.Click += (_, _) => TryRecordService();

        Panel formCard = CreateSectionCard("Record Service Entry", form, DockStyle.Top);
        Panel spacer = CreateVerticalSpacer(14);
        Panel gridCard = CreateSectionCard("Servicing History", _historyGrid, DockStyle.Fill);

        view.Controls.Add(gridCard);
        view.Controls.Add(spacer);
        view.Controls.Add(formCard);
        return view;
    }

    private Panel BuildCostsView()
    {
        var view = CreateViewContainer("tabCosts");

        var kpiGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 186,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = CanvasBg,
            Margin = new Padding(0)
        };

        for (int c = 0; c < 4; c++)
            kpiGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        for (int r = 0; r < 2; r++)
            kpiGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        kpiGrid.Controls.Add(CreateKpiCard("TOTAL SPEND", _kpiTotalSpend, CopperPrimary), 0, 0);
        kpiGrid.Controls.Add(CreateKpiCard("SERVICES RECORDED", _kpiServices, SidebarBg), 1, 0);
        kpiGrid.Controls.Add(CreateKpiCard("AVERAGE / SERVICE", _kpiAverage, SidebarBg), 2, 0);
        kpiGrid.Controls.Add(CreateKpiCard("VEHICLES", _kpiVehicles, SidebarBg), 3, 0);
        kpiGrid.Controls.Add(CreateKpiCard("PARTS COST", _kpiParts, CardBorder), 0, 1);
        kpiGrid.Controls.Add(CreateKpiCard("LABOR COST", _kpiLabor, CardBorder), 1, 1);
        kpiGrid.Controls.Add(CreateKpiCard("DISCOUNTS GIVEN", _kpiDiscount, CardBorder), 2, 1);
        kpiGrid.Controls.Add(CreateKpiCard("VAT CHARGED (7.5%)", _kpiVat, CardBorder), 3, 1);

        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = CanvasBg
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));

        Panel vehicleCostCard = CreateSectionCard("Spend by Vehicle", _costByVehicleGrid, DockStyle.Fill);
        vehicleCostCard.Margin = new Padding(0, 0, 0, 7);

        Panel categoryCostCard = CreateSectionCard("Spend by Category", _costByCategoryGrid, DockStyle.Fill);
        categoryCostCard.Margin = new Padding(0, 7, 0, 0);

        split.Controls.Add(vehicleCostCard, 0, 0);
        split.Controls.Add(categoryCostCard, 0, 1);

        Panel spacer = CreateVerticalSpacer(12);

        view.Controls.Add(split);
        view.Controls.Add(spacer);
        view.Controls.Add(kpiGrid);
        return view;
    }

    private Panel BuildAlertsView()
    {
        var view = CreateViewContainer("tabAlerts");

        var summaryContent = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = CardBg
        };
        summaryContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        summaryContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        summaryContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _alertSummary.Font = _cardTitleFont;
        _alertSummary.Anchor = AnchorStyles.Left;

        var legend = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = CardBg,
            Margin = new Padding(0)
        };
        legend.Controls.Add(CreateLegendPill("Overdue", OverdueBg, OverdueFg));
        legend.Controls.Add(CreateLegendPill("Due within 14d / 500 km", DueSoonBg, DueSoonFg));
        legend.Controls.Add(CreateLegendPill("Upcoming", UpcomingBg, UpcomingFg));

        summaryContent.Controls.Add(_alertSummary, 0, 0);
        summaryContent.Controls.Add(legend, 1, 0);

        var summaryCard = new Panel
        {
            Dock = DockStyle.Top,
            Height = 58,
            BackColor = CardBg,
            Padding = new Padding(18, 12, 18, 12)
        };
        AttachCardBorder(summaryCard);
        summaryCard.Controls.Add(summaryContent);

        Panel spacer = CreateVerticalSpacer(14);
        Panel gridCard = CreateSectionCard("Active Maintenance Schedules", _alertGrid, DockStyle.Fill);

        view.Controls.Add(gridCard);
        view.Controls.Add(spacer);
        view.Controls.Add(summaryCard);
        return view;
    }

    // ------------------------------------------------------------------ reusable UI builders

    private static Panel CreateViewContainer(string name) => new()
    {
        Name = name,
        Dock = DockStyle.Fill,
        BackColor = CanvasBg,
        Padding = new Padding(22, 18, 22, 18),
        Visible = false
    };

    private static Panel CreateVerticalSpacer(int height) => new()
    {
        Dock = DockStyle.Top,
        Height = height,
        BackColor = CanvasBg
    };

    private Panel CreateSectionCard(string title, Control body, DockStyle dock)
    {
        var card = new Panel
        {
            Dock = dock,
            AutoSize = dock == DockStyle.Top,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = CardBg,
            Padding = new Padding(18, 14, 18, 16)
        };
        AttachCardBorder(card);

        var header = new Label
        {
            Text = title,
            Font = _cardTitleFont,
            ForeColor = InkPrimary,
            Dock = DockStyle.Top,
            Height = 28,
            TextAlign = ContentAlignment.TopLeft,
            UseMnemonic = false
        };

        var rule = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = GridLineColor
        };

        var gap = new Panel
        {
            Dock = DockStyle.Top,
            Height = 10,
            BackColor = CardBg
        };

        body.Dock = dock == DockStyle.Top ? DockStyle.Top : DockStyle.Fill;

        card.Controls.Add(body);
        card.Controls.Add(gap);
        card.Controls.Add(rule);
        card.Controls.Add(header);
        return card;
    }

    private Panel CreateKpiCard(string caption, Label valueLabel, Color accentColor)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 12, 12),
            Padding = new Padding(16, 14, 14, 10),
            BackColor = CardBg
        };
        card.Paint += (_, e) =>
        {
            using var borderPen = new Pen(CardBorder);
            e.Graphics.DrawRectangle(borderPen, 0, 0, card.Width - 1, card.Height - 1);
            using var topBar = new SolidBrush(accentColor);
            e.Graphics.FillRectangle(topBar, 1, 1, card.Width - 2, 3);
        };

        var captionLabel = new Label
        {
            Text = caption,
            Dock = DockStyle.Top,
            Height = 20,
            Font = _kpiCaptionFont,
            ForeColor = InkMuted,
            UseMnemonic = false
        };

        valueLabel.Dock = DockStyle.Fill;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.Font = _kpiValueFont;
        valueLabel.ForeColor = InkPrimary;

        card.Controls.Add(valueLabel);
        card.Controls.Add(captionLabel);
        return card;
    }

    private Label CreateLegendPill(string text, Color backColor, Color foreColor) => new()
    {
        Text = text,
        AutoSize = true,
        Font = _badgeFont,
        BackColor = backColor,
        ForeColor = foreColor,
        Padding = new Padding(10, 4, 10, 4),
        Margin = new Padding(8, 2, 0, 2)
    };

    private static void AttachCardBorder(Panel panel)
    {
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(CardBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
        };
    }

    private TableLayoutPanel NewFormTable(int rows)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = rows,
            BackColor = CardBg,
            Padding = new Padding(0, 2, 0, 2)
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        for (int i = 0; i < rows; i++)
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        return table;
    }

    private void AddField(
        TableLayoutPanel table, string caption, Control input, int row, int labelColumn, int inputSpan = 1)
    {
        var label = new Label
        {
            Text = caption,
            Font = _fieldLabelFont,
            ForeColor = InkSecondary,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 12, 8),
            UseMnemonic = false
        };

        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(0, 5, 16, 5);

        table.Controls.Add(label, labelColumn, row);
        table.Controls.Add(input, labelColumn + 1, row);

        if (inputSpan > 1)
            table.SetColumnSpan(input, inputSpan);
    }

    private void StylePrimaryButton(Button button)
    {
        button.AutoSize = false;
        button.Size = new Size(164, 36);
        button.BackColor = CopperPrimary;
        button.ForeColor = Color.White;
        button.Font = _semiboldFont;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = CopperHover;
        button.FlatAppearance.MouseDownBackColor = CopperPressed;
        button.Cursor = Cursors.Hand;
    }

    private void StyleInput(Control control)
    {
        control.Font = _baseFont;
        control.ForeColor = InkPrimary;
        control.BackColor = Color.White;

        if (control is TextBox textBox)
            textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    private DataGridView CreateGrid(
        string name,
        string[] headers,
        int[] rightAlignedColumns,
        float[]? fillWeights = null,
        bool compact = false)
    {
        Font cellFont = compact ? _compactGridFont : _baseFont;
        Font headerFont = compact ? _compactHeaderFont : _gridHeaderFont;
        Padding cellPadding = compact ? new Padding(5, 0, 5, 0) : new Padding(8, 0, 8, 0);

        var grid = new DataGridView
        {
            Name = name,
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false,
            BackgroundColor = CardBg,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            GridColor = GridLineColor,
            ColumnHeadersHeight = 36,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        };

        grid.RowTemplate.Height = 38;

        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = SubtleSurface,
            ForeColor = InkSecondary,
            Font = headerFont,
            SelectionBackColor = SubtleSurface,
            SelectionForeColor = InkSecondary,
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = cellPadding
        };

        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = CardBg,
            ForeColor = InkPrimary,
            Font = cellFont,
            SelectionBackColor = GridSelectBg,
            SelectionForeColor = InkPrimary,
            Padding = cellPadding
        };

        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = GridAltRow,
            ForeColor = InkPrimary,
            Font = cellFont,
            SelectionBackColor = GridSelectBg,
            SelectionForeColor = InkPrimary,
            Padding = cellPadding
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var column = new DataGridViewTextBoxColumn
            {
                Name = $"col{i}",
                HeaderText = headers[i]
            };

            if (fillWeights is not null && i < fillWeights.Length)
                column.FillWeight = fillWeights[i];

            if (Array.IndexOf(rightAlignedColumns, i) >= 0)
            {
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }

            grid.Columns.Add(column);
        }

        // Enable double-buffering for smooth, flicker-free scrolling
        typeof(DataGridView)
            .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(grid, true);

        return grid;
    }

    // ------------------------------------------------------------------ custom vector brand mark & GDI cleanup

    private static void PaintBrandMark(Graphics g, Rectangle bounds)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (GraphicsPath badge = CreateRoundedRect(bounds, 9))
        using (var bgBrush = new SolidBrush(SidebarSurface))
        using (var borderPen = new Pen(SidebarDivider))
        {
            g.FillPath(bgBrush, badge);
            g.DrawPath(borderPen, badge);
        }

        float scale = bounds.Width / 256f;
        float ox = bounds.X;
        float oy = bounds.Y;
        PointF P(float x, float y) => new(ox + x * scale, oy + y * scale);

        // Ledger Apex A-chassis
        using var whiteBrush = new SolidBrush(Color.White);
        g.FillPolygon(whiteBrush, new[]
        {
            P(36, 198), P(114, 56), P(142, 56), P(178, 118), P(147, 118), P(128, 84), P(66, 198)
        });

        // Signal Amber active ledger bar
        using var amberBrush = new SolidBrush(SignalAmber);
        g.FillPolygon(amberBrush, new[]
        {
            P(118, 134), P(188, 134), P(202, 159), P(104, 159)
        });

        // Base ledger bar
        g.FillPolygon(whiteBrush, new[]
        {
            P(94, 174), P(212, 174), P(226, 198), P(80, 198)
        });
    }

    private static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private Font Own(Font font)
    {
        _ownedFonts.Add(font);
        return font;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _appIcon?.Dispose();
            foreach (Font font in _ownedFonts)
                font.Dispose();
            _ownedFonts.Clear();
        }

        base.Dispose(disposing);
    }

    private static Icon? LoadAppIcon()
    {
        using Stream? stream = typeof(MainForm).Assembly.GetManifestResourceStream("AutoLedgerSimple.Assets.autoledger.ico");
        return stream is null ? null : new Icon(stream);
    }
}
