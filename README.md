# DROPZONE

> **Move Files. Not Your Privacy.**

DROPZONE is a high-performance, privacy-first, cross-platform file-sharing application supporting Windows, Linux, macOS, Android, and iOS. Its mission is to enable secure, direct device-to-device file transfers over local networks and the internet without relying on traditional cloud file storage.

---

## 🚀 Key Highlights

* **True Zero-Cloud Architecture**: Transfers stream directly peer-to-peer (P2P). No intermediary server ever stores, decrypts, or buffers your data.
* **Modern Hybrid Tech Stack**:
  * **Frontend**: C# / .NET / Avalonia UI (XAML, MVVM) — responsive, native desktop UX with zero gradients, crisp typography, and dark/light themes.
  * **Core Engine**: Rust + Tokio async networking — memory-safe, ultra-low latency, multi-gigabit throughput.
  * **Storage**: Embedded SQLite for atomic transfer history, paired devices, and permission audits.
* **Military-Grade Security**:
  * Authenticated End-to-End Encryption (AEAD: ChaCha20-Poly1305 / AES-256-GCM).
  * Ephemeral key exchange with Short Authentication String (SAS) / QR / PIN pairing.
  * SHA-256 streaming verification per chunk and whole file.
  * Quarantine zone, permission enforcement, and strict path-traversal prevention.

---

## 🏛 Architecture Overview

DROPZONE follows a clean, decoupled 8-layer architecture:

