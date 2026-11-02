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

