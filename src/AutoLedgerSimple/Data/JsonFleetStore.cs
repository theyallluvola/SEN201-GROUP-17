using System.Text.Json;
using System.Text.Json.Serialization;
using AutoLedgerSimple.Data;
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

    public JsonFleetStore(string filePath)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public FleetDatabase Load()
    {
        if (!File.Exists(FilePath))
            return new FleetDatabase();

        var json = File.ReadAllText(FilePath);
        if (string.IsNullOrWhiteSpace(json))
            return new FleetDatabase();

        return JsonSerializer.Deserialize<FleetDatabase>(json, Options) ?? new FleetDatabase();
    }

    public void Save(FleetDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        var json = JsonSerializer.Serialize(database, Options);
        File.WriteAllText(FilePath, json);
    }
}