```
┌─────────────────────────────────────────────────────────────┐
│                    Presentation Layer                       │
│        (Avalonia UI, XAML, MVVM, Reusable Components)       │
└─────────────────────────────┬───────────────────────────────┘
                              │
┌─────────────────────────────▼───────────────────────────────┐
│                     Application Layer                       │
│ (Transfer Orchestrator, Queue, Speed/ETA Calc, Pairing Coor)│
└──────────────┬───────────────────────────────┬──────────────┘
               │                               │
┌──────────────▼──────────────┐ ┌──────────────▼──────────────┐
│        Domain Layer         │ │       Security Engine       │
│(TransferJob, Device, Chunks)│ │ (AEAD, SHA-256, Quarantine) │
└──────────────┬──────────────┘ └──────────────┬──────────────┘
               │                               │
┌──────────────▼───────────────────────────────▼──────────────┐
│                   Infrastructure Layer                      │
│ (Sqlite Repositories, File I/O Streams, Logging, Platform)  │
└─────────────────────────────┬───────────────────────────────┘
                              │ C-ABI FFI Bridge
┌─────────────────────────────▼───────────────────────────────┐
│                  Native Networking Engine                   │
│         (Rust + Tokio, Framed Streaming, Direct LAN)        │
└─────────────────────────────────────────────────────────────┘
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for full structural details.

---

## 🗺 Phased Development Roadmap

1. **Phase 1: Core File Transfer** (Completed ✅)
   * Rust Tokio streaming engine (binary framing `DZP1`, 64-byte chunk descriptor, backpressure).
   * C# FFI Interop bridge (`DropZoneEngineInterop`).
   * Domain model, transfer queue, concurrency slots, pause/resume/cancel mechanics.
   * SQLite persistence (WAL mode) for transfer jobs and history.
   * Avalonia MVVM desktop interface with drag-and-drop file/folder sharing, dark theme, and solid palette.
2. **Phase 2: Security & Advanced Networking** (Completed ✅)
   * Authenticated end-to-end encryption (ChaCha20-Poly1305 with sequence nonces).
   * Perfect Forward Secrecy: Ephemeral Curve25519 (`X25519`) Diffie-Hellman key agreement + RFC 5869 HKDF-SHA256 session key derivation.
   * Cryptographic device authentication & pairing via 6-digit Short Authentication String (SAS) numeric PIN matching.
   * RFC 5389 STUN client for reflexive NAT candidate discovery (`srflx`) and direct P2P hole punching.
   * RFC 5766 TURN relay client framing with guaranteed zero-knowledge blind encryption.
   * Heuristic threat analyzer: Right-to-Left Override (RTLO) Unicode spoofing detection, disguised double extension detection.
   * Isolated Quarantine Vault (`%LOCALAPPDATA%/DropZone/Quarantine/`) with read-only attribution, companion `.meta.json` tracking, safe restoration, and permanent purge.
   * Device permission management (Trusted, Prompt, Blocked, Revoked) in SQLite.
3. **Phase 3: Analytics & Device Management** (Completed ✅)
   * Real-time network telemetry, diagnostic RTT ping probes, and link latency tracking.
   * Adaptive chunk sizing engine dynamically scaling between 64 KB and 4 MB based on measured bandwidth and loss.
   * Granular device permissions (`DevicePermissions`: auto-accept byte thresholds, custom folder scoping, forced PIN verification, instant revocation).
   * Encrypted local backup vault (`.dzbackup` envelope with PBKDF2 100,000 iterations + AES-256-GCM AEAD).
   * Dedicated Avalonia UI Diagnostics & Telemetry view with live connection metrics and encrypted export/import.
4. **Phase 4: Mobile Applications (Android & iOS)** (Completed ✅)
   * Platform abstraction layer (`IPlatformService`, `IPlatformWakeLockService`, `IPlatformNotificationService`, `IPlatformLifecycleService`, `IPlatformStoragePicker`).
   * Android application (`DropZone.Android`):
     * Foreground service (`DropZoneTransferForegroundService`) with `FOREGROUND_SERVICE_TYPE_DATA_SYNC` to prevent Doze mode teardown.
     * `PowerManager.WakeLock` (`PARTIAL_WAKE_LOCK`) and `WifiManager.MulticastLock` bypass for uninterrupted mDNS discovery.
     * Scoped storage and Storage Access Framework (SAF) integration.
     * Low-importance ongoing progress notification channel and high-importance security alerts.
   * iOS application (`DropZone.iOS`):
     * Sandboxed container integration with `UIDocumentPickerViewController`.
     * `UIApplication.SharedApplication.IdleTimerDisabled` auto-lock and sleep prevention.
     * `UNUserNotificationCenter` notification dispatching and badge management.
     * `NSLocalNetworkUsageDescription` and Bonjour services registration in `Info.plist`.
     * `BGTaskScheduler` (`com.dropzone.transfer.background`) and `BGProcessingTask` execution.
   * Cross-platform responsive UI adaptation:
     * Shared `MainView.axaml` (`UserControl`) supporting both Desktop (`IClassicDesktopStyleApplicationLifetime`) and Mobile (`ISingleViewApplicationLifetime`).
     * Responsive navigation: 200px Left Rail for wide/desktop screens and thumb-friendly Bottom Navigation Bar for mobile/portrait form factors.
     * Mobile file picker action card replacing desktop drag-and-drop.
   * Multi-target native engine configuration (`cdylib` and `staticlib`) and runtime dynamic library resolution across desktop and mobile operating systems.
5. **Phase 5: Pro & Enterprise Functionality** (Completed ✅)
   * Continuous / Recursive Folder Synchronization (`FolderSyncService`): directory tree delta analysis, file status tracking (`Added`, `Modified`, `Deleted`, `Identical`), atomic `.syncpart` write staging, and configurable conflict resolution policies.
   * Tamper-Evident Compliance Audit Ledger (`ComplianceAuditService`): immutable append-only hash-chained ledger in SQLite, mathematically verifiable against tampering or sequence deletions for SOC2, HIPAA, and ISO 27001 compliance.
   * Enterprise PKI Certificate Validation (`EnterpriseCertificateValidator`): X.509 device identity verification, internal Root CA enrollment, and CRL revocation enforcement.
   * Bandwidth Throttling & Quality of Service (`BandwidthThrottler`): token-bucket rate limiter for managing network utilization across shared uplinks.
6. **Phase 6: Quality Assurance & Stable Release** (Completed ✅)
   * Full-stack end-to-end integration tests (`ReleaseValidationTests`): validating all 8 application layers in sequence.
   * 73 total automated tests (60 .NET integration tests + 13 Rust core engine unit tests) with 100% pass rate.
   * Production release documentation: [Release Notes](docs/RELEASE_NOTES.md), [Installation & Deployment Guide](docs/INSTALLATION.md), [Architecture Specification](docs/ARCHITECTURE.md), [Security Guide](docs/SECURITY.md), [Networking Protocol](docs/NETWORKING_PROTOCOL.md), and [API Reference](docs/API_REFERENCE.md).

---

## 🛠 Building & Running

### Prerequisites
* **.NET 8.0+ / .NET 9.0+ / .NET 11.0 SDK**
* **Rust 1.80+ (Cargo)**
* **LLVM / C Compiler** (e.g., clang / MSVC)

### Build Engine (Rust)
```bash
cd engine
cargo build --release
```

### Build Application (.NET)
```bash
cd src
dotnet build DropZone.sln
```

### Run Tests
```bash
cd src
dotnet test DropZone.sln
```

---

## 📄 Documentation

* [System Architecture](docs/ARCHITECTURE.md)
* [Security Specification](docs/SECURITY.md)
* [Networking Protocol](docs/NETWORKING_PROTOCOL.md)
* [API Reference](docs/API_REFERENCE.md)
* [Testing Strategy](docs/TESTING.md)
* [Contribution Guidelines](docs/CONTRIBUTING.md)

---

## ⚖️ License & Privacy Commitment

DROPZONE is dedicated to uncompromising personal privacy.
**Move Files. Not Your Privacy.**

