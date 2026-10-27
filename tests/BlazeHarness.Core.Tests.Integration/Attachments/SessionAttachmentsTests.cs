using BlazeHarness.Core.Attachments;

namespace BlazeHarness.Core.Tests.Integration.Attachments;

public sealed class SessionAttachmentsTests : IDisposable
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bh-attach-" + Guid.NewGuid().ToString("N"));
    private readonly SessionAttachments _sut;

    public SessionAttachmentsTests()
    {
        _sut = new SessionAttachments(Path.Combine(_folder, "attachments"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_TextFile_KeepsItAndCountsLines()
    {
        // Act
        var info = await _sut.SaveAsync("notes.md", "one\ntwo\nthree\n");

        // Assert
        info.Should().Match<AttachmentInfo>(i => i.Name == "notes.md" && i.MediaType == "text/plain" && i.Size == 14 && i.Lines == 3 && i.Id.Length == 8);
        _sut.Find(info.Id).Should().Match<AttachmentFile>(f => f.Name == "notes.md" && f.MediaType == "text/plain");
        (await File.ReadAllTextAsync(_sut.Find(info.Id)!.Path, TestContext.Current.CancellationToken)).Should().Be("one\ntwo\nthree\n");
    }

    [Fact]
    public async Task SaveAsync_PngNamedAsText_IsAnImageWithTheRightExtension()
    {
        // Act
        var info = await _sut.SaveAsync("screenshot.txt", PngHeader);

        // Assert
        info.Should().Match<AttachmentInfo>(i => i.Name == "screenshot.png" && i.MediaType == "image/png" && i.Lines == 0 && i.IsImage);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 }, "image/jpeg", "a.jpg")]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 0, 0, 0, 0, 0, 0 }, "image/gif", "a.gif")]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, "image/webp", "a.webp")]
    public async Task SaveAsync_OtherImageFormats_AreRecognizedFromTheirContent(byte[] content, string mediaType, string name)
    {
        // Act
        var info = await _sut.SaveAsync("a.bin", content);

        // Assert
        (info.MediaType, info.Name).Should().Be((mediaType, name));
    }

    [Fact]
    public async Task SaveAsync_LongNameOfATextFileNamedAsAnImage_StaysWithinTheNameLimit()
    {
        // Act
        var info = await _sut.SaveAsync(new string('n', 200) + ".png", "just words");

        // Assert
        info.Name.Should().HaveLength(80).And.EndWith(".txt");
    }

    [Fact]
    public async Task SaveAsync_LongNameOfAnImageWithTheWrongExtension_StaysWithinTheNameLimit()
    {
        // Act
        var info = await _sut.SaveAsync(new string('n', 200) + ".txt", PngHeader);

        // Assert
        info.Name.Should().HaveLength(80).And.EndWith(".png");
    }

    [Fact]
    public async Task SaveAsync_TextNamedAsAnImage_GetsATextExtension()
    {
        // Act
        var info = await _sut.SaveAsync("fake.png", "just words");

        // Assert
        (info.MediaType, info.Name).Should().Be(("text/plain", "fake.png.txt"));
        _sut.Find(info.Id)!.MediaType.Should().Be("text/plain");
    }

    [Fact]
    public async Task SaveAsync_BinaryFile_IsRejectedAndLeavesNothingBehind()
    {
        // Act
        var act = () => _sut.SaveAsync("doc.pdf", [(byte)'%', (byte)'P', (byte)'D', (byte)'F', 0, 1, 2, 3, 4, 5, 6, 7]);

        // Assert
        (await act.Should().ThrowAsync<AttachmentException>()).WithMessage("*doc.pdf*Only images*");
        Directory.GetFileSystemEntries(_sut.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_EmptyFile_IsRejected()
    {
        // Act
        var act = () => _sut.SaveAsync("empty.txt", []);

        // Assert
        (await act.Should().ThrowAsync<AttachmentException>()).WithMessage("*empty*");
    }

    [Fact]
    public async Task SaveAsync_FileOverTheLimit_IsRejectedAndLeavesNothingBehind()
    {
        // Act
        var act = () => _sut.SaveAsync("big.txt", new byte[SessionAttachments.MaxFileBytes + 1]);

        // Assert
        (await act.Should().ThrowAsync<AttachmentException>()).WithMessage("*larger than 20 MB*");
        Directory.GetFileSystemEntries(_sut.Directory).Should().BeEmpty();
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\Users\\me\\notes.txt", "notes.txt")]
    [InlineData("a/b/c.txt", "c.txt")]
    [InlineData("", "file")]
    [InlineData("  ..  ", "file")]
    [InlineData("bad\u0001name.txt", "bad_name.txt")]
    public async Task SaveAsync_ClientSuppliedName_NeverBecomesAPath(string given, string expected)
    {
        // Act
        var info = await _sut.SaveAsync(given, "text");

        // Assert
        info.Name.Should().Be(expected);
        Path.GetDirectoryName(_sut.Find(info.Id)!.Path).Should().Be(_sut.Directory);
    }

    [Fact]
    public async Task SaveAsync_VeryLongName_IsShortenedKeepingTheExtension()
    {
        // Act
        var info = await _sut.SaveAsync(new string('a', 300) + ".txt", "text");

        // Assert
        info.Name.Should().HaveLength(80).And.EndWith(".txt");
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("ab")]
    [InlineData("zzzzzzzz")]
    [InlineData("ABCDEF12")]
    [InlineData("*")]
    [InlineData(null)]
    public void Find_InvalidId_ReturnsNull(string? id)
    {
        // Act
        var file = _sut.Find(id);

        // Assert
        file.Should().BeNull();
    }

    [Fact]
    public async Task Find_WellFormedUnknownId_ReturnsNull()
    {
        // Arrange
        await _sut.SaveAsync("a.txt", "x");

        // Act
        var file = _sut.Find("0123abcd");

        // Assert
        file.Should().BeNull();
    }
}
