using BlazeHarness.Web.Scripts;
using Microsoft.JSInterop;

namespace BlazeHarness.Web.Tests.Unit.Scripts;

public sealed class ChatScriptsTests
{
    private readonly RecordingJsRuntime _js = new();
    private readonly ChatScripts _sut;

    public ChatScriptsTests() => _sut = new ChatScripts(_js);

    [Fact]
    public async Task CopyTextAsync_ScriptSucceeds_ReturnsTrue()
    {
        // Arrange
        _js.Result = true;

        // Act
        var copied = await _sut.CopyTextAsync("hello");

        // Assert
        copied.Should().BeTrue();
        _js.Calls.Should().ContainSingle().Which.Should().BeEquivalentTo(("bhChat.copyText", new object?[] { "hello" }));
    }

    [Fact]
    public async Task CopyTextAsync_CircuitIsGone_ReturnsFalse()
    {
        // Arrange
        _js.Failure = new JSDisconnectedException("gone");

        // Act
        var copied = await _sut.CopyTextAsync("hello");

        // Assert
        copied.Should().BeFalse();
    }

    [Fact]
    public async Task HighlightAsync_EntryId_PassesItAsText()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        await _sut.HighlightAsync(default, "query", id);

        // Assert
        _js.Calls.Should().ContainSingle().Which.Args.Should().EndWith(["query", id.ToString()]);
    }

    [Fact]
    public async Task ClearHighlightAsync_CallIsCanceled_DoesNotThrow()
    {
        // Arrange
        _js.Failure = new TaskCanceledException();

        // Act
        var act = async () => await _sut.ClearHighlightAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }
}
