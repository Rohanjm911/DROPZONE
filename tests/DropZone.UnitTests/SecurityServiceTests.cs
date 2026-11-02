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
}
