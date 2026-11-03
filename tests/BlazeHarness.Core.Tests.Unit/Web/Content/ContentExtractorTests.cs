using BlazeHarness.Core.Web;
using BlazeHarness.Core.Web.Content;
using System.Text;

namespace BlazeHarness.Core.Tests.Unit.Web.Content;

public sealed class ContentExtractorTests
{
    private static readonly Uri Address = new("https://example.com/page");

    private readonly IPdfTextExtractor _pdf = Substitute.For<IPdfTextExtractor>();
    private readonly IOfficeTextExtractor _office = Substitute.For<IOfficeTextExtractor>();
    private readonly ContentExtractor _sut;

    public ContentExtractorTests()
    {
        _sut = new ContentExtractor(_pdf, _office);
    }

    [Fact]
    public void Extract_Html_ConvertsToMarkdown()
    {
        // Act
        var result = _sut.Extract(Response("text/html", "<html><head><title>T</title></head><body><h1>Hi</h1></body></html>"), TestContext.Current.CancellationToken);

        // Assert
        result.Title.Should().Be("T");
        result.Text.Should().Contain("# Hi");
    }

    [Fact]
    public void Extract_Json_IsReadAsTrimmedText()
    {
        // Act
        var result = _sut.Extract(Response("application/json", "\uFEFF  {\"a\":1}\n"), TestContext.Current.CancellationToken);

        // Assert
        result.Text.Should().Be("{\"a\":1}");
        result.Notice.Should().BeNull();
    }

    [Fact]
    public void Extract_TextWithCharset_DecodesWithIt()
    {
        // Arrange
        var body = Encoding.Latin1.GetBytes("caf\u00e9");

        // Act
        var result = _sut.Extract(new FetchResponse(Address, 200, "text/plain", "iso-8859-1", body, Truncated: false), TestContext.Current.CancellationToken);

        // Assert
        result.Text.Should().Be("caf\u00e9");
    }

    [Fact]
    public void Extract_UnknownCharset_FallsBackToUtf8()
    {
        // Act
        var result = _sut.Extract(new FetchResponse(Address, 200, "text/plain", "no-such-charset", Encoding.UTF8.GetBytes("caf\u00e9"), Truncated: false), TestContext.Current.CancellationToken);

        // Assert
        result.Text.Should().Be("caf\u00e9");
    }

    [Fact]
    public void Extract_TruncatedText_AddsNotice()
    {
        // Act
        var result = _sut.Extract(Response("text/plain", "partial", truncated: true), TestContext.Current.CancellationToken);

        // Assert
        result.Notice.Should().Contain("cut off");
    }

    [Fact]
    public void Extract_NoContentTypeButHtml_IsReadAsPage()
    {
        // Act
        var result = _sut.Extract(Response(null, "<!DOCTYPE html><html><body><h2>Sniffed</h2></body></html>"), TestContext.Current.CancellationToken);

        // Assert
        result.Text.Should().Contain("## Sniffed");
    }

    [Fact]
    public void Extract_NoContentTypeAndBinary_Throws()
    {
        // Act
        var act = () => _sut.Extract(new FetchResponse(Address, 200, null, null, [1, 0, 2, 3], Truncated: false), TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*'unknown'*");
    }

    [Fact]
    public void Extract_Image_Throws()
    {
        // Act
        var act = () => _sut.Extract(Response("image/png", "x"), TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*'image/png'*");
    }

    [Fact]
    public void Extract_PdfBySignature_GoesToThePdfReader()
    {
        // Arrange
        var body = Encoding.ASCII.GetBytes("%PDF-1.7 ...");
        var expected = new ExtractedDocument("Doc", "text", []);
        _pdf.Extract(body, Arg.Any<CancellationToken>()).Returns(expected);

        // Act
        var result = _sut.Extract(new FetchResponse(Address, 200, "application/octet-stream", null, body, Truncated: false), TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public void Extract_TruncatedPdf_Throws()
    {
        // Act
        var act = () => _sut.Extract(Response("application/pdf", "%PDF-1.7", truncated: true), TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*too large*");
        _pdf.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    public void Extract_OfficeContentType_GoesToTheOfficeReader(string contentType)
    {
        // Arrange
        var body = new byte[] { 1, 2, 3 };
        var expected = new ExtractedDocument("Doc", "text", []);
        _office.Extract(body, Arg.Any<CancellationToken>()).Returns(expected);

        // Act
        var result = _sut.Extract(new FetchResponse(Address, 200, contentType, null, body, Truncated: false), TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeSameAs(expected);
    }

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("application/zip")]
    [InlineData(null)]
    public void Extract_ZipBodyWithGenericType_GoesToTheOfficeReader(string? contentType)
    {
        // Arrange
        var body = new byte[] { (byte)'P', (byte)'K', 3, 4, 0 };
        var expected = new ExtractedDocument("Doc", "text", []);
        _office.Extract(body, Arg.Any<CancellationToken>()).Returns(expected);

        // Act
        var result = _sut.Extract(new FetchResponse(Address, 200, contentType, null, body, Truncated: false), TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public void Extract_TruncatedOfficeDocument_Throws()
    {
        // Act
        var act = () => _sut.Extract(Response("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "PK", truncated: true), TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*too large*");
        _office.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Extract_OctetStreamThatIsNotAZip_Throws()
    {
        // Act
        var act = () => _sut.Extract(Response("application/octet-stream", "plain bytes"), TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*Office documents*'application/octet-stream'*");
        _office.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Extract_ZipSignatureWithHtmlType_IsStillHtml()
    {
        // Act
        var result = _sut.Extract(Response("text/html", "PK\u0003\u0004<p>Hi</p>"), TestContext.Current.CancellationToken);

        // Assert
        result.Text.Should().Contain("Hi");
        _office.ReceivedCalls().Should().BeEmpty();
    }

    private static FetchResponse Response(string? contentType, string body, bool truncated = false)
    {
        return new FetchResponse(Address, 200, contentType, null, Encoding.UTF8.GetBytes(body), truncated);
    }
}
