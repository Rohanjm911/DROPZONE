# DROPZONE – Security Specification & Threat Model

## 1. Security Principles

DROPZONE adheres to the principle of **Defense-in-Depth** and **Zero-Trust Networking**:
* Never trust the local network (LANs are often hostile or shared).
* Never invent cryptographic primitives.
* Never execute received files automatically.
* Never permit unvalidated file paths.
* Never allow relay nodes to access plaintext data.

---

## 2. Cryptographic Architecture

### 2.1 Authenticated End-to-End Encryption (AEAD)
* **Cipher**: `ChaCha20-Poly1305` (RFC 8439) with fallback or hardware acceleration option for `AES-256-GCM`.
* **Key Derivation**: Ephemeral ECDH over Curve25519 (`X25519`) combined with static device identity keys (`Ed25519`) using HKDF-SHA256.
* **Replay Protection**: Monotonically increasing 64-bit chunk sequence numbers included in the AEAD Additional Authenticated Data (AAD).

### 2.2 Device Authentication & Pairing
* **Identity Keys**: Each DROPZONE instance generates a persistent Ed25519 keypair on first run, stored in platform-secure storage (Windows DPAPI / macOS Keychain / Linux SecretService).
* **Pairing Modes**:
  1. **Short Authentication String (SAS)**: Visual comparison of 4 dictionary words derived from session hash.
  2. **6-Digit PIN**: Dynamic single-use numeric code exchanged out-of-band.
  3. **QR Code**: Target device displays QR containing `{ device_id, public_key, session_token, ip, port }`. Initiator scans QR to authenticate.

---

## 3. Data Integrity & Content Safety

### 3.1 Dual-Layer SHA-256 Verification
1. **Chunk-Level**: Every chunk includes a SHA-256 checksum over the plaintext bytes. If a received chunk fails checksum verification, it is immediately discarded and re-requested.
2. **File-Level**: When all chunks are received, an end-to-end SHA-256 digest is computed over the entire assembled file and compared against the sender's signed manifest.

### 3.2 Path Traversal & File Injection Defense
* **Manifest Path Sanitization**:
  * All incoming file paths are treated as untrusted.
  * Paths containing `..`, absolute drive letters (`C:\`), or root slashes (`/`) are rejected outright.
  * Paths are normalized to relative paths and resolved strictly within the designated user download directory.
  * Filenames with control characters, null bytes, or reserved OS names (`CON`, `PRN`, `AUX`, `NUL`) are renamed or blocked.

### 3.3 Quarantine & Suspicious File Handling
* **Temporary Files**: Incoming transfers are written to `<filename>.<job_id>.dropzone_part`.
* **Executable Safety**: Executable files (`.exe`, `.bat`, `.cmd`, `.sh`, `.vbs`, `.ps1`, `.apk`, `.deb`) trigger explicit user confirmation before opening.
* **Windows Mark of the Web**: On Windows, incoming files receive the `Zone.Identifier` ADS (ZoneId=3, Internet) to ensure OS-level protection dialogs apply.
* **Malware Scanning Hook**: Pluggable integration point for Windows Defender (AMSI), ClamAV on Linux, or platform scanners before files are released from quarantine.

---

## 4. Threat Model & Mitigations

| Threat | Vector | Mitigation in DROPZONE |
|---|---|---|
| **Eavesdropping on LAN** | Packet capture on public Wi-Fi | All data payloads encrypted via ChaCha20-Poly1305. |
| **Man-in-the-Middle (MitM)** | ARP spoofing, rogue DNS | Cryptographic device pairing (SAS / PIN / QR) pins public keys. |
| **Relay Snooping (TURN)** | Untrusted relay operator | Relays only receive encrypted opaque envelopes; zero plaintext access. |
| **Directory Traversal** | Malicious sender path `../../system32` | Canonical relative path validation against base download sandbox. |
| **Partial File Tampering** | Bit-flipping attacks on transit | Poly1305 AEAD tag authentication + SHA-256 chunk & file hash checks. |
| **Drive Filling / Denial of Service** | Gigantic transfer payload | Explicit transfer approval requirement; disk space pre-flight check. |

