using System.Text.Json;
using DropZone.Domain.Enums;
using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;

namespace DropZone.Application.Services;

public sealed class TransferOrchestrator : IAsyncDisposable
{
    private readonly IDropZoneEngine _engine;
    private readonly ITransferRepository _repository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IFileSystemStorage _storage;
    private readonly ISecurityService _security;
    private readonly TransferQueue _queue;
    private readonly Dictionary<Guid, TransferSpeedEstimator> _speedEstimators = new();
    private readonly Dictionary<Guid, int> _retryCounts = new();
    private readonly object _lock = new();

    public event Action<TransferJob>? JobUpdated;
    public event Action<TransferManifest>? InboundTransferRequested;
    public event Action<PairingPinInfo>? PairingPinDisplayRequested;
    public event Action<PairingResult>? PairingCompleted;
    public event Action<NatCandidate>? NatCandidateDiscovered;
    public event Action<TransferTelemetry>? TelemetryUpdated;

    public TransferOrchestrator(
        IDropZoneEngine engine,
        ITransferRepository repository,
        IDeviceRepository deviceRepository,
        IFileSystemStorage storage,
        ISecurityService security,
        TransferQueue? queue = null)
    {
        _engine = engine;
        _repository = repository;
        _deviceRepository = deviceRepository;
        _storage = storage;
        _security = security;
        _queue = queue ?? new TransferQueue();

        _engine.EventReceived += OnEngineEventReceived;
    }

    public async Task InitializeAsync(EngineConfig config, CancellationToken ct = default)
    {
        await _repository.InitializeAsync(ct);
        await _deviceRepository.InitializeAsync(ct);
        await _engine.InitializeAsync(config, ct);
        await _engine.StartServerAsync("0.0.0.0", config.ListenPort, ct);
        await _engine.StartDiscoveryAsync(config.DeviceName, config.ListenPort, ct);

        // Load recent jobs from DB
        var savedJobs = await _repository.GetAllJobsAsync(50, ct);
        foreach (var job in savedJobs)
        {
            if (job.State == TransferState.Transferring || job.State == TransferState.Connecting)
            {
                job.State = TransferState.Paused; // Mark previously active transfers as paused on launch
            }
            _queue.TryEnqueue(job);
        }
    }

    public async Task<TransferJob> StartOutboundTransferAsync(
        string sourcePath,
        Device targetDevice,
        string senderDeviceId,
        string senderDeviceName,
        CancellationToken ct = default)
    {
        var manifest = await _storage.BuildManifestAsync(sourcePath, senderDeviceId, senderDeviceName, ct);

        var job = new TransferJob
        {
            Id = manifest.JobId,
            PeerDeviceId = targetDevice.Id,
            PeerDeviceName = targetDevice.Name,
            PeerEndpoint = $"{targetDevice.IpAddress}:{targetDevice.Port}",
            Direction = TransferDirection.Outbound,
            State = TransferState.Pending,
            TotalBytes = manifest.TotalBytes,
            TransferredBytes = 0,
            Files = manifest.Files,
            CreatedAt = DateTime.UtcNow
        };

        _queue.TryEnqueue(job);
        await _repository.AddJobAsync(job, ct);
        JobUpdated?.Invoke(job);

        lock (_lock)
        {
            _speedEstimators[job.Id] = new TransferSpeedEstimator();
        }

        // Trigger transfer attempt
        _ = ProcessNextInQueueAsync(job, manifest, targetDevice);
        return job;
    }

    private async Task ProcessNextInQueueAsync(TransferJob job, TransferManifest manifest, Device targetDevice)
    {
        try
        {
            job.State = TransferState.Connecting;
            JobUpdated?.Invoke(job);
            await _repository.UpdateJobStateAsync(job.Id, job.State);

            await _engine.SendJobAsync(targetDevice.IpAddress, targetDevice.Port, manifest);
        }
        catch (Exception ex)
        {
            await HandleTransferFailureAsync(job.Id, ex.Message);
        }
    }

