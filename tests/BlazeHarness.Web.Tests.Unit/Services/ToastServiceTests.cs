using BlazeHarness.Web.Services;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class ToastServiceTests
{
    private readonly ToastService _sut = new();
    private readonly List<Toast> _shown = [];

    public ToastServiceTests()
    {
        _sut.Shown += (_, e) => _shown.Add(e.Value);
    }

    [Fact]
    public async Task TrySaveAsync_ActionSucceeds_ReturnsTrueWithoutToast()
    {
        // Arrange
        var ran = false;

        // Act
        var result = await _sut.TrySaveAsync(Run, "failed");

        // Assert
        result.Should().BeTrue();
        ran.Should().BeTrue();
        _shown.Should().BeEmpty();

        Task Run()
        {
            ran = true;
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task TrySaveAsync_StorageFailure_ShowsErrorToastWithoutTheExceptionMessage(Type exceptionType)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType, "secret text")!;

        // Act
        var result = await _sut.TrySaveAsync(() => throw exception, "Could not save.");

        // Assert
        result.Should().BeFalse();
        _shown.Should().ContainSingle().Which.Should().Match<Toast>(t => t.Message == "Could not save." && t.Kind == ToastKind.Error);
    }

    [Fact]
    public async Task TrySaveAsync_OtherException_Propagates()
    {
        // Arrange
        Func<Task> act = () => _sut.TrySaveAsync(() => throw new ArgumentException("bug"), "failed");

        // Act / Assert
        await act.Should().ThrowAsync<ArgumentException>();
        _shown.Should().BeEmpty();
    }
}
