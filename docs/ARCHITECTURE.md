# DROPZONE – System Architecture Specification

## 1. Architectural Principles

DROPZONE is built around the following foundational principles:

1. **Zero-Knowledge / Zero-Cloud Storage**: Files travel directly between source and target devices. Intermediate servers (signaling/TURN) only route encrypted packets and never store or decrypt payloads.
2. **Layer Separation & Modularity**: A strict 8-layer separation ensures that presentation, domain logic, persistence, and low-level networking can evolve and be tested independently.
3. **Memory Safety & High-Throughput**: The core network transport engine is written in Rust using Tokio, delivering predictable performance, asynchronous non-blocking I/O, and memory safety without garbage-collection pauses.
4. **Resilience & Fault Tolerance**: Transfers are chunk-based and idempotent. Network interruptions trigger exponential backoff retry and chunk-level resume rather than full-file retransmissions.
5. **Security by Default**: All transfers require explicit or pre-approved device trust, authenticated end-to-end encryption (AEAD), and cryptographic hash verification.

---

## 2. The 8 Architectural Layers

```
+--------------------------------------------------------------------------+
| 1. Presentation Layer (DropZone.UI)                                      |
|    - Avalonia UI (Cross-platform desktop & mobile rendering)             |
|    - MVVM Pattern (CommunityToolkit.Mvvm)                                |
|    - Views: TransfersView, DiscoveryView, SettingsView, Dialogs          |
|    - ViewModels: MainViewModel, TransferQueueViewModel, DeviceViewModel |
|    - Reusable UI Components, Dark/Light Themes, Zero Gradients           |
+--------------------------------------------------------------------------+
                                     │
+------------------------------------▼-------------------------------------+
| 2. Application Layer (DropZone.Application)                              |
|    - Transfer Orchestrator (Queueing, Concurrency, Throttling)           |
|    - Speed & ETA Estimator (Moving average window)                       |
|    - Device Discovery Coordinator                                        |
|    - Pairing & Trust Workflows                                           |
|    - Application Notification Bus                                        |
+--------------------------------------------------------------------------+
                  │                                        │
+-----------------▼----------------------+ +---------------▼---------------+
| 3. Domain Layer (DropZone.Domain)      | | 6. Security Engine            |
|    - Entities: TransferJob, Device,    | |    (DropZone.Security / Rust) |
|      TransferManifest, FileItem        | |    - ChaCha20-Poly1305 AEAD   |
|    - Value Objects: ChunkOffset,       | |    - SHA-256 Checksums        |
|      SecurityFingerprint, TransferRate | |    - Path Traversal Guard     |
|    - Enums: TransferState,             | |    - Quarantine Isolation     |
|      ConnectionMode, DeviceTrustLevel  | |    - Key Derivation (HKDF)    |
|    - Interfaces: ITransferEngine,      | +-------------------------------+
|      ITransferRepository, IFileSystem  |
+----------------------------------------+
                  │                                        │
+-----------------▼----------------------------------------▼---------------+
| 4. Infrastructure Layer (DropZone.Infrastructure)                        |
|    - SQLite Persistence (Transfer history, paired peers, config)        |
|    - Local File System Access (Streaming chunks, sparse allocation)     |
|    - Structured Logging (Microsoft.Extensions.Logging)                   |
|    - Settings & Configuration Repository                                 |
+--------------------------------------------------------------------------+
                  │
+-----------------▼--------------------------------------------------------+
| 8. Platform Integration Layer (DropZone.Platform)                        |
|    - Windows: Shell notification, Taskbar progress, Power sleep locks    |
|    - Linux: FreeDesktop notifications, DBus integration                 |
|    - macOS: Apple User Notifications, AppNap suspension prevention       |
+--------------------------------------------------------------------------+
                  │ C-ABI P/Invoke Native Interface
+-----------------▼--------------------------------------------------------+
| 5. Native Networking Engine (dropzone_engine - Rust/Tokio)               |
|    - Tokio Async Multi-threaded Runtime                                  |
|    - Binary Framed Transport Protocol                                    |
|    - Local Discovery (mDNS / UDP Broadcast)                              |
|    - Direct TCP / TLS Socket Listeners & Connectors                      |
|    - Chunk Multiplexing, Flow Control & Backpressure                     |
|    - Event Callback Dispatch to Managed C# Runtime                       |
+--------------------------------------------------------------------------+
```

