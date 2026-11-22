using System.Collections.Generic;
using System.Linq;
using IPBanManager.Core.Models;
using IPBanManager.Infrastructure;
using Xunit;

namespace IPBanManager.Tests.Infrastructure;

[Trait("Category", "Unit")]
public class ConfigurationMapperTests
{
    [Fact]
    public void ToSettingsThenFromSettings_RoundTripsEveryEditableProperty()
    {
        var original = new IPBanConfiguration
        {
            FailedLoginAttemptsBeforeBan = 9,
            BanTime = "00:01:00:00,01:00:00:00",
            ResetFailedLoginCountForUnbannedIPAddresses = true,
            ClearBannedIPAddressesOnRestart = true,
            UseDefaultBannedIPAddressHandler = false,
            FirewallRulePrefix = "X_",
            Whitelist = new List<string> { "1.1.1.1", "10.0.0.0/8" },
            Blacklist = new List<string> { "2.2.2.2" },
            UserNameWhitelist = new List<string> { "admin" },
            WhitelistRegex = "^10\\.",
            ProcessToRunOnBan = "a.exe|###IPADDRESS###",
        };

        var roundTripped = ConfigurationMapper.FromSettings(ConfigurationMapper.ToSettings(original));

        Assert.Equal(9, roundTripped.FailedLoginAttemptsBeforeBan);
        Assert.Equal(original.BanTime, roundTripped.BanTime);
        Assert.True(roundTripped.ResetFailedLoginCountForUnbannedIPAddresses);
        Assert.False(roundTripped.UseDefaultBannedIPAddressHandler);
        Assert.Equal("X_", roundTripped.FirewallRulePrefix);
        Assert.Equal(original.Whitelist, roundTripped.Whitelist);
        Assert.Equal(original.Blacklist, roundTripped.Blacklist);
        Assert.Equal(original.UserNameWhitelist, roundTripped.UserNameWhitelist);
        Assert.Equal(original.WhitelistRegex, roundTripped.WhitelistRegex);
        Assert.Equal(original.ProcessToRunOnBan, roundTripped.ProcessToRunOnBan);
    }

    [Theory]
    [InlineData("a,b", new[] { "a", "b" })]
    [InlineData(" a , b ,, ", new[] { "a", "b" })]
    [InlineData("a\r\nb\nc", new[] { "a", "b", "c" })]
    [InlineData("", new string[0])]
    public void SplitList_HandlesSeparatorsAndBlanks(string input, string[] expected) =>
        Assert.Equal(expected, ConfigurationMapper.SplitList(input));

    [Fact]
    public void FromSettings_InvalidNumber_FallsBackToDefault()
    {
        var settings = new Dictionary<string, string> { ["FailedLoginAttemptsBeforeBan"] = "not-a-number" };

        Assert.Equal(5, ConfigurationMapper.FromSettings(settings).FailedLoginAttemptsBeforeBan);
    }
}

[Trait("Category", "Unit")]
public class FirewallParsingTests
{
    [Theory]
    [InlineData("1.2.3.4/255.255.255.255", "1.2.3.4")]
    [InlineData("10.0.0.0/255.0.0.0", "10.0.0.0/8")]
    [InlineData("::1/128", "::1")]
    [InlineData("5.5.5.5", "5.5.5.5")]
    public void Normalize_ConvertsMaskToCidr(string input, string expected) =>
        Assert.Equal(expected, FirewallBannedIpProvider.Normalize(input));

    [Fact]
    public void ParseRemoteAddresses_SplitsAndNormalizes()
    {
        var list = FirewallBannedIpProvider.ParseRemoteAddresses("1.1.1.1/255.255.255.255,2.2.2.0/255.255.255.0").ToList();

        Assert.Equal(new[] { "1.1.1.1", "2.2.2.0/24" }, list);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseRemoteAddresses_WildcardOrEmpty_YieldsNothing(string input) =>
        Assert.Empty(FirewallBannedIpProvider.ParseRemoteAddresses(input));

    [Theory]
    [InlineData("IPBan_Block_0", "Automático")]
    [InlineData("ipban_block_12", "Automático")]
    [InlineData("IPBan_GlobalBlacklist_0", "Blacklist")]
    [InlineData("IPBan_GlobalWhitelist_0", null)]
    [InlineData("Other_Rule", null)]
    [InlineData(null, null)]
    public void OriginOf_ClassifiesRuleNames(string name, string expected) =>
        Assert.Equal(expected, FirewallBannedIpProvider.OriginOf(name, "IPBan_Block_", "IPBan_GlobalBlacklist_"));
}
