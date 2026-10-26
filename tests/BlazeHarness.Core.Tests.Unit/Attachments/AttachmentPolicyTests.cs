using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Tests.Unit.Attachments;

public sealed class AttachmentPolicyTests
{
    private static readonly ModelConfig Small = new() { Id = "m", ContextWindow = 8000 };

    [Fact]
    public void Plan_Image_IsSentAsAnImageEvenForAHugeFile()
    {
        // Act
        var plan = AttachmentPolicy.Plan([Image("a1", 5_000_000)], Small);

        // Assert
        plan.Should().ContainSingle().Which.Delivery.Should().Be(AttachmentDelivery.Image);
    }

    [Fact]
    public void Plan_SmallText_IsInline()
    {
        // Act
        var plan = AttachmentPolicy.Plan([Text("a1", 1_000)], Small);

        // Assert
        plan.Single().Delivery.Should().Be(AttachmentDelivery.Inline);
    }

    [Fact]
    public void Plan_TextOverATenthOfTheWindow_IsReadOnDemand()
    {
        // Act: 8000 / 10 = 800 tokens, about 3200 bytes.
        var plan = AttachmentPolicy.Plan([Text("a1", 4_000)], Small);

        // Assert
        plan.Single().Delivery.Should().Be(AttachmentDelivery.OnDemand);
    }

    [Fact]
    public void Plan_SeveralTextFiles_ShareOneBudgetAndALargeFileDoesNotUseItUp()
    {
        // Act
        var plan = AttachmentPolicy.Plan([Text("a1", 2_000), Text("a2", 2_000), Text("a3", 100)], Small);

        // Assert
        plan.Select(p => p.Delivery).Should().Equal(AttachmentDelivery.Inline, AttachmentDelivery.OnDemand, AttachmentDelivery.Inline);
    }

    [Fact]
    public void InlineTokenBudget_UnknownWindow_UsesAFixedAmount()
    {
        // Act
        var budget = AttachmentPolicy.InlineTokenBudget(new ModelConfig { Id = "m" });

        // Assert
        budget.Should().Be(4_000);
    }

    [Theory]
    [InlineData(InputTypes.Text, false)]
    [InlineData(InputTypes.Text | InputTypes.Image, true)]
    public void AcceptsImages_Input_MatchesTheImageFlag(InputTypes input, bool expected)
    {
        // Act
        var accepts = AttachmentPolicy.AcceptsImages(new ModelConfig { Id = "m", Input = input });

        // Assert
        accepts.Should().Be(expected);
    }

    private static AttachmentInfo Image(string id, long size) => new(id, id + ".png", "image/png", size, 0);

    private static AttachmentInfo Text(string id, long size) => new(id, id + ".txt", "text/plain", size, 10);
}
