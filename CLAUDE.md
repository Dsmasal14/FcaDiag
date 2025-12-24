# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build entire solution
dotnet build

# Build specific project
dotnet build src/FcaDiag.Core

# Run tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~ModuleDatabaseTests"

# Run console application (Windows only - requires J2534 adapter)
dotnet run --project src/FcaDiag.Console
```

## Architecture

FcaDiag is a .NET diagnostic tool for FCA (Fiat Chrysler Automobiles) vehicles using UDS (Unified Diagnostic Services) over CAN bus via J2534 pass-thru adapters.

### Project Structure

- **FcaDiag.Core** - Core abstractions, models, and enums (cross-platform)
  - `Enums/` - Protocol enums (UDS service IDs, NRCs, session types, module types)
  - `Models/` - Data models (UdsResponse, DiagnosticTroubleCode, FcaModuleDefinition)
  - `Interfaces/` - `ICanAdapter` for hardware abstraction, `IDiagnosticService` for UDS operations

- **FcaDiag.Protocols** - Protocol implementations (cross-platform)
  - `Transport/` - FCA module database with CAN IDs, DID definitions, routine IDs
  - `Uds/` - UDS client implementation and ISO-TP (ISO 15765-2) frame handling

- **FcaDiag.J2534** - J2534 pass-thru adapter (Windows only, net10.0-windows)
  - `Native/` - P/Invoke definitions, API structures, error codes
  - `J2534Adapter` - Implements `ICanAdapter` using J2534 API
  - `J2534DeviceDiscovery` - Finds installed J2534 devices from Windows registry

- **FcaDiag.Console** - Interactive CLI application (Windows only)
- **FcaDiag.Tests** - xUnit tests

### Key Patterns

**CAN Addressing**: Each ECU has a request ID (tester→ECU) and response ID (ECU→tester). Standard OBD-II uses 0x7E0-0x7E7/0x7E8-0x7EF. FCA modules use manufacturer-specific IDs defined in `FcaModuleDatabase`.

**UDS Flow**: `UdsClient` implements `IDiagnosticService` and handles:
- Session management (default, extended, programming)
- Security access with seed/key
- DID read/write operations
- DTC read/clear
- Routine control

**ISO-TP**: `IsoTpHandler` segments/reassembles messages >8 bytes using single frame, first frame, consecutive frame, and flow control frames.

**J2534**: SAE standard pass-thru interface. DLLs are discovered from registry path `HKLM\SOFTWARE\PassThruSupport.04.04`. The adapter loads the vendor DLL dynamically and uses ISO15765 protocol for UDS communication.

### Extending

To add a CAN adapter (e.g., SocketCAN for Linux):
1. Create new project targeting the platform
2. Implement `ICanAdapter` interface
3. Reference from Console or create platform-specific CLI

To add vehicle-specific modules:
1. Add module type to `FcaModuleType` enum in Core
2. Add definition to `FcaModuleDatabase.Modules` in Protocols