---

## 3. C# / Rust Interoperability (FFI)

The managed C# application and the native Rust engine communicate across a strictly typed C-ABI interface.

### Memory & Threading Boundaries
* **String Passing**: UTF-8 null-terminated pointers (`*const c_char`). Managed strings are passed using `Marshal.StringToCoTaskMemUTF8` or standard P/Invoke UTF-8 marshaling.
* **Buffers**: Raw byte buffers are allocated by the caller or passed as `(data_ptr: *const u8, len: usize)`.
* **Events**: Asynchronous notifications from Rust to C# are dispatched via a function pointer delegate:
  ```rust
  pub type EventCallback = extern "C" fn(event_type: i32, payload_json: *const c_char, user_data: *mut c_void);
  ```
  The C# runtime registers an unmanaged function pointer delegate decorated with `[UnmanagedCallersOnly]` or a pinned static delegate instance.
* **Error Handling**: FFI functions return an integer status code (`0 = Success`, negative = error code). A thread-local error string can be fetched via `dropzone_get_last_error()`.

---

## 4. Transfer Lifecycle State Machine

```
[Created] ──► [Queued] ──► [Connecting/Handshake] ──► [Transferring] ──► [Verifying] ──► [Completed]
                               │                             │
                               ▼                             ▼
                            [Failed]                     [Paused]
                               │                             │
                               ▼                             ▼
                            [Retry]                     [Resuming]
                               │                             │
                               └─────────────────────────────┘
```

1. **Created**: User drops file(s) or folder onto a device target.
2. **Queued**: Manifest generated (file list, total bytes, chunk count, file SHA-256).
3. **Handshake**: Device session authenticated; target approves or auto-accepts transfer. Target verifies existing chunks for resume.
4. **Transferring**: Chunks streamed with framed header (job_id, file_index, chunk_offset, payload_len, chunk_sha256).
5. **Verifying**: Once all chunks arrive, target executes final full-file SHA-256 calculation.
6. **Completed**: File atomically renamed from `.dropzone_part` to destination filename; marked complete in SQLite database.

---

## 5. Storage & Persistence

* **Database**: Embedded SQLite (`Microsoft.Data.Sqlite`) in WAL (Write-Ahead Logging) mode.
* **Schemas**:
  * `transfer_jobs`: Tracks job status, peer ID, start/finish timestamps, total bytes, transferred bytes.
  * `transfer_files`: Files within a multi-file job, relative path, size, sha256, status.
  * `devices`: Discovered and paired peers, trust levels (`Blocked`, `Prompt`, `Trusted`), public identity key, last seen.
  * `settings`: Key-value application settings (download folder, device name, auto-accept, network port).

---

## 6. Mobile Platform Architecture (Android & iOS)

### Android Architecture (`DropZone.Android`)
* **Background Execution & Doze Mode**: Mobile kernels kill inactive background TCP sockets when the display sleeps. DropZone addresses this with `DropZoneTransferForegroundService` (`android:foregroundServiceType="dataSync"`), holding a `PowerManager.WakeLock` (`PARTIAL_WAKE_LOCK`) throughout active transfers.
* **Multicast Filtering Bypass**: Android kernels filter UDP multicast packets by default to preserve battery life. DropZone acquires `WifiManager.MulticastLock` during discovery to ensure incoming mDNS beacons are received reliably.
* **Scoped Storage (API 29+)**: Raw filesystem access to arbitrary directories is restricted. DropZone routes downloads through app-scoped sandboxed storage and integrates with the Android Storage Access Framework (SAF) and MediaStore.
* **Notification Channels**: Notifications are partitioned into `dropzone_channel_transfers` (Low importance, silent ongoing progress bars) and `dropzone_channel_alerts` (High importance, heads-up security warnings).

