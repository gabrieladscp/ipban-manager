using System.Linq;
using IPBanManager.Core.Models;
using IPBanManager.Core.Validation;
using Xunit;

namespace IPBanManager.Tests.Core;

[Trait("Category", "Unit")]
public class TimeSpanFormatTests
{
    [Theory]
    [InlineData("01:00:00:00")]
    [InlineData("00:00:00:15")]
    [InlineData("0:00:00:15")]
    [InlineData(" 01:23:59:59 ")]
    public void IsValid_WellFormedDuration_ReturnsTrue(string text) =>
        Assert.True(TimeSpanFormat.IsValid(text));

    [Theory]
    [InlineData("01:24:00:00")]
    [InlineData("00:00:60:00")]
    [InlineData("1:00:00")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void IsValid_MalformedDuration_ReturnsFalse(string text) =>
        Assert.False(TimeSpanFormat.IsValid(text));

    [Fact]
    public void TryParse_ValidText_ReturnsExpectedTimeSpan()
    {
        var ok = TimeSpanFormat.TryParse("02:03:04:05", out var value);

        Assert.True(ok);
        Assert.Equal(new System.TimeSpan(2, 3, 4, 5), value);
    }
}

[Trait("Category", "Unit")]
public class IpEntryTests
{
    [Theory]
    [InlineData("1.2.3.4")]
    [InlineData("10.0.0.0/8")]
    [InlineData("2001:db8::/32")]
    [InlineData("::1")]
    public void IsIpOrCidr_ValidAddress_ReturnsTrue(string text) =>
        Assert.True(IpEntry.IsIpOrCidr(text));

    [Theory]
    [InlineData("1.2.3.4/33")]
    [InlineData("1.2.3")]            // forma legada aceita por IPAddress.TryParse
    [InlineData("1.2.3.4/")]
    [InlineData("1.2.3.4/8/9")]
    [InlineData("host.example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void IsIpOrCidr_InvalidAddress_ReturnsFalse(string text) =>
        Assert.False(IpEntry.IsIpOrCidr(text));

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("203.0.113.5", false)]
    public void IsLoopbackOrUnspecified_ClassifiesAddress(string text, bool expected) =>
        Assert.Equal(expected, IpEntry.IsLoopbackOrUnspecified(text));

    [Theory]
    [InlineData("10.0.0.0/8", true)]
    [InlineData("0.0.0.0/0", true)]
    [InlineData("192.168.0.0/16", false)]
    [InlineData("203.0.113.5", false)]
    [InlineData("2001::/16", true)]
    [InlineData("2001:db8::/32", false)]
    public void IsWideRange_ClassifiesPrefix(string text, bool expected) =>
        Assert.Equal(expected, IpEntry.IsWideRange(text));

    [Theory]
    [InlineData("1.2.3.4", true)]
    [InlineData("example.com", true)]
    [InlineData("https://host/list.txt", true)]
    [InlineData("1.2.3.4 5.6.7.8", false)]
    [InlineData("a,b", false)]
    [InlineData("a|b", false)]
    [InlineData("  ", false)]
    public void IsListEntry_ChecksSeparators(string text, bool expected) =>
        Assert.Equal(expected, IpEntry.IsListEntry(text));
}

[Trait("Category", "Unit")]
public class ConfigurationValidatorTests
{
    private readonly ConfigurationValidator _validator = new ConfigurationValidator();

    [Fact]
    public void Validate_DefaultConfiguration_HasNoErrors() =>
        Assert.Empty(_validator.Validate(new IPBanConfiguration()));

    [Fact]
    public void Validate_BanTimeDescending_ReportsOrderingError()
    {
        var config = new IPBanConfiguration { BanTime = "01:00:00:00,00:01:00:00" };

        var errors = _validator.Validate(config);

        Assert.Contains(errors, e => e.Contains("crescente"));
    }

    [Fact]
    public void Validate_BanTimeAscendingWithSpaces_HasNoErrors()
    {
        var config = new IPBanConfiguration { BanTime = "00:01:00:00, 01:00:00:00" };

        Assert.Empty(_validator.Validate(config));
    }

    [Fact]
    public void Validate_InvalidRegexAndAttempts_ReportsBoth()
    {
        var config = new IPBanConfiguration { WhitelistRegex = "([", FailedLoginAttemptsBeforeBan = 0 };

        var errors = _validator.Validate(config);

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void Validate_ListEntryWithSeparator_ReportsEntry()
    {
        var config = new IPBanConfiguration();
        config.Blacklist.Add("1.2.3.4 5.6.7.8");

        var errors = _validator.Validate(config);

        Assert.Single(errors);
        Assert.Contains("1.2.3.4 5.6.7.8", errors.Single());
    }

    [Theory]
    [InlineData("ExpireTime")]
    [InlineData("CycleTime")]
    [InlineData("MinimumTimeBetweenFailedLoginAttempts")]
    [InlineData("MinimumTimeBetweenSuccessfulLoginAttempts")]
    public void Validate_InvalidDuration_ReportsError(string property)
    {
        var config = new IPBanConfiguration();
        typeof(IPBanConfiguration).GetProperty(property).SetValue(config, "xx");

        Assert.Single(_validator.Validate(config));
    }

    [Fact]
    public void Validate_EmptyFirewallPrefix_ReportsError()
    {
        var config = new IPBanConfiguration { FirewallRulePrefix = " " };

        Assert.Contains(_validator.Validate(config), e => e.Contains("Prefixo"));
    }
}
