# DROPZONE – Installation & Deployment Guide

## System Requirements

| Platform | Minimum OS Version | Architecture | Required Runtimes |
|---|---|---|---|
| **Windows** | Windows 10 (1809+) / Windows 11 | x64, ARM64 | .NET 8.0 / 9.0 / 11.0 runtime |
| **Linux** | Ubuntu 22.04+, Fedora 38+, Debian 12+ | x86_64, aarch64 | glibc 2.31+, OpenSSL 3.0+ |
| **macOS** | macOS Monterey (12.0+) | Apple Silicon (M1+), Intel x86_64 | Native Universal2 |
| **Android** | Android 8.0 (API 26+) up to Android 15 | arm64-v8a, armeabi-v7a, x86_64 | Avalonia.Android host |
| **iOS** | iOS 15.0+ / iPadOS 15.0+ | arm64 | Avalonia.iOS host |

---

## Building from Source

### 1. Prerequisites
* [.NET SDK](https://dotnet.microsoft.com/) 8.0, 9.0, or 11.0 (preview)
* [Rust toolchain](https://rustup.rs/) (edition 2021, cargo 1.80+)
* Standard C build tools (MSVC on Windows, GCC/Clang on Linux/macOS)

### 2. Build the Core Networking Engine (Rust)
```bash
cd engine
cargo build --release
```
Artifact generated:
* Windows: `engine/target/release/dropzone_engine.dll`
* Linux: `engine/target/release/libdropzone_engine.so`
* macOS: `engine/target/release/libdropzone_engine.dylib`

### 3. Build the Desktop Application (C# / Avalonia)
```bash
cd src
dotnet build DropZone.slnx -c Release
```

### 4. Running the Application
```bash
dotnet run --project src/DropZone.UI/DropZone.UI.csproj -c Release
```

---

## Network Firewall Configuration

DROPZONE uses the following ports for peer discovery and secure binary streaming:

| Port | Protocol | Purpose | Scope |
|---|---|---|---|
| `52490` | TCP | Encrypted Direct LAN & Remote Transfers | Inbound / Outbound |
| `5353` | UDP | Multicast DNS (mDNS) Discovery (`_dropzone._tcp`) | Local Subnet (Multicast 224.0.0.251) |
| `19302` | UDP | STUN Binding Requests (NAT Reflexive Candidate Discovery) | Outbound to Public STUN |
| `3478` | UDP/TCP | TURN Blind Relay Fallback | Outbound to Configured Relay |

---

## Deployment & Packaging Options

* **Windows MSI / Self-Contained Executable**:
  ```bash
  dotnet publish src/DropZone.UI/DropZone.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
  ```
* **Linux AppImage / Tarball**:
  ```bash
  dotnet publish src/DropZone.UI/DropZone.UI.csproj -c Release -r linux-x64 --self-contained true
  ```
* **macOS App Bundle**:
  ```bash
  dotnet publish src/DropZone.UI/DropZone.UI.csproj -c Release -r osx-arm64 --self-contained true
  ```
