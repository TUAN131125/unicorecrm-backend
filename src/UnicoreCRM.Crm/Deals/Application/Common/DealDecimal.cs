using System.Globalization;
using System.Numerics;

namespace UnicoreCRM.Crm.Deals.Application.Common;

internal static class DealDecimal
{
    private const int Scale = 6;
    private static readonly BigInteger ScaleFactor = BigInteger.Pow(10, Scale);

    internal static BigInteger ParseScaled(string value)
    {
        var negative = value.StartsWith("-", StringComparison.Ordinal);
        var unsigned = negative ? value[1..] : value;
        var parts = unsigned.Split('.', 2);
        var whole = BigInteger.Parse(parts[0], CultureInfo.InvariantCulture);
        var fractionText = parts.Length == 1 ? string.Empty : parts[1];
        var fraction = fractionText.Length == 0
            ? BigInteger.Zero
            : BigInteger.Parse(fractionText.PadRight(Scale, '0'), CultureInfo.InvariantCulture);
        var scaled = whole * ScaleFactor + fraction;
        return negative ? -scaled : scaled;
    }

    internal static string Format(BigInteger scaled)
    {
        if (scaled.IsZero)
            return "0";
        var negative = scaled.Sign < 0;
        var absolute = BigInteger.Abs(scaled);
        var whole = BigInteger.DivRem(absolute, ScaleFactor, out var fraction);
        var fractionText = fraction.ToString($"D{Scale}", CultureInfo.InvariantCulture).TrimEnd('0');
        var value = fractionText.Length == 0
            ? whole.ToString(CultureInfo.InvariantCulture)
            : $"{whole.ToString(CultureInfo.InvariantCulture)}.{fractionText}";
        return negative ? $"-{value}" : value;
    }

    internal static BigInteger PercentageOf(BigInteger amount, BigInteger percentage)
    {
        var numerator = amount * percentage;
        var denominator = new BigInteger(100) * ScaleFactor;
        var negative = numerator.Sign < 0;
        var absolute = BigInteger.Abs(numerator);
        var quotient = BigInteger.DivRem(absolute, denominator, out var remainder);
        if (remainder * 2 >= denominator)
            quotient++;
        return negative ? -quotient : quotient;
    }
}
