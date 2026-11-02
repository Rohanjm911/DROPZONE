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

1. **Phase 1: Core File Transfer** (Current)
   * Rust Tokio streaming engine (framing, chunking, backpressure).
   * C# FFI Interop bridge (`DropZoneEngineInterop`).
   * Domain model, transfer queue, pause/resume/cancel mechanics.
   * SQLite persistence for jobs and history.
   * Avalonia MVVM desktop interface with drag-and-drop file/folder sharing.
2. **Phase 2: Security & Advanced Networking**
   * ChaCha20-Poly1305 authenticated session channels.
   * Short Authentication String (SAS), 6-digit PIN, and QR-based device pairing.
   * STUN-based NAT traversal and TURN zero-knowledge encrypted relay fallback.
   * Zero-trust quarantine and path-traversal guardrails.
3. **Phase 3: Analytics & Device Management**
   * Real-time network telemetry and transfer speed diagnostics.
   * Device permission management (auto-accept, prompt, blocked).
   * Encrypted local backup and export of transfer history.
4. **Phase 4: Mobile Applications (Android & iOS)**
   * Avalonia mobile adaptations, scoped storage handling, foreground transfer services.
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

