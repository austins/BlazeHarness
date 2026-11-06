using BlazeHarness.Core.Config;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Integration.Config;

public sealed class CustomInstructionsFileTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-instructions-");
    private readonly string _path;

    public CustomInstructionsFileTests()
    {
        Directory.CreateDirectory(_temp.Root);
        _path = Path.Combine(_temp.Root, "instructions.md");
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void Constructor_NoFile_IsEmpty()
    {
        // Act
        using var sut = Create();

        // Assert
        sut.Text.Should().BeEmpty();
    }

    [Fact]
    public async Task Constructor_ExistingFile_ReadsIt()
    {
        // Arrange
        await File.WriteAllTextAsync(_path, "Reply briefly.", TestContext.Current.CancellationToken);

        // Act
        using var sut = Create();

        // Assert
        sut.Text.Should().Be("Reply briefly.");
    }

    [Fact]
    public async Task SaveAsync_Text_WritesTheTextAsGivenAndDoesNotRaiseExternalChange()
    {
        // Arrange
        using var sut = Create();
        var raised = 0;
        sut.ExternalChange += (_, _) => raised++;

        // Act
        await sut.SaveAsync("# Rules\n\nBe kind.", TestContext.Current.CancellationToken);
        sut.OnExternalChange();

        // Assert
        sut.Text.Should().Be("# Rules\n\nBe kind.");
        (await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken)).Should().Be("# Rules\n\nBe kind.");
        raised.Should().Be(0);
    }

    [Fact]
    public async Task OnExternalChange_EditedOnDisk_ReloadsAndRaisesExternalChange()
    {
        // Arrange
        using var sut = Create();
        var raised = 0;
        sut.ExternalChange += (_, _) => raised++;
        await File.WriteAllTextAsync(_path, "Reply briefly.", TestContext.Current.CancellationToken);

        // Act
        sut.OnExternalChange();

        // Assert
        raised.Should().Be(1);
        sut.Text.Should().Be("Reply briefly.");
    }

    [Fact]
    public async Task OnExternalChange_FileDeleted_BecomesEmpty()
    {
        // Arrange
        await File.WriteAllTextAsync(_path, "Reply briefly.", TestContext.Current.CancellationToken);
        using var sut = Create();
        File.Delete(_path);

        // Act
        sut.OnExternalChange();

        // Assert
        sut.Text.Should().BeEmpty();
    }

    [Fact]
    public async Task OnExternalChange_FileCannotBeRead_KeepsTheLoadedTextAndRetries()
    {
        // Arrange
        await File.WriteAllTextAsync(_path, "Reply briefly.", TestContext.Current.CancellationToken);
        var time = new FakeTimeProvider();
        using var sut = Create(time);
        var raised = 0;
        sut.ExternalChange += (_, _) => raised++;
        var locked = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Act
        sut.OnExternalChange();
        var keptWhileLocked = (raised, sut.Text);
        await locked.DisposeAsync();
        await File.WriteAllTextAsync(_path, "Be kind.", TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));

        // Assert
        keptWhileLocked.Should().Be((0, "Reply briefly."));
        raised.Should().Be(1);
        sut.Text.Should().Be("Be kind.");
    }

    [Fact]
#pragma warning disable IDISP016, IDISP017 // Using the file after disposal is the point of the test.
    public void OnExternalChange_AfterDispose_DoesNotThrow()
    {
        // Arrange
        var sut = Create();
        sut.Dispose();

        // Act
        var act = sut.OnExternalChange;

        // Assert
        act.Should().NotThrow();
    }
#pragma warning restore IDISP016, IDISP017

    private CustomInstructionsFile Create(TimeProvider? time = null)
    {
        return new CustomInstructionsFile(_path, NullLogger<CustomInstructionsFile>.Instance, time ?? TimeProvider.System, watch: false);
    }
}
