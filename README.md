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
3. **Phase 3: Analytics & Device Management** (Next Phase)
   * Real-time network telemetry, ping, and transfer speed diagnostics charts.
   * Detailed device permission management and capability flags.
   * Encrypted local backup and export of transfer history.
4. **Phase 4: Mobile Applications (Android & iOS)**
   * Avalonia mobile adaptations, scoped storage handling, background transfer services.
5. **Phase 5: Pro & Enterprise Functionality**
   * Multi-interface aggregation, folder sync, enterprise credential management.
6. **Phase 6: Quality Assurance & Stable Release**
   * Security audits, stress testing, fuzzing, cross-platform packaging.

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

