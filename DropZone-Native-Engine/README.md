# DROPZONE Native Engine (`dropzone_engine`)

The native Rust networking and cryptographic core of DROPZONE.

## Contents
- `dropzone_engine.dll`: Windows x64 compiled dynamic library (built in Release mode with Tokio, ChaCha20-Poly1305, X25519, STUN/TURN, mDNS).
- `dropzone_engine.h`: C-ABI function prototypes and event definitions for integration in C#, C/C++, Swift, or Kotlin via JNI.

## Linking
- **Windows**: Link against `dropzone_engine.dll` or load dynamically via `LoadLibraryW`.
- **Linux**: Build `libdropzone_engine.so` via `cargo build --release` in `engine/`.
- **macOS / iOS**: Build `libdropzone_engine.dylib` or static `libdropzone_engine.a`.
- **Android**: Build for NDK targets (`aarch64-linux-android`, `x86_64-linux-android`).
