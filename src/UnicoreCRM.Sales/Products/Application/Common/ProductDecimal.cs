using System.Globalization;
using System.Numerics;

namespace UnicoreCRM.Sales.Products.Application.Common;

internal readonly record struct ProductDecimal(BigInteger Unscaled, int Scale)
{
    internal static bool TryParse(string? value, out ProductDecimal result)
    {
        result = default;
        if (string.IsNullOrEmpty(value) || value[0] == '+')
            return false;

        var negative = value[0] == '-';
        var unsigned = negative ? value[1..] : value;
        if (unsigned.Length == 0)
            return false;

        var separator = unsigned.IndexOf('.');
        if (separator != unsigned.LastIndexOf('.'))
            return false;
        var integer = separator < 0 ? unsigned : unsigned[..separator];
        var fraction = separator < 0 ? string.Empty : unsigned[(separator + 1)..];
        if (integer.Length == 0 || integer.Length > 1 && integer[0] == '0' || fraction.Length is > 6 or 0 && separator >= 0)
            return false;
        if (!integer.All(char.IsAsciiDigit) || !fraction.All(char.IsAsciiDigit))
            return false;

        var digits = integer + fraction;
        if (!BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var unscaled))
            return false;
        result = Normalize(new ProductDecimal(negative ? -unscaled : unscaled, fraction.Length));
        return true;
    }

    internal bool IsNegative => Unscaled.Sign < 0;
    internal bool IsZero => Unscaled.IsZero;

    internal static int Compare(ProductDecimal left, ProductDecimal right)
    {
        var scale = Math.Max(left.Scale, right.Scale);
        return (left.Unscaled * Pow10(scale - left.Scale)).CompareTo(right.Unscaled * Pow10(scale - right.Scale));
    }

    internal static ProductDecimal Add(ProductDecimal left, ProductDecimal right)
    {
        var scale = Math.Max(left.Scale, right.Scale);
        return Normalize(new ProductDecimal(
            left.Unscaled * Pow10(scale - left.Scale) + right.Unscaled * Pow10(scale - right.Scale),
            scale));
    }

    internal static ProductDecimal Multiply(ProductDecimal left, ProductDecimal right) =>
        Normalize(new ProductDecimal(left.Unscaled * right.Unscaled, left.Scale + right.Scale));

    internal static ProductDecimal DivideAndRoundHalfUp(
        ProductDecimal numerator,
        ProductDecimal denominator,
        int maximumScale)
    {
        if (denominator.Unscaled.IsZero)
            throw new DivideByZeroException();

        var scaledNumerator = numerator.Unscaled * Pow10(denominator.Scale + maximumScale);
        var scaledDenominator = denominator.Unscaled * Pow10(numerator.Scale);
        var quotient = BigInteger.DivRem(scaledNumerator, scaledDenominator, out var remainder);
        if (BigInteger.Abs(remainder) * 2 >= BigInteger.Abs(scaledDenominator))
            quotient += scaledNumerator.Sign == scaledDenominator.Sign ? BigInteger.One : -BigInteger.One;
        return Normalize(new ProductDecimal(quotient, maximumScale));
    }

    internal static ProductDecimal RoundHalfUp(ProductDecimal value, int maximumScale) =>
        Round(value, maximumScale);

    public override string ToString()
    {
        var value = Normalize(this);
        var sign = value.Unscaled.Sign < 0 ? "-" : string.Empty;
        var digits = BigInteger.Abs(value.Unscaled).ToString(CultureInfo.InvariantCulture);
        if (value.Scale == 0)
            return sign + digits;
        if (digits.Length <= value.Scale)
            digits = digits.PadLeft(value.Scale + 1, '0');
        return sign + digits[..^value.Scale] + "." + digits[^value.Scale..];
    }

    private static ProductDecimal Round(ProductDecimal value, int maximumScale)
    {
        if (value.Scale <= maximumScale)
            return Normalize(value);

        var divisor = Pow10(value.Scale - maximumScale);
        var quotient = BigInteger.DivRem(value.Unscaled, divisor, out var remainder);
        if (BigInteger.Abs(remainder) * 2 >= divisor)
            quotient += value.Unscaled.Sign >= 0 ? BigInteger.One : -BigInteger.One;
        return Normalize(new ProductDecimal(quotient, maximumScale));
    }

    private static ProductDecimal Normalize(ProductDecimal value)
    {
        var unscaled = value.Unscaled;
        var scale = value.Scale;
        while (scale > 0 && unscaled % 10 == 0)
        {
            unscaled /= 10;
            scale--;
        }
        return new ProductDecimal(unscaled, scale);
    }

    private static BigInteger Pow10(int exponent) => BigInteger.Pow(10, exponent);
}
