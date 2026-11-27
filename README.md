# AutoLedger — Vehicle Maintenance Management System

**SEN 201 — Introduction to Software Engineering (2026)**  
School of Science and Technology, Pan-Atlantic University  

AutoLedger is a C# Windows Forms desktop application for managing a vehicle fleet. It lets fleet managers register vehicles, record servicing history with automatic discount and 7.5% VAT calculation, monitor maintenance costs, and track upcoming or overdue service schedules backed by atomic local JSON storage.

## Group 17 Members

| Name | Matric Number | GitHub | Role |
|---|---|---|---|
| **MADUBUKO Obumneme** | 25120112031 | `@Obumneme-1` | Project Manager & Data/Domain Architect — Part 1: OOP Vehicle Models, Schedules & Atomic JSON Store |
| **IGUN Olaoluwa** | 26120112022 | `@theyallluvola` | Lead C# Backend & Brand Engineer — Part 2: Business Services, Scheduling Engine, Cost Reporting & Brand Icon |
| **Obiagwu Ronald** | 25120112035 | `@R-obiagwu12` | Desktop UI Engineer, System Modeller & QA Lead — Part 3: WinForms Interface, Input Validation & xUnit Test Suite |

## Core Features

- **Fleet Vehicles:** Register Passenger, Commercial, and Motorcycle vehicles with subtype-specific default maintenance intervals (`180d / 5,000 km`, `90d / 10,000 km`, `60d / 3,000 km`), unique plate validation, and live schedule status.
- **Servicing History:** Record maintenance entries with odometer monotonicity checks (including backdated historical records), parts and labor costs in Naira (`₦`), percentage discounts, optional 7.5% VAT, and automatic rollover to the next active schedule.
- **Maintenance Costs:** View fleet-wide KPI totals alongside per-vehicle and per-category expenditure breakdowns.
- **Upcoming & Overdue Alerts:** Prioritised alert list colour-coded by urgency — **Overdue**, **Due Soon** (within 14 days or 500 km), and **Upcoming**.

## Project Structure

```text
AutoLedgerSimple.sln
├── src/AutoLedgerSimple/
│   ├── Assets/          # Application icon (autoledger.ico)
│   ├── Data/            # FleetDatabase, IFleetStore, JsonFleetStore (atomic + file-locked JSON)
│   ├── Models/          # Vehicle hierarchy, ServiceRecord, MaintenanceSchedule
│   ├── Services/        # VehicleService, MaintenanceService, FleetReportModels
│   ├── UI/              # MainForm, UiValidationHelper
│   └── Program.cs       # Single-instance desktop entry point
└── tests/AutoLedgerSimple.Tests/
    └── SimpleVmmsTests.cs  # 33 xUnit unit, integration, UI, and regression tests
```

## Build, Run & Test

Requires Windows and the [.NET SDK](https://dotnet.microsoft.com/download) (.NET 9 or newer via major-version roll-forward).

```powershell
# Build the solution
dotnet build -c Release

# Run the desktop application
dotnet run --project src/AutoLedgerSimple

# Run the automated test suite (33 tests)
dotnet test -c Release
```

Fleet data is stored automatically at `%LocalAppData%\AutoLedgerSimple\fleet.json` and seeded with sample fleet vehicles on first launch.

## Development Process

Built using an **Iterative and Incremental (Agile)** workflow with XP engineering practices: each group member delivered a vertical layer via feature branches and Pull Requests, with peer code review and automated xUnit regression tests verifying every fix before merging into `main`.
