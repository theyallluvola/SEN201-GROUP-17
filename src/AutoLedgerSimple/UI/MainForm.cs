using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AutoLedgerSimple.Data;
using AutoLedgerSimple.Models;
using AutoLedgerSimple.Services;

namespace AutoLedgerSimple.UI;

/// <summary>
/// Four-tab desktop front end: Fleet Vehicles, Servicing History, Maintenance Costs
/// and Upcoming &amp; Overdue Alerts. Built entirely in code (no designer file).
/// All feedback goes to the status bar rather than modal dialogs.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly Color OverdueColor = Color.FromArgb(255, 218, 218);
    private static readonly Color DueSoonColor = Color.FromArgb(255, 243, 205);
    private static readonly Color UpcomingColor = Color.FromArgb(220, 242, 225);

    private sealed record VehicleChoice(Guid Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record CategoryChoice(ServiceCategory Value)
    {
        public override string ToString() => UiValidationHelper.FormatCategory(Value);
    }

    private readonly IFleetStore _store;
    private readonly FleetDatabase _database;
    private readonly VehicleService _vehicles;
    private readonly MaintenanceService _maintenance;

    private readonly TabControl _tabs = new() { Name = "tabs", Dock = DockStyle.Fill };
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Text = "Ready." };

    // Fleet Vehicles tab
    private readonly ComboBox _vehicleType = new() { Name = "cboVehicleType", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _plate = new() { Name = "txtPlate" };
    private readonly TextBox _make = new() { Name = "txtMake" };
    private readonly TextBox _model = new() { Name = "txtModel" };
    private readonly TextBox _year = new() { Name = "txtYear" };
    private readonly TextBox _odometer = new() { Name = "txtOdometer" };
    private readonly TextBox _owner = new() { Name = "txtOwner" };
    private readonly TextBox _registeredOn = new() { Name = "txtRegisteredOn", PlaceholderText = "yyyy-MM-dd (blank = today)" };
    private readonly Button _registerButton = new() { Name = "btnRegister", Text = "Register vehicle", AutoSize = true, Anchor = AnchorStyles.Right };
    private readonly DataGridView _vehicleGrid = CreateGrid(
        "gridVehicles",
        new[] { "Plate", "Type", "Vehicle", "Year", "Odometer (km)", "Owner", "Registered", "Next service" },
        new[] { 3, 4 });

    // Servicing History tab
    private readonly ComboBox _serviceVehicle = new() { Name = "cboServiceVehicle", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _serviceDate = new() { Name = "txtServiceDate" };
    private readonly TextBox _serviceOdometer = new() { Name = "txtServiceOdometer" };
    private readonly ComboBox _category = new() { Name = "cboCategory", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _description = new() { Name = "txtDescription" };
    private readonly TextBox _parts = new() { Name = "txtParts" };
    private readonly TextBox _labor = new() { Name = "txtLabor" };
    private readonly TextBox _discount = new() { Name = "txtDiscount", Text = "0" };
    private readonly CheckBox _applyVat = new() { Name = "chkVat", Text = "Apply VAT (7.5%)", AutoSize = true };
    private readonly Label _preview = new()
    {
        Name = "lblPreview",
        AutoSize = false,
        Height = 56,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.FromArgb(245, 248, 255),
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 8, 0)
    };
    private readonly Button _recordButton = new() { Name = "btnRecord", Text = "Record service", AutoSize = true, Anchor = AnchorStyles.Right };
    private readonly DataGridView _historyGrid = CreateGrid(
        "gridHistory",
        new[] { "Date", "Plate", "Category", "Description", "Odometer (km)", "Parts", "Labor", "Discount", "VAT", "Total" },
        new[] { 4, 5, 6, 7, 8, 9 });

    // Maintenance Costs tab
    private readonly Label _kpiTotalSpend = new() { Name = "kpiTotalSpend", Text = "₦0.00" };
    private readonly Label _kpiServices = new() { Name = "kpiServices", Text = "0" };
    private readonly Label _kpiAverage = new() { Name = "kpiAverage", Text = "₦0.00" };
    private readonly Label _kpiParts = new() { Name = "kpiParts", Text = "₦0.00" };
    private readonly Label _kpiLabor = new() { Name = "kpiLabor", Text = "₦0.00" };
    private readonly Label _kpiDiscount = new() { Name = "kpiDiscount", Text = "₦0.00" };
    private readonly Label _kpiVat = new() { Name = "kpiVat", Text = "₦0.00" };
    private readonly Label _kpiVehicles = new() { Name = "kpiVehicles", Text = "0" };
    private readonly DataGridView _costByVehicleGrid = CreateGrid(
        "gridCostByVehicle",
        new[] { "Plate", "Vehicle", "Type", "Services", "Parts", "Labor", "Discount", "VAT", "Total" },
        new[] { 3, 4, 5, 6, 7, 8 });
    private readonly DataGridView _costByCategoryGrid = CreateGrid(
        "gridCostByCategory",
        new[] { "Category", "Services", "Total" },
        new[] { 1, 2 });

    // Alerts tab
    private readonly Label _alertSummary = new() { Name = "lblAlertSummary", AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
    private readonly DataGridView _alertGrid = CreateGrid(
        "gridAlerts",
        new[] { "Status", "Plate", "Vehicle", "Due date", "Time left", "Due at (km)", "Current (km)", "Distance left" },
        new[] { 5, 6 });

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

        Text = "AutoLedger - Vehicle Maintenance Management";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 720);
        MinimumSize = new Size(1000, 640);

        _serviceDate.Text = Today.ToString("yyyy-MM-dd", Inv);

        _tabs.TabPages.Add(BuildFleetTab());
        _tabs.TabPages.Add(BuildServiceTab());
        _tabs.TabPages.Add(BuildCostsTab());
        _tabs.TabPages.Add(BuildAlertsTab());

        _statusStrip.Items.Add(_statusLabel);
        Controls.Add(_tabs);
        Controls.Add(_statusStrip);

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
            JsonFleetStore.RestoreSnapshot(_database, snapshot);
            RefreshAll();
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
            JsonFleetStore.RestoreSnapshot(_database, snapshot);
            RefreshAll();
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
                _vehicleGrid.Rows[row].Cells[7].Style.BackColor = StatusColor(status.Value);
        }
    }

    private void RefreshServiceVehicleChoices()
    {
        Guid? previous = (_serviceVehicle.SelectedItem as VehicleChoice)?.Id;

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

        _costByCategoryGrid.Rows.Clear();
        foreach (CategoryCostSummary c in summary.ByCategory)
        {
            _costByCategoryGrid.Rows.Add(
                UiValidationHelper.FormatCategory(c.Category),
                c.ServiceCount.ToString(Inv),
                UiValidationHelper.FormatNaira(c.TotalSpend));
        }
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

            _alertGrid.Rows[row].DefaultCellStyle.BackColor = StatusColor(a.Status);
        }

        int overdue = alerts.Count(a => a.Status == ScheduleStatus.Overdue);
        int dueSoon = alerts.Count(a => a.Status == ScheduleStatus.DueSoon);
        int upcoming = alerts.Count(a => a.Status == ScheduleStatus.Upcoming);
        _alertSummary.Text = $"{overdue} overdue   |   {dueSoon} due soon   |   {upcoming} upcoming";
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
            $"Subtotal {UiValidationHelper.FormatNaira(sample.SubTotal)}   |   " +
            $"Discount ({discountText}%) -{UiValidationHelper.FormatNaira(sample.DiscountAmount)}{Environment.NewLine}" +
            $"{vatLabel} {UiValidationHelper.FormatNaira(sample.VatAmount)}   |   " +
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
        _statusLabel.ForeColor = isError ? Color.Firebrick : Color.SeaGreen;
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

    private static Color StatusColor(ScheduleStatus status) => status switch
    {
        ScheduleStatus.Overdue => OverdueColor,
        ScheduleStatus.DueSoon => DueSoonColor,
        ScheduleStatus.Upcoming => UpcomingColor,
        _ => SystemColors.Window
    };

    // ------------------------------------------------------------------ layout

    private TabPage BuildFleetTab()
    {
        var page = new TabPage("Fleet Vehicles") { Name = "tabFleet", Padding = new Padding(8) };

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
        AddField(form, "Owner", _owner, 3, 0);
        AddField(form, "Registered on", _registeredOn, 3, 2);
        form.Controls.Add(_registerButton, 3, 4);

        _registerButton.Click += (_, _) => TryRegisterVehicle();

        page.Controls.Add(_vehicleGrid);
        page.Controls.Add(form);
        return page;
    }

    private TabPage BuildServiceTab()
    {
        var page = new TabPage("Servicing History") { Name = "tabService", Padding = new Padding(8) };

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

        _preview.Dock = DockStyle.Fill;
        form.Controls.Add(_preview, 0, 5);
        form.SetColumnSpan(_preview, 3);
        form.Controls.Add(_recordButton, 3, 5);

        _serviceVehicle.SelectedIndexChanged += (_, _) => PrefillOdometer();
        _parts.TextChanged += (_, _) => UpdatePreview();
        _labor.TextChanged += (_, _) => UpdatePreview();
        _discount.TextChanged += (_, _) => UpdatePreview();
        _applyVat.CheckedChanged += (_, _) => UpdatePreview();
        _recordButton.Click += (_, _) => TryRecordService();

        _historyGrid.Columns[3].FillWeight = 220;

        page.Controls.Add(_historyGrid);
        page.Controls.Add(form);
        return page;
    }

    private TabPage BuildCostsTab()
    {
        var page = new TabPage("Maintenance Costs") { Name = "tabCosts", Padding = new Padding(8) };

        var kpis = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 6)
        };
        kpis.Controls.Add(CreateKpiCard("Total spend", _kpiTotalSpend));
        kpis.Controls.Add(CreateKpiCard("Services recorded", _kpiServices));
        kpis.Controls.Add(CreateKpiCard("Average per service", _kpiAverage));
        kpis.Controls.Add(CreateKpiCard("Vehicles", _kpiVehicles));
        kpis.Controls.Add(CreateKpiCard("Parts", _kpiParts));
        kpis.Controls.Add(CreateKpiCard("Labor", _kpiLabor));
        kpis.Controls.Add(CreateKpiCard("Discounts given", _kpiDiscount));
        kpis.Controls.Add(CreateKpiCard("VAT charged", _kpiVat));

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        split.Controls.Add(Wrap("Spend by vehicle", _costByVehicleGrid), 0, 0);
        split.Controls.Add(Wrap("Spend by category", _costByCategoryGrid), 1, 0);

        page.Controls.Add(split);
        page.Controls.Add(kpis);
        return page;
    }

    private TabPage BuildAlertsTab()
    {
        var page = new TabPage("Upcoming && Overdue Alerts") { Name = "tabAlerts", Padding = new Padding(8) };

        var legend = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Text = "Red = overdue   |   Amber = due within 14 days or 500 km   |   Green = upcoming",
            Margin = new Padding(0, 0, 0, 6)
        };

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        header.Controls.Add(_alertSummary);
        header.Controls.Add(legend);

        page.Controls.Add(_alertGrid);
        page.Controls.Add(header);
        return page;
    }

    private static TableLayoutPanel NewFormTable(int rows)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = rows,
            Padding = new Padding(0, 0, 0, 8)
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        for (int i = 0; i < rows; i++)
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        return table;
    }

    private static void AddField(
        TableLayoutPanel table, string caption, Control input, int row, int labelColumn, int inputSpan = 1)
    {
        var label = new Label
        {
            Text = caption,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 6)
        };

        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(0, 3, 16, 3);

        table.Controls.Add(label, labelColumn, row);
        table.Controls.Add(input, labelColumn + 1, row);

        if (inputSpan > 1)
            table.SetColumnSpan(input, inputSpan);
    }

    private static Panel CreateKpiCard(string caption, Label valueLabel)
    {
        var card = new Panel
        {
            Width = 190,
            Height = 70,
            Margin = new Padding(0, 0, 10, 10),
            Padding = new Padding(10, 6, 10, 6),
            BackColor = Color.FromArgb(240, 244, 250)
        };

        var captionLabel = new Label
        {
            Text = caption,
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = Color.DimGray
        };

        valueLabel.Dock = DockStyle.Fill;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.Font = new Font("Segoe UI Semibold", 13F);

        card.Controls.Add(valueLabel);
        card.Controls.Add(captionLabel);
        return card;
    }

    private static GroupBox Wrap(string title, Control content)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8) };
        content.Dock = DockStyle.Fill;
        box.Controls.Add(content);
        return box;
    }

    private static DataGridView CreateGrid(string name, string[] headers, int[] rightAlignedColumns)
    {
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
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var column = new DataGridViewTextBoxColumn
            {
                Name = $"col{i}",
                HeaderText = headers[i]
            };

            if (Array.IndexOf(rightAlignedColumns, i) >= 0)
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            grid.Columns.Add(column);
        }

        return grid;
    }
}
