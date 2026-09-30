using JiMes.Api.Infrastructure.Settings;

namespace JiMes.Api.Tests;

public sealed class SettingValueTests
{
    [Theory]
    [InlineData("INT", " 42 ", null, null, "42")]
    [InlineData("DECIMAL", "0.10", 0, 1, "0.10")]
    [InlineData("BOOL", "TRUE", null, null, "true")]
    [InlineData("BOOL", "0", null, null, "false")]
    [InlineData("TIME", "8:00", null, null, "08:00")]
    [InlineData("TIME", "08:30:15", null, null, "08:30:15")]
    [InlineData("JSON", "[\"INSPECTION_REPORT\"]", null, null, "[\"INSPECTION_REPORT\"]")]
    [InlineData("PATH", @" D:\MES\Files ", null, null, @"D:\MES\Files")]
    [InlineData("STRING", "", null, null, "")]
    public void Valid_values_are_normalized(string type, string raw, int? min, int? max, string expected)
    {
        var value = SettingValue.Normalize(type, raw, (decimal?)min, (decimal?)max, out var error);
        Assert.Null(error);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("INT", "1.5", null, null)]
    [InlineData("INT", "5", 10, 3600)]
    [InlineData("INT", "4000", 10, 3600)]
    [InlineData("DECIMAL", "abc", null, null)]
    [InlineData("BOOL", "yes", null, null)]
    [InlineData("TIME", "25:00", null, null)]
    [InlineData("JSON", "{bad", null, null)]
    [InlineData("PATH", "  ", null, null)]
    [InlineData("UNKNOWN", "x", null, null)]
    public void Invalid_values_are_rejected(string type, string raw, int? min, int? max)
    {
        Assert.Null(SettingValue.Normalize(type, raw, (decimal?)min, (decimal?)max, out var error));
        Assert.NotNull(error);
    }
}
