using System.Linq;
using IPBanManager.Core.Updates;
using Xunit;

namespace IPBanManager.Tests.Core;

[Trait("Category", "Unit")]
public class SemVerTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, false)]
    [InlineData("v1.2.3", 1, 2, 3, false)]
    [InlineData("0.0.1", 0, 0, 1, false)]
    [InlineData("1.0.0-beta.1", 1, 0, 0, true)]
    [InlineData("1.0.0-beta.1+build.5", 1, 0, 0, true)]
    [InlineData("10.20.30", 10, 20, 30, false)]
    public void TryParse_ValidVersions_ParsesParts(string text, int major, int minor, int patch, bool pre)
    {
        Assert.True(SemVer.TryParse(text, out var v));
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(patch, v.Patch);
        Assert.Equal(pre, v.IsPrerelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]       // zero à esquerda não é permitido
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-beta..1")]
    [InlineData("a.b.c")]
    [InlineData("1.2.3-01")]     // identificador numérico com zero à esquerda
    public void TryParse_InvalidVersions_ReturnsFalse(string text) =>
        Assert.False(SemVer.TryParse(text, out _));

    [Fact]
    public void Compare_FollowsTheSemverSpecificationExample()
    {
        // Exemplo oficial da especificação 2.0, em ordem crescente de precedência.
        var ordered = new[]
        {
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
            "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0"
        };
        var parsed = ordered.Select(SemVer.Parse).ToList();

        for (int i = 0; i < parsed.Count - 1; i++)
            Assert.True(parsed[i] < parsed[i + 1], $"{ordered[i]} deveria ser menor que {ordered[i + 1]}");
    }

    [Theory]
    [InlineData("1.10.0", "1.9.0")]   // comparação numérica, não textual
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("1.0.0", "1.0.0-rc.1")]
    public void Compare_GreaterVersion_IsGreater(string greater, string lesser) =>
        Assert.True(SemVer.Parse(greater) > SemVer.Parse(lesser));

    [Fact]
    public void Compare_BuildMetadata_IsIgnored() =>
        Assert.Equal(SemVer.Parse("1.0.0+abc"), SemVer.Parse("1.0.0+xyz"));

    [Fact]
    public void Compare_HugeNumericPrereleaseIdentifiers_DoNotOverflow() =>
        Assert.True(SemVer.Parse("1.0.0-beta.99999999999999999999") > SemVer.Parse("1.0.0-beta.9"));

    [Theory]
    [InlineData("1.0.0", UpdateChannel.Stable)]
    [InlineData("1.0.0-beta.3", UpdateChannel.Beta)]
    [InlineData("2.1.0-rc.1", UpdateChannel.Beta)]
    public void Channel_IsDerivedFromPrerelease(string text, UpdateChannel expected) =>
        Assert.Equal(expected, SemVer.Parse(text).Channel);

    [Fact]
    public void ToString_DropsBuildMetadataAndKeepsPrerelease() =>
        Assert.Equal("1.2.3-beta.4", SemVer.Parse("v1.2.3-beta.4+sha.abc").ToString());

    [Fact]
    public void Parse_Invalid_ThrowsFormatException() =>
        Assert.Throws<System.FormatException>(() => SemVer.Parse("não-é-versão"));
}
