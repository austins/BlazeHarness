using BlazeHarness.Core.Web;
using BlazeHarness.Core.Web.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace BlazeHarness.Core.Tests.Unit.Web.Content;

public sealed class PdfPigTextExtractorTests
{
    private readonly PdfPigTextExtractor _sut = new();

    [Fact]
    public void Extract_Pdf_ReturnsPageText()
    {
        // Act
        var result = _sut.Extract(Pdf("Hello world"), TestContext.Current.CancellationToken);

        // Assert
        result.Text.Should().Contain("Hello world");
    }

    [Theory]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { (byte)'\r', (byte)'\n', (byte)' ', (byte)'\t' })]
    public void Extract_PaddingAfterEndOfFileMarker_StillReadsTheText(byte[] padding)
    {
        // Arrange
        var pdf = Pdf("Hello world").Concat(padding).ToArray();

        // Act
        var result = _sut.Extract(pdf, TestContext.Current.CancellationToken);

        // Assert
        result.Text.Should().Contain("Hello world");
    }

    [Theory]
    [InlineData(512)]
    [InlineData(4096)]
    [InlineData(100_000)]
    public void Extract_LongNulPaddingAfterEndOfFileMarker_StillReadsTheText(int length)
    {
        // Arrange
        var pdf = Pdf("Hello world").Concat(new byte[length]).ToArray();

        // Act
        var result = _sut.Extract(pdf, TestContext.Current.CancellationToken);

        // Assert
        result.Text.Should().Contain("Hello world");
    }

    [Fact]
    public void Extract_PdfWithoutText_Throws()
    {
        // Arrange
        using var builder = new PdfDocumentBuilder();
        builder.AddPage(595, 842);

        // Act
        var act = () => _sut.Extract(builder.Build(), TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*no text*");
    }

    [Fact]
    public void Extract_NotAPdf_Throws()
    {
        // Act
        var act = () => _sut.Extract([1, 2, 3, 4], TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*could not be read*");
    }

    [Fact]
    public void Extract_MorePagesThanTheLimit_StopsWithANotice()
    {
        // Arrange
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 0; i < PdfPigTextExtractor.MaxPages + 1; i++)
        {
            builder.AddPage(200, 200).AddText("page", 12, new PdfPoint(10, 100), font);
        }

        // Act
        var result = _sut.Extract(builder.Build(), TestContext.Current.CancellationToken);

        // Assert
        result.Notice.Should().Contain($"first {PdfPigTextExtractor.MaxPages} of {PdfPigTextExtractor.MaxPages + 1}");
    }

    private static byte[] Pdf(string text)
    {
        using var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(595, 842);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(text, 12, new PdfPoint(50, 700), font);
        return builder.Build();
    }
}
