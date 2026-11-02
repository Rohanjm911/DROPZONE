using System.Collections.Concurrent;
using System.Text.Json;
using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DropZone.Infrastructure.Enterprise;

public sealed class EnterpriseCertificateValidator : IEnterpriseSecurityService
{
    private readonly ILogger<EnterpriseCertificateValidator>? _logger;
    private readonly ConcurrentDictionary<string, EnterpriseCertificate> _trustedRoots = new();
    private readonly ConcurrentDictionary<string, string> _revocationList = new();
    private EnterprisePolicy _policy = new();
    private readonly object _lock = new();

    public EnterpriseCertificateValidator(ILogger<EnterpriseCertificateValidator>? logger = null)
    {
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task RegisterTrustedRootCertificateAsync(EnterpriseCertificate rootCertificate, CancellationToken ct = default)
    {
        _trustedRoots[rootCertificate.Thumbprint] = rootCertificate;
        _logger?.LogInformation("[Enterprise PKI] Registered trusted root CA: {Subject} ({Thumbprint})",
            rootCertificate.Subject, rootCertificate.Thumbprint);
        return Task.CompletedTask;
    }

    public Task RevokeCertificateAsync(string thumbprint, string reason, CancellationToken ct = default)
    {
        _revocationList[thumbprint] = reason;
        _logger?.LogWarning("[Enterprise PKI] Certificate {Thumbprint} revoked: {Reason}", thumbprint, reason);
        return Task.CompletedTask;
    }

    public Task<bool> ValidateDeviceCertificateAsync(
        string deviceId,
        EnterpriseCertificate certificate,
        CancellationToken ct = default)
    {
        if (certificate == null)
            return Task.FromResult(false);

        // 1. Revocation check
        if (certificate.IsRevoked || _revocationList.ContainsKey(certificate.Thumbprint))
        {
            _logger?.LogWarning("[Enterprise PKI] Device '{DeviceId}' presented REVOKED certificate {Thumbprint}", deviceId, certificate.Thumbprint);
            return Task.FromResult(false);
        }

        // 2. Validity date range check
        var now = DateTime.UtcNow;
        if (now < certificate.ValidFromUtc || now > certificate.ValidToUtc)
        {
            _logger?.LogWarning("[Enterprise PKI] Device '{DeviceId}' presented EXPIRED/NOT_YET_VALID certificate {Thumbprint}", deviceId, certificate.Thumbprint);
            return Task.FromResult(false);
        }

        // 3. Trusted Root Issuer check (if root store is populated)
        if (_trustedRoots.Count > 0)
        {
            var isIssuerTrusted = _trustedRoots.Values.Any(r => 
                string.Equals(r.Subject, certificate.Issuer, StringComparison.OrdinalIgnoreCase));

            if (!isIssuerTrusted && !_trustedRoots.ContainsKey(certificate.Thumbprint))
            {
                _logger?.LogWarning("[Enterprise PKI] Device '{DeviceId}' certificate issuer '{Issuer}' is NOT in trusted root CA store", deviceId, certificate.Issuer);
                return Task.FromResult(false);
            }
        }

        _logger?.LogInformation("[Enterprise PKI] Device '{DeviceId}' certificate {Thumbprint} successfully verified", deviceId, certificate.Thumbprint);
        return Task.FromResult(true);
    }

    public Task<EnterprisePolicy> GetPolicyAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_policy);
        }
    }

    public Task UpdatePolicyAsync(EnterprisePolicy policy, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _policy = policy;
        }
        _logger?.LogInformation("[Enterprise PKI] Enterprise security policy updated: EnforceCerts={Enforce}, DisallowUnmanaged={Disallow}",
            policy.EnforceCertificatesOnly, policy.DisallowUnmanagedDevices);
        return Task.CompletedTask;
    }
}
