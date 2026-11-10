using BlazeHarness.Core.Config;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Web.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazeHarness.Web.Tests.Integration.Hosting;

public sealed class EditorLauncherTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly JsonFileStore<AppConfig> _config;
    private readonly EditorLauncher _sut;

    public EditorLauncherTests()
    {
        _config = new JsonFileStore<AppConfig>(_temp.Combine("config.json"), NullLogger<JsonFileStore<AppConfig>>.Instance, TimeProvider.System, watch: false);
        _sut = new EditorLauncher(_config, NullLogger<EditorLauncher>.Instance);
        _temp.CreateDirectory("repo", "src");
        File.WriteAllText(_temp.Combine("repo", "src", "a.cs"), "class A;");
        File.WriteAllText(_temp.Combine("outside.cs"), "class B;");
    }

    public void Dispose()
    {
        _config.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void Resolve_FileInsideTheFolder_ReturnsItsFullPath()
    {
        // Act
        var path = EditorLauncher.Resolve(_temp.Combine("repo"), "src/a.cs");

        // Assert
        path.Should().Be(_temp.Combine("repo", "src", "a.cs"));
    }

    [Theory]
    [InlineData("../outside.cs")]
    [InlineData("src/missing.cs")]
    [InlineData("src")]
    [InlineData("")]
    public void Resolve_OutsideMissingOrNotAFile_ReturnsNull(string relativePath)
    {
        // Act
        var path = EditorLauncher.Resolve(_temp.Combine("repo"), relativePath);

        // Assert
        path.Should().BeNull();
    }

    [Fact]
    public void Resolve_RootedPath_ReturnsNull()
    {
        // Act
        var path = EditorLauncher.Resolve(_temp.Combine("repo"), _temp.Combine("outside.cs"));

        // Assert
        path.Should().BeNull();
    }

    [Fact]
    public async Task OpenAsync_MissingFile_ReturnsNotFound()
    {
        // Act
        var result = await _sut.OpenAsync(_temp.Combine("repo"), "src/missing.cs", TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(EditorOpenResult.NotFound);
    }

    [Fact]
    public async Task OpenAsync_ConfiguredEditorMissing_ReturnsFailed()
    {
        // Arrange
        await _config.UpdateAsync(config => config with { EditorCommand = "bh-no-such-editor-" + Guid.NewGuid().ToString("N") + " -x" }, TestContext.Current.CancellationToken);

        // Act
        var result = await _sut.OpenAsync(_temp.Combine("repo"), "src/a.cs", TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(EditorOpenResult.Failed);
    }
}
