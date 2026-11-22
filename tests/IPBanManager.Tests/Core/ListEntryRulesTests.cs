using IPBanManager.Core.Validation;
using Xunit;

namespace IPBanManager.Tests.Core;

[Trait("Category", "Unit")]
public class ListEntryRulesTests
{
    private static string Normalize(string text, params string[] existing)
    {
        var error = ListEntryRules.Validate(ListEntryKind.Address, text, existing, out var normalized);
        Assert.True(error == null, $"'{text}' deveria ser aceito, mas: {error}");
        return normalized;
    }

    private static string Reject(string text, params string[] existing)
    {
        var error = ListEntryRules.Validate(ListEntryKind.Address, text, existing, out _);
        Assert.True(error != null, $"'{text}' deveria ser recusado.");
        return error;
    }

    [Theory]
    [InlineData("203.0.113.10", "203.0.113.10")]
    [InlineData(" 203.0.113.10 ", "203.0.113.10")]
    [InlineData("198.51.100.0/24", "198.51.100.0/24")]
    [InlineData("10.0.0.0/8", "10.0.0.0/8")]
    [InlineData("Servidor.Exemplo.COM.br", "servidor.exemplo.com.br")]
    [InlineData("exemplo.com", "exemplo.com")]
    [InlineData("2001:DB8::10", "2001:db8::10")]
    [InlineData("2001:db8::/32", "2001:db8::/32")]
    public void Validate_ValidAddress_IsAcceptedAndNormalized(string input, string expected)
    {
        Assert.Equal(expected, Normalize(input));
    }

    [Theory]
    [InlineData("203.0.113")]          // octetos faltando
    [InlineData("203.0.113.256")]      // octeto > 255
    [InlineData("203.0.113.10.5")]     // octetos demais
    [InlineData("203.0..10")]          // octeto vazio
    [InlineData("010.0.113.10")]       // zero à esquerda
    [InlineData("203.0.113.0/33")]     // prefixo > 32
    [InlineData("203.0.113.0/")]       // prefixo vazio
    [InlineData("203.0.113.0/4")]      // faixa grande demais
    [InlineData("127.0.0.1")]          // loopback
    [InlineData("0.0.0.0")]
    [InlineData("exemplo")]            // domínio sem TLD
    [InlineData("-exemplo.com")]       // rótulo começando com hífen
    [InlineData("exemplo.c")]          // TLD de 1 letra
    [InlineData("exemplo.com/24")]     // CIDR em domínio
    [InlineData("2001:db8::zz")]
    [InlineData("::1")]
    [InlineData("1.2.3.4 5")]          // espaço
    [InlineData("1.2.3.4,5.6.7.8")]    // vírgula
    [InlineData("")]
    public void Validate_InvalidAddress_IsRejected(string input)
    {
        Reject(input);
    }

    [Fact]
    public void Validate_InvalidIPv4_ExplainsTheExpectedFormat()
    {
        Assert.Contains("4 números de 0 a 255", Reject("203.0.113.300"));
    }

    [Fact]
    public void Validate_Duplicate_IsRejectedIgnoringCase()
    {
        Assert.Contains("já está na lista", Reject("Exemplo.com", "exemplo.com"));
        Assert.Contains("já está na lista", Reject("203.0.113.10", "203.0.113.10"));
    }

    [Theory]
    [InlineData('7', true)]
    [InlineData('.', true)]
    [InlineData('/', true)]
    [InlineData(':', true)]
    [InlineData('-', true)]
    [InlineData('a', true)]
    [InlineData(' ', false)]
    [InlineData(',', false)]
    [InlineData(';', false)]
    [InlineData('*', false)]
    [InlineData('ç', false)]
    public void IsAllowedChar_Address_OnlyDigitsLettersAndAddressSymbols(char c, bool allowed)
    {
        Assert.Equal(allowed, ListEntryRules.IsAllowedChar(ListEntryKind.Address, c));
    }

    [Fact]
    public void Validate_UserName_AcceptsDomainUsersAndRejectsSpaces()
    {
        Assert.Null(ListEntryRules.Validate(ListEntryKind.UserName, @"EMPRESA\joao.silva", new string[0], out var n));
        Assert.Equal(@"EMPRESA\joao.silva", n);
        Assert.NotNull(ListEntryRules.Validate(ListEntryKind.UserName, "joao silva", new string[0], out _));
    }

    [Fact]
    public void SplitPasted_SplitsOnLinesCommasSemicolonsAndSpaces()
    {
        Assert.Equal(new[] { "1.1.1.1", "2.2.2.2", "3.3.3.3", "a.com" },
            ListEntryRules.SplitPasted("1.1.1.1\r\n2.2.2.2, 3.3.3.3; a.com "));
    }
}
