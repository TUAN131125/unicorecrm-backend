using UnicoreCRM.Crm.Customers.Contracts;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class DirectCustomerPhoneTests
{
    [Theory]
    [InlineData("0912345678", "+84912345678")]
    [InlineData("+84912345678", "+84912345678")]
    [InlineData("84912345678", "+84912345678")]
    [InlineData("(091) 234-56.78", "+84912345678")]
    [InlineData("02838220011", "+842838220011")]
    [InlineData("+842838220011", "+842838220011")]
    [InlineData("+12025550123", "+12025550123")]
    public void Full_numbers_normalize_without_identity_inference(string input, string expected)
        => Assert.Equal(expected, CustomerPhoneComparison.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345678")]
    [InlineData("091234567")]
    [InlineData("+840912345678")]
    [InlineData("09123456789")]
    [InlineData("0912abc678")]
    [InlineData("0912/345678")]
    public void Invalid_numbers_never_produce_a_partial_match(string? input)
        => Assert.Null(CustomerPhoneComparison.Normalize(input));

    [Fact]
    public void Stored_forms_are_complete_and_country_specific()
    {
        Assert.Equal(new[] { "+84912345678", "84912345678", "0912345678" }, CustomerPhoneComparison.ExactStoredForms("+84912345678"));
        Assert.Equal(new[] { "+12025550123" }, CustomerPhoneComparison.ExactStoredForms("+12025550123"));
        Assert.NotEqual(CustomerPhoneComparison.Normalize("0912345678"), CustomerPhoneComparison.Normalize("0812345678"));
    }
}
