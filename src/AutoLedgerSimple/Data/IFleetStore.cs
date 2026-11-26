using AutoLedgerSimple.Data;

namespace AutoLedgerSimple.Data;

public interface IFleetStore
{
    string FilePath { get; }
    FleetDatabase Load();
    void Save(FleetDatabase database);
}