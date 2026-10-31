using SiloSupport.Application.Helpers;

namespace SiloSupport.Tests;

public class BaleIdentityTests
{
    [Theory]
    [InlineData("@Ali", "ali")]
    [InlineData("  ali ", "ali")]
    [InlineData("123", "123")]
    [InlineData("@", "")]
    [InlineData(null, "")]
    public void Normalize_TrimsStripsAtSignAndLowersCase(string? input, string expected) =>
        Assert.Equal(expected, BaleIdentity.Normalize(input));

    [Fact]
    public void Variants_AreWithAndWithoutAtSign()
    {
        Assert.Equal(new[] { "ali", "@ali" }, BaleIdentity.Variants("@Ali"));
        Assert.Empty(BaleIdentity.Variants("@"));
    }

    [Fact]
    public void CandidatesFor_CombinesIdAndUserNameWithoutDuplicates()
    {
        Assert.Equal(new[] { "42", "@42", "ali", "@ali" }, BaleIdentity.CandidatesFor(42, "@Ali"));
        Assert.Equal(new[] { "ali", "@ali" }, BaleIdentity.CandidatesFor(null, "ali"));
        Assert.Empty(BaleIdentity.CandidatesFor(null, " "));
    }

    [Fact]
    public void Matchers_DistinguishNumericIdFromUserName()
    {
        Assert.True(BaleIdentity.IsNumericIdMatch("42", 42));
        Assert.False(BaleIdentity.IsNumericIdMatch("@ali", 42));
        Assert.True(BaleIdentity.IsUsernameMatch("@ALI", "ali"));
        Assert.False(BaleIdentity.IsUsernameMatch("@ali", null));
    }
}