    public async Task AcceptInboundTransferAsync(Guid jobId, CancellationToken ct = default)
    {
        if (!_queue.TryGet(jobId, out var job) || job == null)
            return;

        // Check if we can resume partial transfer
        var firstFile = job.Files.FirstOrDefault();
        long resumeOffset = 0;
        if (firstFile != null)
        {
            resumeOffset = await _storage.GetExistingResumeOffsetAsync(
                job.Id, 
                firstFile.RelativePath, 
                firstFile.SizeBytes, 
                firstFile.Sha256, 
                ct);
        }

        job.State = TransferState.Transferring;
        job.TransferredBytes = resumeOffset;
        JobUpdated?.Invoke(job);
        await _repository.UpdateJobStateAsync(job.Id, job.State, ct: ct);

        await _engine.AcceptJobAsync(job.Id, (ulong)resumeOffset, ct);
    }

    public async Task RejectInboundTransferAsync(Guid jobId, string reason = "Declined by user", CancellationToken ct = default)
    {
        if (_queue.TryGet(jobId, out var job) && job != null)
        {
            job.State = TransferState.Cancelled;
            job.ErrorMessage = reason;
            JobUpdated?.Invoke(job);
            await _repository.UpdateJobStateAsync(job.Id, job.State, reason, ct);
        }

        await _engine.RejectJobAsync(jobId, reason, ct);
    }

    public async Task PauseTransferAsync(Guid jobId, CancellationToken ct = default)
    {
        if (_queue.TryGet(jobId, out var job) && job != null)
        {
            job.State = TransferState.Paused;
            job.SpeedBytesPerSec = 0;
            job.EtaSeconds = 0;
            JobUpdated?.Invoke(job);
            await _repository.UpdateJobStateAsync(job.Id, job.State, ct: ct);
        }

        await _engine.PauseJobAsync(jobId, ct);
    }

    public async Task ResumeTransferAsync(Guid jobId, CancellationToken ct = default)
    {
        if (_queue.TryGet(jobId, out var job) && job != null)
        {
            job.State = TransferState.Transferring;
            JobUpdated?.Invoke(job);
            await _repository.UpdateJobStateAsync(job.Id, job.State, ct: ct);
        }

        await _engine.ResumeJobAsync(jobId, ct);
    }

    public async Task CancelTransferAsync(Guid jobId, CancellationToken ct = default)
    {
        if (_queue.TryGet(jobId, out var job) && job != null)
        {
            job.State = TransferState.Cancelled;
            job.ErrorMessage = "Cancelled by user";
            JobUpdated?.Invoke(job);
            await _repository.UpdateJobStateAsync(job.Id, job.State, job.ErrorMessage, ct);
            await _storage.CleanPartFilesAsync(jobId, ct);
        }

        await _engine.CancelJobAsync(jobId, ct);
    }

    private void OnEngineEventReceived(object? sender, EngineEvent e)
    {
        _ = HandleEngineEventAsync(e);
    }

    private async Task HandleEngineEventAsync(EngineEvent e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.PayloadJson);
            var root = doc.RootElement;