### iOS Architecture (`DropZone.iOS`)
* **Sandboxed Container**: iOS strictly isolates file access within the application's `Documents`, `Library/Caches`, and `tmp` directories. File selections outside the sandbox use `UIDocumentPickerViewController`.
* **Sleep & Auto-Lock Prevention**: During active file transfers, `UIApplication.SharedApplication.IdleTimerDisabled` is asserted by `IOSWakeLockService` to prevent premature socket termination.
* **Local Network Privacy (iOS 14+)**: DropZone configures `NSLocalNetworkUsageDescription` and registers `_dropzone._tcp` under `NSBonjourServices` in `Info.plist` to obtain explicit user permission before opening local peer sockets.
* **Background Tasks**: Multi-megabyte and multi-gigabyte transfers coordinate with `BGTaskScheduler` (`com.dropzone.transfer.background`) and `BGProcessingTask`.

### Responsive Cross-Platform UI Adaptation
* **Dual Lifetime Architecture**: The shared UI is encapsulated within `MainView.axaml` (`UserControl`). On desktop platforms, `MainWindow` hosts `MainView` within `IClassicDesktopStyleApplicationLifetime`. On mobile platforms, `MainView` is mounted directly into `ISingleViewApplicationLifetime`.
* **Adaptive Navigation**: Desktop renders a 200px Left Navigation Rail; mobile form factors automatically switch to a thumb-friendly Bottom Navigation Bar with large tap targets.
* **Mobile File Selection**: Since desktop drag-and-drop is unavailable on mobile operating systems, the Transfers view provides a dedicated "Pick Files to Send" touch action integrated with the platform storage picker.

---

## 7. Pro & Enterprise Architecture

### 7.1 Recursive Folder Synchronization
The folder synchronization subsystem (`FolderSyncService`) facilitates zero-cloud, direct directory synchronization:
* **Directory Tree Scanner**: Recursively inspects file attributes (relative path, byte size, last modified timestamp UTC, and streaming SHA-256).
* **Delta Diffing Engine**: Compares source and destination manifests, cataloging entries into `Added`, `Modified`, `Deleted`, and `Identical`.
* **Conflict Resolution Engine**: Deterministically resolves competing file versions based on user-defined policies: `SourceWins`, `TargetWins`, `NewerTimestampWins`, `LargerFileWins`, or `ManualReview`.
* **Atomic Part-File Swap**: Synchronized files are written to `.syncpart` temporary buffers before being atomically renamed, preventing half-written corruptions during crashes or power cuts.

### 7.2 Cryptographic Compliance Audit Ledger
Designed for enterprise regulatory compliance (SOC2, HIPAA, ISO 27001):
* **Tamper-Evident Hash Chain**: Each audit entry is cryptographically anchored to its predecessor:
  $$\text{EntryHash}_n = \text{SHA256}(\text{Seq}_n \parallel \text{Timestamp}_n \parallel \text{EventType}_n \parallel \text{Actor}_n \parallel \text{Target}_n \parallel \text{Details}_n \parallel \text{EntryHash}_{n-1})$$
* **Mathematical Verification**: `VerifyAuditChainIntegrityAsync` traverses the ledger sequentially, detecting any altered payload, deleted row, or inserted entry with 100% precision.
* **Compliance Reporting**: Exports full audit reports in signed JSON format including chain verification status.

### 7.3 Enterprise PKI & Certificate Validation
* **X.509 Certificate Validation**: Validates corporate fleet certificates against trusted Root CA stores (`EnterpriseCertificateValidator`).
* **Certificate Revocation**: In-memory and database Certificate Revocation Lists (CRL) prevent unauthorized communication from compromised or decommissioned devices.
* **Enterprise Security Policy**: Configures mandatory certificate enforcement, restrictions against unmanaged peer devices, and maximum allowed file limits.

### 7.4 Bandwidth Throttling & Quality of Service (QoS)
* **Token Bucket Algorithm**: `BandwidthThrottler` regulates network throughput with sub-millisecond precision, preventing link saturation on shared office or VPN connections.

