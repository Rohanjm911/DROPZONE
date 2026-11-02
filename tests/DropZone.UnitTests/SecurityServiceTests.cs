using System.Text;
using DropZone.Infrastructure.Storage;
using Xunit;

namespace DropZone.UnitTests;

public class SecurityServiceTests
{
    private readonly SecurityService _service = new();

    [Theory]
    [InlineData("photos/vacation.jpg", true)]
    [InlineData("documents/reports/2026.pdf", true)]
    [InlineData("simple.txt", true)]
    [InlineData("../escape.txt", false)]
    [InlineData("photos/../../etc/passwd", false)]
    [InlineData("/root/secret", false)]
    [InlineData("C:\\Windows\\System32\\cmd.exe", false)]
    [InlineData("CON", false)]
    [InlineData("sub/PRN.txt", false)]
    [InlineData("", false)]
    [InlineData("null\0byte.txt", false)]
    public void IsPathSafe_ValidatesCorrectly(string path, bool expectedSafe)
    {
        var result = _service.IsPathSafe(path, out var sanitized);
        Assert.Equal(expectedSafe, result);
        if (expectedSafe)
        {
            Assert.False(string.IsNullOrWhiteSpace(sanitized));
            Assert.DoesNotContain("..", sanitized);
        }
    }

    [Theory]
    [InlineData("malware.exe", true)]
    [InlineData("script.bat", true)]
    [InlineData("hack.cmd", true)]
    [InlineData("linux.sh", true)]
    [InlineData("android.apk", true)]
    [InlineData("document.pdf", false)]
    [InlineData("image.png", false)]
    [InlineData("data.json", false)]
    public void IsSuspiciousFileType_DetectsExecutableFiles(string filename, bool expectedSuspicious)
    {
        var result = _service.IsSuspiciousFileType(filename);
        Assert.Equal(expectedSuspicious, result);
    }

    [Fact]
    public void ComputeSha256_KnownVector_Matches()
    {
        var data = Encoding.UTF8.GetBytes("hello dropzone");
        var hash = _service.ComputeSha256(data);

        Assert.Equal(64, hash.Length);
        Assert.Equal("905fb293d8c506cd043536f62b5857df620878e80c6ef7f594ac3f2c7ed70b56", hash);
    }

    [Fact]
    public void AnalyzeFile_DetectsDoubleExtensionsAndRtloSpoofing()
    {
        // RTLO Unicode spoofing
        var rtlo = _service.AnalyzeFile("invoice\u202Efdp.exe");
        Assert.True(rtlo.IsSuspicious);
        Assert.Equal(Domain.Models.ThreatRiskLevel.Critical, rtlo.RiskLevel);

        // Disguised double extension
        var disguised = _service.AnalyzeFile("company_financials.pdf.exe");
        Assert.True(disguised.IsSuspicious);
        Assert.Equal(Domain.Models.ThreatRiskLevel.Critical, disguised.RiskLevel);
        Assert.Contains("double extension", disguised.Reason);

        // Standard executable
        var standardExe = _service.AnalyzeFile("setup.msi");
        Assert.True(standardExe.IsSuspicious);
        Assert.Equal(Domain.Models.ThreatRiskLevel.High, standardExe.RiskLevel);

        // Clean file
        var clean = _service.AnalyzeFile("contract.docx");
        Assert.False(clean.IsSuspicious);
        Assert.Equal(Domain.Models.ThreatRiskLevel.None, clean.RiskLevel);
    }

    [Fact]
    public async Task Quarantine_Lifecycle_QuarantinesRestoresAndDeletes()
    {
        var tempQuarantineDir = Path.Combine(Path.GetTempPath(), $"dz_quarantine_test_{Guid.NewGuid():N}");
        var customSecurity = new SecurityService(tempQuarantineDir);

        try
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"malicious_{Guid.NewGuid():N}.exe");
            await File.WriteAllTextAsync(tempFile, "SUSPICIOUS_PAYLOAD_TEST");

            // 1. Quarantine file
            var record = await customSecurity.QuarantineFileAsync(tempFile, "malicious.exe", "Test Threat", "peer-123");
            Assert.False(File.Exists(tempFile));
            Assert.True(File.Exists(record.QuarantinedFilePath));

            // 2. Query quarantined files
            var list = await customSecurity.GetQuarantinedFilesAsync();
            Assert.Contains(list, r => r.Id == record.Id && r.OriginalFileName == Path.GetFileName(tempFile));

            // 3. Restore file
            var restoredPath = Path.Combine(Path.GetTempPath(), $"restored_{Guid.NewGuid():N}.exe");
            var restored = await customSecurity.RestoreQuarantinedFileAsync(record.Id, restoredPath);
            Assert.True(restored);
            Assert.True(File.Exists(restoredPath));
            Assert.Equal("SUSPICIOUS_PAYLOAD_TEST", await File.ReadAllTextAsync(restoredPath));

            // 4. Delete restored
            File.Delete(restoredPath);
        }
        finally
        {
            if (Directory.Exists(tempQuarantineDir))
            {
                Directory.Delete(tempQuarantineDir, recursive: true);
            }
        }
    }
}
