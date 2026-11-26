using System.Text.Json;
using AutoLedgerSimple.Data;
using AutoLedgerSimple.Services;
using AutoLedgerSimple.UI;

namespace AutoLedgerSimple
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            string dataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoLedgerSimple",
                "fleet.json");

            IFleetStore store = new JsonFleetStore(dataPath);

            FleetDatabase database;
            try
            {
                database = store.Load();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                MessageBox.Show(
                    $"The fleet data file could not be read:\n{store.FilePath}\n\n{ex.Message}",
                    "AutoLedger",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var vehicleService = new VehicleService(database);
            var maintenanceService = new MaintenanceService(database);

            if (maintenanceService.SeedSampleDataIfEmpty())
            {
                try
                {
                    store.Save(database);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The form reports save problems on the next change, so start up anyway.
                }
            }

            Application.Run(new MainForm(store, database, vehicleService, maintenanceService));
        }
    }
}

