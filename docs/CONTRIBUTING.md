# DROPZONE – Contribution Guidelines

Thank you for contributing to DROPZONE.

## Core Rules

1. **Security First**: Never bypass input sanitization, checksums, or cryptographic checks.
2. **Architecture Integrity**: Strictly respect the 8 layers. Do not add UI dependencies into Domain or Infrastructure.
3. **No Third-Party Bloat**: Do not introduce web frameworks (React, Vite) or Docker into the core architecture.
4. **Testing Mandatory**: Every new domain feature or network packet type must include automated tests.
5. **Cross-Platform**: All code must run on Windows, Linux, and macOS without platform-specific hardcoding unless encapsulated in `IPlatformService`.

