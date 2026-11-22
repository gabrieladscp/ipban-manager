using IPBanManager.Core.Models;
using IPBanManager.Core.Validation;
using Xunit;

namespace IPBanManager.Tests.Core;

[Trait("Category", "Unit")]
public class RuleEntriesTests
{
    [Fact]
    public void FirewallRule_ValidForm_ProducesIPBanLine()
    {
        var error = FirewallRuleEntry.Create("RedisAllowIP", true, "10.0.0.1, 192.168.1.0/24", "6379", ".", out var entry);

        Assert.Null(error);
        Assert.Equal("RedisAllowIP;allow;10.0.0.1,192.168.1.0/24;6379;.", entry.Format());
    }

    [Fact]
    public void FirewallRule_OfficialWideRangeExample_IsAccepted()
    {
        var error = FirewallRuleEntry.Create("WebOnly", false, "0.0.0.0/1,128.0.0.0/1,::/1,8000::/1", "22,80,443,3389", "linux", out var entry);

        Assert.Null(error);
        Assert.Equal("WebOnly;block;0.0.0.0/1,128.0.0.0/1,::/1,8000::/1;22,80,443,3389;linux", entry.Format());
    }

    [Theory]
    [InlineData("nome com espaço", "10.0.0.1", "80", "Nome da regra")]
    [InlineData("", "10.0.0.1", "80", "Nome da regra")]
    [InlineData("Regra", "", "80", "ao menos um IP")]
    [InlineData("Regra", "10.0.0.300", "80", "10.0.0.300")]
    [InlineData("Regra", "exemplo.com", "80", "exemplo.com")]
    [InlineData("Regra", "10.0.0.1", "70000", "Porta inválida")]
    [InlineData("Regra", "10.0.0.1", "0", "Porta inválida")]
    [InlineData("Regra", "10.0.0.1", "90-80", "invertida")]
    [InlineData("Regra", "10.0.0.1", "80,,443", "Porta inválida")]
    public void FirewallRule_InvalidForm_ExplainsTheProblem(string name, string ips, string ports, string expected)
    {
        var error = FirewallRuleEntry.Create(name, true, ips, ports, ".", out _);

        Assert.Contains(expected, error);
    }

    [Fact]
    public void FirewallRule_EmptyPorts_MeansAllPorts()
    {
        Assert.Null(FirewallRuleEntry.Create("Tudo", false, "203.0.113.0/24", "", ".", out var entry));
        Assert.Contains("todas as portas", FirewallRuleEntry.Describe(entry.Format()));
    }

    [Fact]
    public void UriRule_ValidForm_ProducesIPBanLine()
    {
        var error = UriRuleEntry.Create("EmergingThreats", "01:00:00:00", "https://rules.emergingthreats.net/fwrules/emerging-Block-IPs.txt", 10000, out var line);

        Assert.Null(error);
        Assert.Equal("EmergingThreats,01:00:00:00,https://rules.emergingthreats.net/fwrules/emerging-Block-IPs.txt,10000", line);
    }

    [Theory]
    [InlineData("Lista", "00:00:00:30", "https://x.com/a.txt", 10, "1 minuto")]
    [InlineData("Lista", "01:00:00:00", "ftp://x.com/a.txt", 10, "https://")]
    [InlineData("Lista", "01:00:00:00", "x.com/a.txt", 10, "https://")]
    [InlineData("Lista", "01:00:00:00", "", 10, "informe")]
    [InlineData("com espaço", "01:00:00:00", "https://x.com/a.txt", 10, "Prefixo")]
    [InlineData("Lista", "01:00:00:00", "https://x.com/a.txt", 0, "Máximo")]
    public void UriRule_InvalidForm_IsRejected(string prefix, string interval, string url, int max, string expected)
    {
        Assert.Contains(expected, UriRuleEntry.Create(prefix, interval, url, max, out _));
    }

    [Fact]
    public void Process_ExistingProgram_ProducesPathPipeArgs()
    {
        var error = ProcessEntry.Create(@"""C:\Scripts\avisar.exe""", "###IPADDRESS### ###USERNAME###", _ => true, out var line);

        Assert.Null(error);
        Assert.Equal(@"C:\Scripts\avisar.exe|###IPADDRESS### ###USERNAME###", line);
    }

    [Theory]
    [InlineData("", "a", "Escolha o programa")]
    [InlineData("avisar.exe", "a", "caminho completo")]
    [InlineData(@"C:\x|y.exe", "a", "caminho completo")]
    [InlineData(@"C:\Scripts\avisar.exe", "a|b", "'|'")]
    public void Process_InvalidForm_IsRejected(string path, string args, string expected)
    {
        Assert.Contains(expected, ProcessEntry.Create(path, args, _ => true, out _));
    }

    [Fact]
    public void Process_MissingProgram_IsRejected()
    {
        Assert.Contains("não existe", ProcessEntry.Create(@"C:\nao\existe.exe", "", _ => false, out _));
    }

    [Fact]
    public void BanTime_SortAndDescribe()
    {
        Assert.Equal(new[] { "00:01:00:00", "01:00:00:00", "07:00:00:00" },
            BanTimeRules.Sort(new[] { "07:00:00:00", "00:01:00:00", "01:00:00:00" }));
        Assert.Equal("1 dia", BanTimeRules.Describe("01:00:00:00"));
        Assert.Equal("2 h 30 min", BanTimeRules.Describe("00:02:30:00"));
        Assert.Equal("bloqueio máximo", BanTimeRules.Describe("00:00:00:00"));
    }

    [Fact]
    public void BanTime_RejectsDuplicatesAndMixingMaximumWithSteps()
    {
        Assert.Contains("já está", BanTimeRules.ValidateNew("01:00:00:00", new[] { "01:00:00:00" }));
        Assert.Contains("sozinho", BanTimeRules.ValidateNew("00:00:00:00", new[] { "01:00:00:00" }));
        Assert.Contains("bloqueio máximo", BanTimeRules.ValidateNew("01:00:00:00", new[] { "00:00:00:00" }));
        Assert.Null(BanTimeRules.ValidateNew("02:00:00:00", new[] { "01:00:00:00" }));
    }

    [Fact]
    public void ConfigurationValidator_RejectsBadPrefixUrlAndEmptyBanTime()
    {
        var config = new IPBanConfiguration { FirewallRulePrefix = "IPBan prefixo", ExternalIPAddressUrl = "meu-ip", BanTime = "" };

        var errors = new ConfigurationValidator().Validate(config);

        Assert.Contains(errors, e => e.Contains("Prefixo das regras"));
        Assert.Contains(errors, e => e.Contains("URL do IP externo"));
        Assert.Contains(errors, e => e.Contains("ao menos um tempo"));
    }

    [Fact]
    public void ListEntryRules_IpAddressKind_RejectsDomain()
    {
        Assert.Contains("domínios não são aceitos", ListEntryRules.Validate(ListEntryKind.IpAddress, "exemplo.com", null, out _));
    }
}
