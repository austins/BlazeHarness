using BlazeHarness.Core.Agent;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class RepeatedFailuresTests
{
    private readonly RepeatedFailures _sut = new();

    [Fact]
    public void Refuses_CallFailedTheMaximumTimes_ReturnsTrue()
    {
        // Arrange
        for (var i = 0; i < RepeatedFailures.MaxFailures; i++)
        {
            _sut.Record("read", """{"a":1,"b":[{"y":2,"x":1}]}""", failed: true, readOnly: true);
        }

        // Act
        var refused = _sut.Refuses("read", """{"b":[{"x":1,"y":2}],"a":1}""");

        // Assert
        refused.Should().BeTrue();
    }

    [Fact]
    public void Refuses_OneFewerFailure_ReturnsFalse()
    {
        // Arrange
        for (var i = 1; i < RepeatedFailures.MaxFailures; i++)
        {
            _sut.Record("read", "{}", failed: true, readOnly: true);
        }

        // Act
        var refused = _sut.Refuses("read", "{}");

        // Assert
        refused.Should().BeFalse();
    }

    [Fact]
    public void Refuses_OtherArguments_ReturnsFalse()
    {
        // Arrange
        for (var i = 0; i < RepeatedFailures.MaxFailures; i++)
        {
            _sut.Record("read", """{"a":1}""", failed: true, readOnly: true);
        }

        // Act
        var refused = _sut.Refuses("read", """{"a":2}""");

        // Assert
        refused.Should().BeFalse();
    }

    [Fact]
    public void Refuses_AfterAWriteSucceeded_ReturnsFalse()
    {
        // Arrange
        for (var i = 0; i < RepeatedFailures.MaxFailures; i++)
        {
            _sut.Record("read", "{}", failed: true, readOnly: true);
        }

        _sut.Record("write", "{}", failed: false, readOnly: false);

        // Act
        var refused = _sut.Refuses("read", "{}");

        // Assert
        refused.Should().BeFalse();
    }

    [Fact]
    public void Refuses_AfterAnotherReadSucceeded_ReturnsTrue()
    {
        // Arrange
        for (var i = 0; i < RepeatedFailures.MaxFailures; i++)
        {
            _sut.Record("read", "{}", failed: true, readOnly: true);
        }

        _sut.Record("list", "{}", failed: false, readOnly: true);

        // Act
        var refused = _sut.Refuses("read", "{}");

        // Assert
        refused.Should().BeTrue();
    }
}
