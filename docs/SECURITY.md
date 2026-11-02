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
* **Cipher**: `ChaCha20-Poly1305` (RFC 8439).
* **Key Agreement**: Diffie-Hellman over Curve25519 (`X25519`, RFC 7748).
* **Key Derivation (RFC 5869 HKDF-SHA256)**:
  * `PRK = HKDF-Extract(salt = sender_salt ^ receiver_salt, IKM = X25519_Shared_Secret)`
  * `OKM = HKDF-Expand(PRK, info = "dropzone-v1-aead-session-keys", L = 64 bytes)`
  * Provides 32 bytes forward encryption key + 32 bytes reverse encryption key for bidirectional channel isolation.
* **Short Authentication String (SAS 6-Digit PIN)**:
  * `SAS_Bytes = HKDF-Expand(PRK, info = "dropzone-v1-sas-code", L = 4 bytes)`
  * `PIN = (u32_be(SAS_Bytes) % 900,000) + 100,000` (Guarantees uniform 6-digit confirmation PIN: `100000` - `999999`).
* **Replay Protection**: Monotonically increasing 64-bit sequence numbers packed into the 96-bit nonce (`nonce[4..12] = seq.to_be_bytes()`).

### 2.2 Device Authentication & Pairing
* **Identity Keys**: Each DROPZONE instance generates a persistent X25519 keypair on first run.
* **Pairing Flow**:
  1. Initiator generates ephemeral X25519 keypair and 32-byte cryptographically secure random salt, transmitting `PairingRequest` frame.
  2. Responder generates ephemeral keypair and salt, computes shared secret and derives the 6-digit SAS PIN code, transmitting `PairingResponse`.
  3. Both endpoints display the exact matching 6-digit PIN and SHA-256 fingerprint for out-of-band visual comparison.
  4. On mutual confirmation, the peer's public key and fingerprint are stored as `Trusted` in the SQLite database. Future transfers between paired devices bypass manual PIN entry while generating unique ephemeral session keys for Perfect Forward Secrecy (PFS).
  5. Users can revoke device trust at any time, instantly invalidating future transfer requests.

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
* **Heuristic Threat Detection**:
  * **Unicode Override Spoofing**: Rejection of Right-to-Left Override characters (`\u202E`, `\u202D`, `\u202B`) used to reverse displayed filenames (e.g., `document[U+202E]exe.pdf`).
  * **Disguised Double Extensions**: Detection of innocent extensions immediately followed by executable extensions (e.g. `report.pdf.exe`, `photo.png.scr`).
  * **Executable Extensions**: Flagging of high-risk binaries and script formats (`.exe`, `.bat`, `.cmd`, `.ps1`, `.vbs`, `.msi`, `.scr`, `.sh`, `.apk`, `.com`, `.pif`, `.hta`, `.cpl`, `.jar`, `.reg`).
* **Quarantine Vault**:
  * Suspicious files are segregated into `%LOCALAPPDATA%/DropZone/Quarantine/`.
  * Files are renamed to `<guid>.quarantine` and assigned `ReadOnly` attributes to block direct execution.
  * Companion `<guid>.meta.json` files record original relative path, source device ID, SHA-256 fingerprint, quarantine timestamp, and specific threat reason.
  * Users can inspect, permanently delete, or override and restore quarantined files from the UI.
* **Temporary Files**: Inbound chunks stream into `<filename>.<job_id>.dropzone_part` before atomic promotion upon SHA-256 validation.

### 3.4 Encrypted Backup & Audit Archive Security
* **Vault Envelope Format (`DZBK1`)**:
  * 5 bytes Magic: `DZBK1`
  * 16 bytes Cryptographic Salt (`RandomNumberGenerator.GetBytes(16)`)
  * 12 bytes AES-GCM Nonce (`RandomNumberGenerator.GetBytes(12)`)
  * 16 bytes Poly1305 / AES-GCM Authentication Tag
  * Variable length encrypted JSON payload
* **Key Derivation**: `PBKDF2-HMAC-SHA256` with 100,000 iterations over user-supplied master passphrase.
* **Integrity & Tamper Resistance**: Any bit modification to ciphertext, salt, or tag immediately triggers authentication failure (`CryptographicException`) without executing or deserializing untrusted data.
* **Granular Policy Enforcement**:
  * `MaxAutoAcceptBytes`: Inbound transfers exceeding this threshold always require explicit interactive confirmation, preventing silent disk filling.
  * `RequirePinVerification`: Forces fresh SAS 6-digit confirmation on every transfer for high-security endpoints.
  * Revocation timestamps immediately invalidate future transfer requests.

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

