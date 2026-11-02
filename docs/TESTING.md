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
$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; $env:MSBUILDUSESERVER="0"; dotnet test tests/DropZone.UnitTests/DropZone.UnitTests.csproj --no-build
```

---

## 3. Test Suite Inventory

### Rust Engine Tests (13 Tests – All Passing)
* `adaptive::tests::test_adaptive_scaling_up_on_fast_lan` – Dynamic chunk size expansion (up to 4MB) for gigabit/10GbE low-latency connections.
* `adaptive::tests::test_adaptive_scaling_down_on_lossy_wan` – Dynamic chunk size downscaling (to 256KB-512KB) for high-latency lossy WAN links.
* `nat::stun::tests::test_stun_binding_request_and_response_parsing` – RFC 5389 STUN Binding Request building and XOR-MAPPED-ADDRESS parsing.
* `nat::turn::tests::test_turn_allocate_request_building` – RFC 5766 TURN allocation framing.
* `nat::turn::tests::test_turn_channel_data_roundtrip` – RFC 5766 ChannelData encapsulation for blind relaying.
* `crypto::tests::test_hmac_sha256_rfc4231_case2` – RFC 2104 / RFC 4231 HMAC-SHA256 test vector validation.
* `crypto::tests::test_hkdf_expand_and_extract` – RFC 5869 HKDF-SHA256 PRK and OKM derivation.
* `crypto::tests::test_x25519_key_agreement_and_sas` – Curve25519 DH shared secret agreement, bidirectional session key isolation, and 6-digit SAS numeric PIN match.
* `crypto::tests::test_sha256` – SHA-256 standard test vector.
* `crypto::tests::test_aead_roundtrip` – ChaCha20-Poly1305 encryption/decryption with sequence nonces.
* `pairing::tests::test_pairing_manager_flow` – End-to-end pairing state machine (request, response, SAS match, user confirmation).
* `protocol::tests::test_header_serialization` – 16-byte `DZP1` fixed header serialization/deserialization.
* `protocol::tests::test_chunk_descriptor_serialization` – 64-byte chunk descriptor framing.

### .NET Tests (51 Tests – All Passing)
* **Mobile Platforms (Android & iOS)**:
  * `AndroidPlatformService`: Target identification (`PlatformType.Android`), mobile flag assertion, scoped storage directory layout (`Downloads`, `AppData`, `Cache`, `Quarantine`).
  * `AndroidWakeLockService`: CPU wake lock lifecycle management (`PARTIAL_WAKE_LOCK`), Wi-Fi multicast lock acquisition for mDNS broadcast discovery.
  * `AndroidNotificationService`: Foreground transfer progress notification, silent channel formatting (`Low`), transfer completion alerts, and security alert dispatch.
  * `AndroidForegroundTransferService`: Background foreground service orchestration, reference-counted concurrent transfers, and automatic sleep lock release.
  * `IOSPlatformService`: Target identification (`PlatformType.IOS`), sandboxed application documents path routing, device model and OS metadata.
  * `IOSWakeLockService`: Idle timer disable/restore toggling (`UIApplication.IdleTimerDisabled`) for uninterrupted file transfers.
  * `IOSNotificationService`: Notification dispatching, badge count synchronization, and completion handling.
  * `DropZoneBackgroundTransferManager`: Background processing task scheduler (`BGProcessingTask`) lifecycle coordination.
  * `DesktopPlatformServices`: Desktop platform identification, Windows thread execution state (`SetThreadExecutionState`), notifications, and lifecycle state management.
  * `TransferOrchestratorMobilePlatformIntegration`: Orchestrator coordination with wake lock service, background task registration, and notification updates throughout outbound and inbound transfer lifecycles.
* **Domain & Queue**:
  * Manifest generation, FileItem model integrity, Job model state transitions.
  * TransferQueue slot concurrency limit enforcement, FIFO priority order, item lookup.
  * Windowed EMA transfer speed estimator, ceiling ETA calculation, state reset.
* **Security & Quarantine**:
  * Path traversal sanitization (rejection of `..`, drive letters, control characters, null bytes).
  * Windows DOS device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-4`, `LPT1-3`).
  * Heuristic analysis: RTLO Unicode override spoofing (`\u202E`).
  * Heuristic analysis: Disguised double extensions (`.pdf.exe`, `.jpg.vbs`).
  * Quarantine lifecycle: isolation into `%LOCALAPPDATA%/DropZone/Quarantine/`, read-only attribution, `.meta.json` tracking, safe restoration, permanent deletion.
* **Database & Device Permissions**:
  * SQLite WAL mode initialization, job creation, progress tracking, status transitions.
  * Device repository upsert, trust level promotion (`Trusted`), revocation (`Prompt`), and blocking (`Blocked`).
  * Granular `DevicePermissions`: auto-accept boolean toggles, byte thresholds (100MB / 50MB), custom download directories, and revocation timestamps.
* **Encrypted Vault Backup**:
  * Encrypted backup generation with user passphrase (PBKDF2 100,000 iterations + AES-256-GCM authenticated envelope `DZBK1`).
  * Full roundtrip restoration of paired devices, keys, and transfer audit history.
  * Authentication tag verification failure and tamper resistance against corrupted ciphertexts or wrong passphrases.
* **Interop & Native Bridge**:
  * Pinned unmanaged callback registration, asynchronous event dispatching.
  * Job pause, resume, cancel dispatch verification.
  * Native X25519 identity key generation and fingerprint extraction.

