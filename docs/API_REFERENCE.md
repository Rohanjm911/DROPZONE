# DROPZONE – C-ABI FFI & Managed Engine API Reference

## 1. Native C-ABI Interface (`dropzone_engine`)

The Rust engine exposes the following `extern "C"` functions for runtime control, connection management, and transfer operations:

### 1.1 Lifecycle & Configuration

```c
// Initializes the DropZone native engine with runtime configuration.
// Returns 0 on success, or a negative error code.
int32_t dropzone_engine_init(const char* config_json);

// Registers the event callback function pointer.
int32_t dropzone_engine_set_event_callback(
    void (*callback)(int32_t event_type, const char* payload_json, void* user_data),
    void* user_data
);

// Gracefully shuts down listeners, active transfer connections, and Tokio runtime.
int32_t dropzone_engine_shutdown(void);

// Retrieves the last error message recorded on the calling thread.
const char* dropzone_engine_get_last_error(void);
```

### 1.2 Networking & Server Operations

```c
// Starts the local TCP server listener on the specified IP and port (0 for auto).
// Emits DZ_EVENT_SERVER_STARTED event with the bound port.
int32_t dropzone_engine_start_server(const char* bind_ip, uint16_t port);

// Stops the listening server socket.
int32_t dropzone_engine_stop_server(void);

// Initiates local peer discovery (UDP broadcast / mDNS).
int32_t dropzone_engine_start_discovery(const char* device_name, uint16_t server_port);

// Stops peer discovery broadcast and listener.
int32_t dropzone_engine_stop_discovery(void);
```

### 1.3 Transfer Controls

```c
// Connects to a remote peer and initiates a transfer job with manifest metadata.
int32_t dropzone_engine_send_job(
    const char* peer_ip,
    uint16_t peer_port,
    const char* manifest_json
);

// Approves an incoming transfer job, supplying a resume byte offset (0 for start).
int32_t dropzone_engine_accept_job(const char* job_id, uint64_t resume_offset);

// Rejects an incoming transfer request.
int32_t dropzone_engine_reject_job(const char* job_id, const char* reason);

// Pauses an active transfer.
int32_t dropzone_engine_pause_job(const char* job_id);

// Resumes a paused transfer.
int32_t dropzone_engine_resume_job(const char* job_id);

// Cancels an ongoing transfer and cleans up connection resources.
int32_t dropzone_engine_cancel_job(const char* job_id);

// Generates an X25519 identity keypair and returns JSON { "public_key": "...", "fingerprint": "..." }.
int32_t dropzone_engine_generate_identity_key(char* out_json, size_t max_len);

// Initiates ephemeral X25519 pairing with a remote peer.
int32_t dropzone_engine_start_pairing(const char* peer_ip, uint16_t peer_port);

// Confirms or rejects an active SAS 6-digit PIN pairing verification.
int32_t dropzone_engine_confirm_pairing(const char* peer_device_id, const char* pin_code, bool is_accepted);

// Asynchronously queries an RFC 5389 STUN server to discover reflexive public IP/port candidate.
int32_t dropzone_engine_test_stun(const char* stun_server, char* out_json, size_t max_len);

// Gathers host and STUN reflexive candidates, emitting DZ_EVENT_NAT_CANDIDATE_DISCOVERED events.
int32_t dropzone_engine_gather_nat_candidates(const char* stun_server);

// Sends an RFC-compliant diagnostic ping probe to measure RTT latency.
int32_t dropzone_engine_ping_peer(const char* peer_ip, uint16_t peer_port);
```

---

## 2. Event Types & JSON Payloads

Delivered across `dropzone_engine_set_event_callback`:

| Code | Constant | Payload Description |
|---|---|---|
| `1` | `DZ_EVENT_SERVER_STARTED` | `{"ip": "0.0.0.0", "port": 52490}` |
| `2` | `DZ_EVENT_PEER_DISCOVERED` | `{"device_id": "...", "name": "...", "ip": "...", "port": 52490}` |
| `10`| `DZ_EVENT_TRANSFER_REQUEST` | Incoming manifest JSON requiring recipient approval |
| `11`| `DZ_EVENT_TRANSFER_ACCEPTED` | `{"job_id": "...", "resume_offset": 0}` |
| `12`| `DZ_EVENT_TRANSFER_REJECTED` | `{"job_id": "...", "reason": "Declined by user"}` |
| `20`| `DZ_EVENT_PROGRESS` | `{"job_id": "...", "file_index": 0, "bytes_transferred": 1048576, "total_bytes": 10485760, "speed_bps": 25000000, "eta_seconds": 3}` |
| `25`| `DZ_EVENT_TELEMETRY` | `{"job_id": "...", "rtt_ms": 12, "speed_bytes_per_sec": 45000000, "chunk_size": 1048576, "route": "direct_lan", "packet_loss_pct": 0.0}` |
| `30`| `DZ_EVENT_PAUSED` | `{"job_id": "..."}` |
| `31`| `DZ_EVENT_RESUMED` | `{"job_id": "...", "resumed_at_offset": 5242880}` |
| `32`| `DZ_EVENT_CANCELLED` | `{"job_id": "...", "reason": "User cancelled"}` |
| `40`| `DZ_EVENT_COMPLETED` | `{"job_id": "...", "verified_sha256": true}` |
| `50`| `DZ_EVENT_PAIRING_REQUEST` | Incoming pairing request from remote peer |
| `51`| `DZ_EVENT_PAIRING_PIN_DISPLAY` | `{"peer_device_id": "...", "peer_device_name": "...", "pin_code": "482915", "fingerprint": "...", "is_initiator": false}` |
| `52`| `DZ_EVENT_PAIRING_COMPLETED` | `{"peer_device_id": "...", "fingerprint": "...", "success": true, "message": "..."}` |
| `53`| `DZ_EVENT_PAIRING_FAILED` | `{"peer_device_id": "...", "success": false, "message": "..."}` |
| `60`| `DZ_EVENT_NAT_CANDIDATE_DISCOVERED` | `{"candidate_type": "server_reflexive", "ip": "203.0.113.1", "port": 54320, "priority": 16777216}` |
| `99`| `DZ_EVENT_ERROR` | `{"job_id": "...", "error_code": -5, "message": "Connection reset"}` |

---

## 3. Managed C# Engine Interface (`IDropZoneEngine`)

In `DropZone.Domain` / `DropZone.Application`:

```csharp
public interface IDropZoneEngine : IAsyncDisposable
{
    Task InitializeAsync(EngineConfig config, CancellationToken ct = default);
    Task StartServerAsync(string bindAddress, ushort port, CancellationToken ct = default);
    Task StopServerAsync(CancellationToken ct = default);
    Task StartDiscoveryAsync(string deviceName, ushort port, CancellationToken ct = default);
    Task StopDiscoveryAsync(CancellationToken ct = default);
    Task SendJobAsync(string peerIp, ushort peerPort, TransferManifest manifest, CancellationToken ct = default);
    Task AcceptJobAsync(Guid jobId, ulong resumeOffset, CancellationToken ct = default);
    Task RejectJobAsync(Guid jobId, string reason, CancellationToken ct = default);
    Task PauseJobAsync(Guid jobId, CancellationToken ct = default);
    Task ResumeJobAsync(Guid jobId, CancellationToken ct = default);
    Task CancelJobAsync(Guid jobId, CancellationToken ct = default);
    Task<IdentityKeyPair> GenerateIdentityKeyAsync(CancellationToken ct = default);
    Task StartPairingAsync(string peerIp, ushort peerPort, CancellationToken ct = default);
    Task<bool> ConfirmPairingAsync(string peerDeviceId, string pinCode, bool isAccepted, CancellationToken ct = default);
    Task<NatCandidate?> TestStunAsync(string stunServer, CancellationToken ct = default);
    Task GatherNatCandidatesAsync(string stunServer, CancellationToken ct = default);

    event EventHandler<EngineEvent>? EventReceived;
}
```

