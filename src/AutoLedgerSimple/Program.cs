using System.Security.Cryptography;
using System.Text;
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

            string mutexName = BuildInstanceMutexName(dataPath);
            using var instanceMutex = new Mutex(initiallyOwned: true, name: mutexName, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "AutoLedger is already running for this fleet file. Close the existing window before opening another instance.",
                    "AutoLedger",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

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

        private static string BuildInstanceMutexName(string filePath)
        {
            string normalized = Path.GetFullPath(filePath).ToUpperInvariant();
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return "Local\\AutoLedgerSimple_" + Convert.ToHexString(hash);
        }
    }
}

