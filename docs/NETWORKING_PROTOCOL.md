# DROPZONE – Binary Framing & Networking Protocol Specification

## 1. Overview

DROPZONE uses a compact binary framed protocol over TCP/TLS for direct LAN transfers and WebRTC/TURN data channels for NAT-traversed connections.

Default LAN port: `52490` (TCP for data stream, UDP for local mDNS/beacon discovery).

---

## 2. Binary Frame Format

All frames sent over a DROPZONE connection begin with a 16-byte fixed-length header:

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|       Magic (0x44 0x5A 0x50 0x31 -> "DZP1")                   |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|          Version (16-bit)     |       Frame Type (16-bit)     |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                       Sequence Number (32-bit)                |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                       Payload Length (32-bit)                 |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                       Payload Bytes (Variable)                |
|                              ...                              |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

### Fields:
* **Magic** (`4 bytes`): `0x44, 0x5A, 0x50, 0x31` ("DZP1" - DropZone Protocol v1).
* **Version** (`2 bytes`, Big-Endian): Protocol version (current `0x0001`).
* **Frame Type** (`2 bytes`, Big-Endian):
  * `0x0001` : `HANDSHAKE_INIT`
  * `0x0002` : `HANDSHAKE_ACK`
  * `0x0003` : `PAIRING_REQUEST` (Outbound device pairing payload)
  * `0x0004` : `PAIRING_RESPONSE` (Inbound pairing response + DH parameters)
  * `0x0005` : `PAIRING_CONFIRM` (User verified SAS PIN match)
  * `0x0006` : `PAIRING_REJECT` (User declined or PIN mismatch)
  * `0x0008` : `CANDIDATE_EXCHANGE` (ICE host/srflx/relay endpoints)
  * `0x000A` : `PING` (Diagnostic round-trip latency probe)
  * `0x000B` : `PONG` (Immediate echo of ping timestamp/sequence)
  * `0x0010` : `TRANSFER_REQUEST` (Manifest metadata in JSON)
  * `0x0011` : `TRANSFER_ACCEPT` (Approved with resume byte offset)
  * `0x0012` : `TRANSFER_REJECT` (Declined by recipient or policy)
  * `0x0020` : `CHUNK_DATA` (Binary payload chunk with subheader)
  * `0x0021` : `CHUNK_ACK` (Acknowledges received chunk offset)
  * `0x0030` : `TRANSFER_PAUSE`
  * `0x0031` : `TRANSFER_RESUME`
  * `0x0032` : `TRANSFER_CANCEL`
  * `0x0040` : `TRANSFER_COMPLETE` (Final verification match)
  * `0x00FF` : `ERROR_NOTIFICATION`
* **Sequence Number** (`4 bytes`): Monotonically increasing frame counter per connection.
* **Payload Length** (`4 bytes`, Big-Endian): Size of following payload in bytes (0 to 16,777,216 / 16MB max).

---

## 3. Chunk Data Sub-Header

When `Frame Type == 0x0020` (`CHUNK_DATA`), the payload begins with a 48-byte chunk descriptor:

```
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                     Job ID (UUID 16 bytes)                    |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                     File Index (32-bit)                       |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
+                     Byte Offset (64-bit)                      +
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                     Chunk Data Size (32-bit)                  |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                     Chunk SHA-256 Checksum                    |
|                           (32 bytes)                          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                     Raw Chunk Data Bytes                      |
|                              ...                              |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

### Chunk Sizing:
* Standard LAN chunk size: **1,048,576 bytes** (1 MB) for high throughput.
* Constrained / mobile chunk size: **65,536 bytes** (64 KB).
* Adaptive chunking adjusts based on measured packet loss and round-trip time.

---

## 4. Manifest Format (JSON Payload)

Sent during `TRANSFER_REQUEST`:

```json
{
  "job_id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "sender_device_id": "dz-dev-91829",
  "sender_device_name": "Rohan's Workstation",
  "total_bytes": 104857600,
  "file_count": 2,
  "files": [
    {
      "index": 0,
      "relative_path": "Photos/Vacation.zip",
      "size_bytes": 104857600,
      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
      "mime_type": "application/zip"
    }
  ],
  "timestamp": 1730592000
}
```

---

## 5. Resumption Semantics

When resuming an interrupted transfer:
1. Sender issues `TRANSFER_REQUEST` with same `job_id` and file metadata.
2. Receiver inspects its local temporary download (`.dropzone_part`).
3. Receiver verifies existing contiguous valid chunks using SHA-256 validation.
4. Receiver replies with `TRANSFER_ACCEPT` containing `resume_offset`:
   ```json
   {
     "job_id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
     "accepted": true,
     "resume_offset": 52428800
   }
   ```
5. Sender seeks immediately to byte `52428800` and continues streaming without re-sending completed chunks.

---

## 6. Remote P2P & NAT Traversal (STUN & TURN)

### 6.1 STUN NAT Reflexive Candidate Discovery (RFC 5389)
* **Binding Request**: Sent to configured STUN server (default: `stun.l.google.com:19302`) via UDP.
* **Header Structure**:
  * Message Type: `0x0001` (Binding Request), `0x0101` (Binding Success Response)
  * Magic Cookie: `0x2112A442`
  * Transaction ID: 12 bytes cryptographically random
* **XOR-MAPPED-ADDRESS Attribute (`0x0020`)**:
  * `X-Port`: Port ^ (`0x2112A442 >> 16`)
  * `X-Address`: IPv4 ^ `0x2112A442` or IPv6 ^ (`0x2112A442` || `Transaction_ID`)
* Discovered reflexive address forms the `server_reflexive` ICE candidate for direct UDP hole punching.

### 6.2 TURN Relay Fallback (RFC 5766)
* When direct LAN and STUN reflexive hole punching fail due to symmetric NAT or firewall rules, DROPZONE initiates TURN allocation (`0x0003`).
* **Cryptographic Blindness**:
  * All payload data forwarded through TURN relays consists of raw encrypted `DZP1` frames (ChaCha20-Poly1305 with peer session keys).
  * Relays only see opaque byte envelopes. Zero plaintext file or manifest data is accessible to intermediate relays.

---

## 7. Adaptive Chunk Sizing & Telemetry

DROPZONE implements dynamic, link-aware chunk sizing to optimize throughput across diverse network topologies:

* **Base Default**: `1,048,576` bytes (1.0 MB).
* **Scaling Rules**:
  * **Ultra-Fast LAN (10GbE / Fast Wi-Fi 6, RTT < 5ms, Speed > 100 MB/s)**: Chunk size dynamically expands to `4,194,304` bytes (4.0 MB) to maximize NIC saturation and eliminate kernel framing overhead.
  * **Standard LAN (Gigabit, RTT < 25ms, Speed > 30 MB/s)**: Chunk size scales to `2,097,152` bytes (2.0 MB).
  * **High-Latency or Lossy WAN (RTT > 150ms or Retry Rate > 2%)**: Chunk size downscales to `262,144` bytes (256 KB) or `524_288` bytes (512 KB) to prevent costly packet retransmission penalties.
* **Bounds**: Minimum `64 KB`, Maximum `8 MB`.