            switch (e.EventType)
            {
                case 2: // DZ_EVENT_PEER_DISCOVERED
                    var peerId = root.GetProperty("device_id").GetString() ?? "";
                    var peerName = root.GetProperty("name").GetString() ?? "Unknown";
                    var peerIp = root.GetProperty("ip").GetString() ?? "";
                    var peerPort = (ushort)root.GetProperty("port").GetUInt16();

                    var device = await _deviceRepository.GetDeviceAsync(peerId) ?? new Device
                    {
                        Id = peerId,
                        Name = peerName,
                        IpAddress = peerIp,
                        Port = peerPort,
                        TrustLevel = DeviceTrustLevel.Prompt
                    };
                    device.LastSeen = DateTime.UtcNow;
                    device.IpAddress = peerIp;
                    device.Port = peerPort;
                    await _deviceRepository.UpsertDeviceAsync(device);
                    break;

                case 10: // DZ_EVENT_TRANSFER_REQUEST
                    var manifest = JsonSerializer.Deserialize<TransferManifest>(e.PayloadJson);
                    if (manifest != null)
                    {
                        await HandleInboundRequestAsync(manifest);
                    }
                    break;

                case 20: // DZ_EVENT_PROGRESS
                    var jobId = Guid.Parse(root.GetProperty("job_id").GetString()!);
                    var transferred = root.GetProperty("bytes_transferred").GetInt64();
                    var total = root.GetProperty("total_bytes").GetInt64();

                    if (_queue.TryGet(jobId, out var pJob) && pJob != null)
                    {
                        TransferSpeedEstimator? estimator;
                        lock (_lock)
                        {
                            if (!_speedEstimators.TryGetValue(jobId, out estimator))
                            {
                                estimator = new TransferSpeedEstimator();
                                _speedEstimators[jobId] = estimator;
                            }
                        }

                        estimator.RecordProgress(transferred);
                        pJob.TransferredBytes = transferred;
                        pJob.TotalBytes = total;
                        pJob.SpeedBytesPerSec = estimator.CurrentSpeedBytesPerSecond;
                        pJob.EtaSeconds = estimator.CalculateEtaSeconds(total);
                        pJob.State = TransferState.Transferring;

                        JobUpdated?.Invoke(pJob);
                        await _repository.UpdateJobProgressAsync(jobId, transferred, pJob.SpeedBytesPerSec, pJob.EtaSeconds);
                    }
                    break;

                case 25: // DZ_EVENT_TELEMETRY
                    var telemetry = JsonSerializer.Deserialize<TransferTelemetry>(e.PayloadJson);
                    if (telemetry != null)
                    {
                        TelemetryUpdated?.Invoke(telemetry);
                    }
                    break;

                case 40: // DZ_EVENT_COMPLETED
                    var compJobId = Guid.Parse(root.GetProperty("job_id").GetString()!);
                    if (_queue.TryGet(compJobId, out var cJob) && cJob != null)
                    {
                        cJob.State = TransferState.Completed;
                        cJob.TransferredBytes = cJob.TotalBytes;
                        cJob.CompletedAt = DateTime.UtcNow;
                        cJob.SpeedBytesPerSec = 0;
                        cJob.EtaSeconds = 0;

                        JobUpdated?.Invoke(cJob);
                        await _repository.UpdateJobStateAsync(compJobId, TransferState.Completed);

                        lock (_lock)
                        {
                            _speedEstimators.Remove(compJobId);
                            _retryCounts.Remove(compJobId);
                        }
                    }
                    break;

                case 51: // DZ_EVENT_PAIRING_PIN_DISPLAY
                    var pinInfo = JsonSerializer.Deserialize<PairingPinInfo>(e.PayloadJson);
                    if (pinInfo != null)
                    {
                        PairingPinDisplayRequested?.Invoke(pinInfo);
                    }
                    break;

                case 52: // DZ_EVENT_PAIRING_COMPLETED
                    var successResult = JsonSerializer.Deserialize<PairingResult>(e.PayloadJson);
                    if (successResult != null)
                    {
                        // Update device trust level in database
                        await _deviceRepository.UpdateTrustLevelAsync(successResult.PeerDeviceId, DeviceTrustLevel.Trusted);
                        PairingCompleted?.Invoke(successResult);
                    }
                    break;

                case 53: // DZ_EVENT_PAIRING_FAILED
                    var failResult = JsonSerializer.Deserialize<PairingResult>(e.PayloadJson);
                    if (failResult != null)
                    {
                        PairingCompleted?.Invoke(failResult);
                    }
                    break;

                case 60: // DZ_EVENT_NAT_CANDIDATE_DISCOVERED
                    var natCandidate = JsonSerializer.Deserialize<NatCandidate>(e.PayloadJson);
                    if (natCandidate != null)
                    {
                        NatCandidateDiscovered?.Invoke(natCandidate);
                    }
                    break;

                case 99: // DZ_EVENT_ERROR
                    var errJobId = Guid.Parse(root.GetProperty("job_id").GetString()!);
                    var errMsg = root.GetProperty("message").GetString() ?? "Unknown error";
                    await HandleTransferFailureAsync(errJobId, errMsg);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Orchestrator] Error parsing event {e.EventType}: {ex.Message}");
        }
    }

    private async Task HandleInboundRequestAsync(TransferManifest manifest)
    {
        var job = new TransferJob
        {
            Id = manifest.JobId,
            PeerDeviceId = manifest.SenderDeviceId,
            PeerDeviceName = manifest.SenderDeviceName,
            Direction = TransferDirection.Inbound,
            State = TransferState.Pending,
            TotalBytes = manifest.TotalBytes,
            TransferredBytes = 0,
            Files = manifest.Files,
            CreatedAt = DateTime.UtcNow
        };

        _queue.TryEnqueue(job);
        await _repository.AddJobAsync(job);
        JobUpdated?.Invoke(job);

        // Security check: Verify if any files in manifest are suspicious or disguised
        var hasSuspiciousFiles = manifest.Files.Any(f => _security.AnalyzeFile(f.RelativePath).IsSuspicious);

        // Check Device Trust Level and Granular Permissions
        var peer = await _deviceRepository.GetDeviceAsync(manifest.SenderDeviceId);
        var permissions = await _deviceRepository.GetPermissionsAsync(manifest.SenderDeviceId);

        if (peer != null && (peer.TrustLevel == DeviceTrustLevel.Blocked || permissions.IsRevoked))
        {
            await RejectInboundTransferAsync(job.Id, "Device blocked or revoked by security policy");
            return;
        }

        // Granular check: Size threshold and pin requirement
        var withinSizeLimit = manifest.TotalBytes <= permissions.MaxAutoAcceptBytes;
        var autoAcceptPermitted = peer != null && 
                                  peer.TrustLevel == DeviceTrustLevel.Trusted && 
                                  permissions.AllowAutoAccept && 
                                  withinSizeLimit && 
                                  !permissions.RequirePinVerification && 
                                  !hasSuspiciousFiles;

        if (autoAcceptPermitted)
        {
            // Auto-accept trusted device within threshold
            await AcceptInboundTransferAsync(job.Id);
            return;
        }

        // Prompt user (including suspicious file warning or size prompt)
        InboundTransferRequested?.Invoke(manifest);
    }

    public Task PingPeerAsync(string peerIp, ushort peerPort, CancellationToken ct = default)
    {
        return _engine.PingPeerAsync(peerIp, peerPort, ct);
    }

    public Task StartPairingAsync(string peerIp, ushort peerPort, CancellationToken ct = default)
    {
        return _engine.StartPairingAsync(peerIp, peerPort, ct);
    }

    public Task<bool> ConfirmPairingAsync(string peerDeviceId, string pinCode, bool isAccepted, CancellationToken ct = default)
    {
        return _engine.ConfirmPairingAsync(peerDeviceId, pinCode, isAccepted, ct);
    }

    public Task<NatCandidate?> TestStunAsync(string stunServer = "stun.l.google.com:19302", CancellationToken ct = default)
    {
        return _engine.TestStunAsync(stunServer, ct);
    }

    public Task GatherNatCandidatesAsync(string stunServer = "stun.l.google.com:19302", CancellationToken ct = default)
    {
        return _engine.GatherNatCandidatesAsync(stunServer, ct);
    }

    private async Task HandleTransferFailureAsync(Guid jobId, string message)
    {
        if (!_queue.TryGet(jobId, out var job) || job == null)
            return;

        int retries;
        lock (_lock)
        {
            _retryCounts.TryGetValue(jobId, out retries);
            retries++;
            _retryCounts[jobId] = retries;
        }

        // Automatic retry up to 3 times
        if (retries <= 3 && job.State != TransferState.Cancelled && job.State != TransferState.Paused)
        {
            job.State = TransferState.Connecting;
            job.ErrorMessage = $"Network hiccup. Retrying ({retries}/3)...";
            JobUpdated?.Invoke(job);
            await _repository.UpdateJobStateAsync(jobId, job.State, job.ErrorMessage);

            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries))); // 2s, 4s, 8s backoff
            await _engine.ResumeJobAsync(jobId);
        }
        else
        {
            job.State = TransferState.Failed;
            job.ErrorMessage = message;
            job.SpeedBytesPerSec = 0;
            job.EtaSeconds = 0;
            JobUpdated?.Invoke(job);
            await _repository.UpdateJobStateAsync(jobId, job.State, message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _engine.EventReceived -= OnEngineEventReceived;
        await _engine.DisposeAsync();
    }
}

