using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Zer0Talk.Utilities;
using Xunit;

namespace Zer0Talk.Tests;

public class BackupArchiveFormatTests
{
    [Theory]
    [InlineData("messages/test.p2e", "messages/test.p2e")]
    [InlineData("/messages/test.p2e", "messages/test.p2e")]
    [InlineData("\\messages\\test.p2e", "messages/test.p2e")]
    [InlineData("  /Themes/theme.zttheme  ", "Themes/theme.zttheme")]
    public void NormalizeEntryPath_NormalizesSeparatorsAndLeadingSlash(string input, string expected)
    {
        var actual = BackupArchiveFormat.NormalizeEntryPath(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("settings.p2e", true)]
    [InlineData("messages/peer/file.p2e", true)]
    [InlineData("Themes/custom.zttheme", true)]
    [InlineData("backup.manifest.json", true)]
    [InlineData("../settings.p2e", false)]
    [InlineData("messages/../../settings.p2e", false)]
    [InlineData("random.txt", false)]
    [InlineData("", false)]
    public void IsAllowedEntry_EnforcesAllowList(string relPath, bool expected)
    {
        var actual = BackupArchiveFormat.IsAllowedEntry(relPath);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Manifest_WriteThenRead_IsSupported()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            BackupArchiveFormat.WriteManifest(archive, "9.9.9-test");
            var payload = archive.CreateEntry("messages/test.txt");
            using var writer = new StreamWriter(payload.Open(), Encoding.UTF8, leaveOpen: false);
            writer.Write("sample");
        }

        ms.Position = 0;
        using var readArchive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var ok = BackupArchiveFormat.TryReadManifest(readArchive, out var manifest);

        Assert.True(ok);
        Assert.NotNull(manifest);
        Assert.Equal(BackupArchiveFormat.FormatId, manifest!.Format);
        Assert.Equal(BackupArchiveFormat.FormatVersion, manifest.Version);
        Assert.True(BackupArchiveFormat.IsSupportedManifest(manifest));
        Assert.Contains("messages", manifest.Includes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("settings.p2e", manifest.Includes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsupportedManifestVersion_IsRejected()
    {
        var unsupported = new BackupArchiveFormat.BackupManifest
        {
            Format = BackupArchiveFormat.FormatId,
            Version = BackupArchiveFormat.FormatVersion + 1,
            AppVersion = "future",
            CreatedUtc = DateTime.UtcNow,
            Includes = Array.Empty<string>()
        };

        Assert.False(BackupArchiveFormat.IsSupportedManifest(unsupported));
    }
}
