# DROPZONE – Testing Strategy & Quality Assurance

## 1. Testing Philosophy
To guarantee high performance, security, and rock-solid reliability across operating systems, DROPZONE implements testing across four distinct tiers:

1. **Unit Tests**:
   * C# Domain logic (Manifest generation, chunk planning, ETA/speed calculations, state transitions).
   * C# Infrastructure (SQLite repository, serialization, path validation, quarantine mechanics).
   * Rust unit tests (Binary framing, chunk packing/unpacking, SHA-256 verification, AEAD cipher operations).
2. **FFI Interoperability Tests**:
   * Marshalling verification, memory leak checks across unmanaged boundaries, event callback delivery.
3. **Integration & Network Simulation Tests**:
   * Loopback transfers (Sender -> Receiver on localhost).
   * Packet loss, pause/resume offset validation, partial file interruption and recovery.
   * Path traversal attack simulation (verifying rejection of dangerous relative paths).
4. **End-to-End & UI Verification**:
   * ViewModels and command bindings tested using MVVM testing harnesses.
   * Multi-file transfer progress, cancel handling, error recovery.

---

## 2. Test Execution Commands

### Run Rust Engine Tests:
```powershell
cd engine
cargo test --offline
```

### Run C# Unit & Integration Tests:
```powershell
cd src
$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; $env:MSBUILDUSESERVER="0"; dotnet test DropZone.sln
```

