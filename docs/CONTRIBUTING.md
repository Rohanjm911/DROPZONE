# DROPZONE – Contribution Guidelines

Thank you for contributing to DROPZONE.

## Core Architectural Principles

1. **Security by Default**: Never bypass input sanitization, checksums, or cryptographic checks. Never invent cryptographic primitives; use ChaCha20-Poly1305, Curve25519, HKDF-SHA256, and AES-256-GCM.
2. **Strict Layer Separation**: Maintain the 8 decoupled architectural layers. Never introduce UI dependencies into Domain, Application, or Infrastructure.
3. **No Web/Docker Bloat**: Do not introduce web frameworks (React, Vite, Node) or Docker into the core application architecture.
4. **Zero-Cloud & Zero-Knowledge**: Transfers must remain peer-to-peer without intermediate unencrypted cloud relays.
5. **Cross-Platform Compatibility**: Code must compile and run consistently across Windows, Linux, macOS, Android, and iOS. All OS-specific behaviors must be encapsulated within `IPlatformServices`.

---

## Coding Standards

### C# / .NET
* Target: .NET 11 / Modern C# 13 features (nullable reference types, pattern matching, file-scoped namespaces).
* MVVM: Use `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`).
* UI/UX: Avalonia UI 11.2 with solid color tokens; **never introduce gradients**.
* Error Handling: Explicit exceptions with descriptive logging; never swallow exceptions silently.
* Async: Use asynchronous non-blocking patterns (`async`/`await`, `CancellationToken`) throughout I/O pipelines.

### Rust Core Networking Engine
* Target: Rust 2021 edition.
* Networking: Tokio multi-threaded asynchronous runtime.
* C-ABI Stability: Maintain the unmanaged FFI interface in `engine/src/ffi.rs`. All exposed functions must return explicit error codes (`0 = Success`).
* Memory Safety: Zero unsafe blocks outside of FFI boundaries.

---

## Commit Guidelines

Follow the Conventional Commits specification:
* `feat(...)`: A new feature (e.g., `feat(phase-5): implement folder sync delta engine`)
* `fix(...)`: A bug fix
* `docs(...)`: Documentation changes
* `test(...)`: Adding or updating tests
* `refactor(...)`: Code change that neither fixes a bug nor adds a feature

---

## Testing & Quality Assurance

Before submitting a Pull Request:
1. Run Rust engine unit tests:
   ```bash
   cd engine && cargo test --offline
   ```
2. Run C# unit and integration tests:
   ```bash
   dotnet test tests/DropZone.UnitTests/DropZone.UnitTests.csproj
   ```
3. Ensure the solution builds with zero warnings and zero errors.
