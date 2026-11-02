# DROPZONE – Release Notes

## Version 1.0.0 (Production Stable Release)

**Release Date:** November 2026  
**Tagline:** *Move Files. Not Your Privacy.*  
**Status:** Stable / Production Ready

DROPZONE is a high-throughput, zero-cloud, privacy-first cross-platform file transfer platform engineered using a hybrid C#/.NET 11 Avalonia UI and Rust/Tokio asynchronous networking architecture.

---

### Milestone Changelog by Development Phase

#### Phase 1: Core File Transfer
* **Tokio Framed Streaming Transport (`DZP1`)**: High-throughput binary framing protocol over non-blocking TCP sockets with backpressure flow control.
* **Chunk Multiplexing & Resumption**: 64-byte structured chunk descriptors, deterministic chunk indexing, and automatic interrupted transfer recovery.
* **C-ABI FFI Interop**: Thread-safe P/Invoke bridge between .NET and Rust with pinned static unmanaged delegates.
* **Local Persistence (SQLite WAL)**: High-speed local database storing transfer histories, device entries, and configuration state.
* **Avalonia Desktop UI**: Professional solid-color aesthetic (zero gradients), dark and light theme palettes, responsive MVVM bindings.

#### Phase 2: Security & Advanced Networking
* **Authenticated End-to-End Encryption (AEAD)**: ChaCha20-Poly1305 with 96-bit sequence-isolated nonces.
* **Perfect Forward Secrecy**: Ephemeral Curve25519 (`X25519`) Diffie-Hellman key agreement + RFC 5869 HKDF-SHA256 session key derivation.
* **Cryptographic Device Pairing**: 6-digit Short Authentication String (SAS) numeric PIN verification preventing Man-in-the-Middle (MITM) attacks.
* **NAT Traversal & Blind Relay**: RFC 5389 STUN candidate discovery and RFC 5766 TURN blind channel data encapsulation.
* **Heuristic Threat Defense**: Automatic detection of Right-to-Left Override (RTLO) Unicode spoofing, disguised double extensions (`.pdf.exe`), and isolation into a dedicated read-only Quarantine Vault (`%LOCALAPPDATA%/DropZone/Quarantine/`).

#### Phase 3: Analytics & Device Management
* **Real-Time Telemetry Engine**: Continuous link latency monitoring (EWMA RTT) and route classification (`DirectLAN`, `ServerReflexive`, `TurnRelay`).
* **Adaptive Chunk Sizing**: Dynamic windowed scaling between 64 KB and 4 MB adapting to link capacity and packet loss.
* **Granular Device Permissions**: Auto-accept thresholds, scoped folder isolation, forced PIN prompts, and instant credential revocation.
* **Military-Grade Vault Backups**: Encrypted backup packages (`DZBK1`) utilizing PBKDF2 (100,000 iterations) and AES-256-GCM.

#### Phase 4: Mobile Applications (Android & iOS)
* **Android Subsystem (`DropZone.Android`)**:
  * Foreground service with `FOREGROUND_SERVICE_TYPE_DATA_SYNC` eliminating background Doze mode connection drops.
  * `PowerManager.WakeLock` (`PARTIAL_WAKE_LOCK`) and `WifiManager.MulticastLock` for mDNS discovery.
  * Scoped Storage and Storage Access Framework (SAF) integration.
* **iOS Subsystem (`DropZone.iOS`)**:
  * Sandboxed container integration with `UIDocumentPickerViewController`.
  * `UIApplication.IdleTimerDisabled` sleep prevention.
  * `NSLocalNetworkUsageDescription` and Bonjour services registration in `Info.plist`.
  * `BGTaskScheduler` background transfer execution.
* **Responsive Touch UI**: Shared `MainView.axaml` (`UserControl`) dynamically toggling between desktop sidebar navigation rail and mobile bottom navigation bar.

#### Phase 5: Pro & Enterprise Functionality
* **Recursive Folder Synchronization**: Continuous directory delta analysis, file status tracking (`Added`, `Modified`, `Deleted`, `Identical`), and configurable conflict resolution strategies (`SourceWins`, `TargetWins`, `NewerTimestampWins`, `LargerFileWins`).
* **Tamper-Evident Compliance Audit Ledger**: Cryptographic SHA-256 hash-chained immutable audit log satisfying SOC2, HIPAA, and ISO 27001 audit standards, with mathematical tamper detection.
* **Enterprise PKI Certificate Validation**: X.509 device identity verification, trusted root CA management, CRL revocation checks, and enterprise policy enforcement.
* **Bandwidth Throttling (QoS)**: Token-bucket rate limiting preventing network saturation on shared corporate links.

#### Phase 6: Final Quality Assurance & Release Validation
* **Full-Stack End-to-End Automated Testing**: 73 total automated tests (60 .NET integration tests + 13 Rust core engine unit tests), achieving 100% pass rate.
* **Zero Warning Compilation**: Strict compiler warning enforcement across C# (`TreatWarningsAsErrors`) and Rust (`#![deny(warnings)]`).
* **Complete Documentation Suite**: Architecture documentation, protocol specifications, API references, security guides, testing methodologies, installation instructions, and release notes.
