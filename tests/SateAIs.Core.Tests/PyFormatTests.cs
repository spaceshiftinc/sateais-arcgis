using SateAIs.Core.Wording;
using Xunit;

namespace SateAIs.Core.Tests;

/// <summary>Where .NET and Python formatting would otherwise disagree: exact binary ties.</summary>
public class PyFormatTests
{
    [Theory]
    [InlineData(0.125, 2, "0.12")]
    [InlineData(2.5, 0, "2")]
    [InlineData(1.5, 0, "2")]
    [InlineData(0.5, 0, "0")]
    [InlineData(1.005, 2, "1.00")]
    [InlineData(12.345, 2, "12.35")]
    [InlineData(99.999, 2, "100.00")]
    [InlineData(1234.5, 2, "1,234.50")]
    [InlineData(1234567.891, 2, "1,234,567.89")]
    [InlineData(30000, 0, "30,000")]
    [InlineData(0, 2, "0.00")]
    [InlineData(-0.001, 2, "-0.00")]
    [InlineData(-1234.567, 1, "-1,234.6")]
    [InlineData(139.5, 6, "139.500000")]
    public void Rounds_half_to_even_and_groups_thousands(double value, int digits, string expected)
    {
        Assert.Equal(expected, PyFormat.Fixed(value, digits));
    }
}
