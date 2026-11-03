using BlazeHarness.Core.Web.Content;

namespace BlazeHarness.Core.Tests.Unit.Web.Content;

public sealed class OfficeMediaTypesTests
{
    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("APPLICATION/VND.OPENXMLFORMATS-OFFICEDOCUMENT.WORDPROCESSINGML.DOCUMENT")]
    public void IsOffice_OfficeType_ReturnsTrue(string mediaType)
    {
        // Act
        var result = OfficeMediaTypes.IsOffice(mediaType);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("application/pdf")]
    [InlineData("application/msword")]
    [InlineData("application/zip")]
    public void IsOffice_OtherType_ReturnsFalse(string? mediaType)
    {
        // Act
        var result = OfficeMediaTypes.IsOffice(mediaType);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("application/octet-stream")]
    [InlineData("application/zip")]
    [InlineData("application/x-zip-compressed")]
    [InlineData("Application/Octet-Stream")]
    public void IsGenericBinary_GenericType_ReturnsTrue(string? mediaType)
    {
        // Act
        var result = OfficeMediaTypes.IsGenericBinary(mediaType);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void IsGenericBinary_SpecificType_ReturnsFalse(string mediaType)
    {
        // Act
        var result = OfficeMediaTypes.IsGenericBinary(mediaType);

        // Assert
        result.Should().BeFalse();
    }
}
