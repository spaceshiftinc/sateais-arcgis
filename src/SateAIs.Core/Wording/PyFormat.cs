using System.Globalization;
using System.Numerics;
using System.Text;

namespace SateAIs.Core.Wording;

/// <summary>Number formatting that matches the Python clients digit for digit.</summary>
internal static class PyFormat
{
    /// <summary>
    /// Python's <c>f"{value:,.{digits}f}"</c>: round half to even on the exact binary value,
    /// then group thousands. .NET's own formatting rounds exact ties away from zero, which
    /// would make the same credit figure read differently in two clients.
    /// </summary>
    public static string Fixed(double value, int digits)
    {
        if (!double.IsFinite(value))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
        var negative = double.IsNegative(value);
        var (mantissa, exponent) = Decompose(Math.Abs(value));
        var scale = BigInteger.Pow(10, digits);

        BigInteger scaled;
        if (exponent >= 0)
        {
            scaled = mantissa * BigInteger.Pow(2, exponent) * scale;
        }
        else
        {
            var denominator = BigInteger.One << -exponent;
            var quotient = BigInteger.DivRem(mantissa * scale, denominator, out var remainder);
            var twice = remainder * 2;
            if (twice > denominator || (twice == denominator && !quotient.IsEven))
            {
                quotient += 1;
            }
            scaled = quotient;
        }

        var text = scaled.ToString(CultureInfo.InvariantCulture).PadLeft(digits + 1, '0');
        var integerPart = GroupThousands(text[..^digits]);
        var result = digits > 0 ? $"{integerPart}.{text[^digits..]}" : integerPart;
        return negative ? "-" + result : result;
    }

    /// <summary>value = mantissa × 2^exponent, exactly.</summary>
    private static (BigInteger Mantissa, int Exponent) Decompose(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var exponentBits = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xFFFFFFFFFFFFFL;
        if (exponentBits == 0)
        {
            exponentBits = 1; // subnormal
        }
        else
        {
            fraction |= 1L << 52;
        }
        return (fraction, exponentBits - 1075);
    }

    private static string GroupThousands(string digits)
    {
        if (digits.Length <= 3)
        {
            return digits;
        }
        var grouped = new StringBuilder();
        var lead = digits.Length % 3;
        if (lead > 0)
        {
            grouped.Append(digits, 0, lead);
        }
        for (var i = lead; i < digits.Length; i += 3)
        {
            if (grouped.Length > 0)
            {
                grouped.Append(',');
            }
            grouped.Append(digits, i, 3);
        }
        return grouped.ToString();
    }
}
