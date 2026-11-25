namespace Store.Api.Tests;

public sealed class LimitsTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(int.MinValue, false)]
    public void Rating_must_be_between_one_and_five(int rating, bool valid)
    {
        Assert.Equal(valid, Limits.ValidateReview(rating, "ok") is null);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("ok", true)]
    public void Comment_must_have_visible_text(string? comment, bool valid)
    {
        Assert.Equal(valid, Limits.ValidateReview(4, comment) is null);
    }

    [Fact]
    public void Comment_length_is_counted_in_utf16_code_units()
    {
        var emoji = "\U0001F600"; // two UTF-16 code units

        Assert.Null(Limits.ValidateReview(4, new string('x', 500)));
        Assert.NotNull(Limits.ValidateReview(4, new string('x', 501)));
        Assert.Null(Limits.ValidateReview(4, string.Concat(Enumerable.Repeat(emoji, 250))));
        Assert.NotNull(Limits.ValidateReview(4, string.Concat(Enumerable.Repeat(emoji, 251))));
    }
}
