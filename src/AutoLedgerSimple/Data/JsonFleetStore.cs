using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoLedgerSimple.Models;

namespace AutoLedgerSimple.Data;

public class JsonFleetStore : IFleetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public string FilePath { get; }

    private string? _lastKnownSnapshot;

    public JsonFleetStore(string filePath)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _lastKnownSnapshot = TryReadCurrentDiskText();
    }

    public FleetDatabase Load()
    {
        if (!File.Exists(FilePath))
        {
            _lastKnownSnapshot = null;
            return new FleetDatabase();
        }

        var json = File.ReadAllText(FilePath);
        _lastKnownSnapshot = json;

        if (string.IsNullOrWhiteSpace(json))
            return new FleetDatabase();

        return DeserializeDatabase(json);
    }

    public void Save(FleetDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = SerializeSnapshot(database);
        using FileStream lockStream = AcquireFileLock();

        string? currentOnDisk = File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
        if (!string.Equals(currentOnDisk, _lastKnownSnapshot, StringComparison.Ordinal))
        {
            throw new IOException(
                "The fleet file was modified by another instance. Reload the application before saving to avoid overwriting changes.");
        }

        var tempPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
            _lastKnownSnapshot = json;
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw;
        }
    }

    private FileStream AcquireFileLock()
    {
        string lockPath = FilePath + ".lock";
        var deadline = Stopwatch.StartNew();
        const int timeoutMs = 3000;

        while (true)
        {
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1);
            }
            catch (IOException) when (deadline.ElapsedMilliseconds < timeoutMs)
            {
                Thread.Sleep(15);
            }
        }
    }

    internal static string SerializeSnapshot(FleetDatabase database)
        => JsonSerializer.Serialize(database, Options);

    internal static void RestoreSnapshot(FleetDatabase target, string snapshotJson)
    {
        FleetDatabase restored = DeserializeDatabase(snapshotJson);
        target.Vehicles.Clear();
        target.Vehicles.AddRange(restored.Vehicles);
        target.ServiceRecords.Clear();
        target.ServiceRecords.AddRange(restored.ServiceRecords);
        target.Schedules.Clear();
        target.Schedules.AddRange(restored.Schedules);
    }

    private static FleetDatabase DeserializeDatabase(string json)
    {
        var db = JsonSerializer.Deserialize<FleetDatabase>(json, Options) ?? new FleetDatabase();
        foreach (var record in db.ServiceRecords)
        {
            record.RecalculateCosts();
        }

        return db;
    }

    private string? TryReadCurrentDiskText()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}