using BlazeHarness.Web.Scripts;
using Microsoft.JSInterop;

namespace BlazeHarness.Web.Tests.Unit.Scripts;

public sealed class PreferenceScriptsTests
{
    private readonly RecordingJsRuntime _js = new();
    private readonly PreferenceScripts _sut;

    public PreferenceScriptsTests() => _sut = new PreferenceScripts(_js);

    [Theory]
    [InlineData(true, "ui")]
    [InlineData(false, "chat")]
    public async Task SetTextSizeAsync_Kind_PassesItsScriptName(bool ui, string name)
    {
        // Act
        await _sut.SetTextSizeAsync(ui ? TextSizeKind.Ui : TextSizeKind.Chat, "large");

        // Assert
        _js.Calls.Should().ContainSingle().Which.Should().BeEquivalentTo(("bhSize.set", new object?[] { name, "large" }));
    }

    [Fact]
    public async Task SetDarkAsync_Dark_PassesTheThemeName()
    {
        // Act
        await _sut.SetDarkAsync(dark: true);

        // Assert
        _js.Calls.Should().ContainSingle().Which.Should().BeEquivalentTo(("bhTheme.set", new object?[] { "dark" }));
    }

    [Fact]
    public async Task GetTextSizeAsync_ScriptReturnsAStep_ReturnsIt()
    {
        // Arrange
        _js.Result = "large";

        // Act
        var step = await _sut.GetTextSizeAsync(TextSizeKind.Chat);

        // Assert
        step.Should().Be("large");
    }

    [Fact]
    public async Task GetTextSizeAsync_CircuitIsGone_ReturnsTheDefault()
    {
        // Arrange
        _js.Failure = new JSDisconnectedException("gone");

        // Act
        var step = await _sut.GetTextSizeAsync(TextSizeKind.Ui);

        // Assert
        step.Should().Be("default");
    }

    [Fact]
    public async Task SetFullChatWidthAsync_CircuitIsGone_DoesNotThrow()
    {
        // Arrange
        _js.Failure = new JSDisconnectedException("gone");

        // Act
        var act = async () => await _sut.SetFullChatWidthAsync(full: true);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetFullChatWidthAsync_OtherFailure_Throws()
    {
        // Arrange
        _js.Failure = new JSException("broken");

        // Act
        var act = async () => await _sut.SetFullChatWidthAsync(full: true);

        // Assert
        await act.Should().ThrowAsync<JSException>();
    }
}
