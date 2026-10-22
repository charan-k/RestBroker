using NUnit.Framework;

namespace RestBroker.Api.Tests;

[TestFixture]
public sealed class ApiTestConfigurationTests
{
    [Test]
    public void Load_WithValidValues_ReturnsConfiguration()
    {
        var configuration = ApiTestConfiguration.Load(ReadFrom(ValidValues()));

        Assert.Multiple(() =>
        {
            Assert.That(configuration.BaseUrl, Is.EqualTo(new Uri("https://example.test")));
            Assert.That(configuration.Username, Is.EqualTo("test-user"));
            Assert.That(configuration.Password, Is.EqualTo("test-password"));
            Assert.That(configuration.BookingData.GetProperty("firstname").GetString(), Is.EqualTo("Test"));
            Assert.That(configuration.ToString(), Does.Not.Contain("test-password"));
            Assert.That(configuration.ToString(), Does.Not.Contain("test-user"));
        });
    }

    [TestCase(ApiTestConfiguration.ApiBaseUrlVariable)]
    [TestCase(ApiTestConfiguration.UsernameVariable)]
    [TestCase(ApiTestConfiguration.PasswordVariable)]
    [TestCase(ApiTestConfiguration.BookingDataVariable)]
    public void Load_WhenRequiredValueIsMissing_FailsWithVariableName(string missingVariable)
    {
        var values = ValidValues();
        values.Remove(missingVariable);

        var exception = Assert.Throws<InvalidOperationException>(
            () => ApiTestConfiguration.Load(ReadFrom(values)));

        Assert.That(exception!.Message, Does.Contain(missingVariable));
    }

    [TestCase(ApiTestConfiguration.UsernameVariable)]
    [TestCase(ApiTestConfiguration.PasswordVariable)]
    public void Load_WhenCredentialIsBlank_FailsWithoutExposingValues(string variable)
    {
        var values = ValidValues();
        values[variable] = "  ";

        var exception = Assert.Throws<InvalidOperationException>(
            () => ApiTestConfiguration.Load(ReadFrom(values)));

        Assert.That(exception!.Message, Does.Contain(variable));
        Assert.That(exception.Message, Does.Not.Contain("test-password"));
    }

    [TestCase("relative/path")]
    [TestCase("ftp://example.test")]
    public void Load_WhenBaseUrlIsInvalid_FailsWithVariableName(string baseUrl)
    {
        var values = ValidValues();
        values[ApiTestConfiguration.ApiBaseUrlVariable] = baseUrl;

        var exception = Assert.Throws<InvalidOperationException>(
            () => ApiTestConfiguration.Load(ReadFrom(values)));

        Assert.That(exception!.Message, Does.Contain(ApiTestConfiguration.ApiBaseUrlVariable));
    }

    [Test]
    public void Load_WhenBookingDataIsMalformedJson_FailsWithVariableName()
    {
        var values = ValidValues();
        values[ApiTestConfiguration.BookingDataVariable] = "{invalid";

        var exception = Assert.Throws<InvalidOperationException>(
            () => ApiTestConfiguration.Load(ReadFrom(values)));

        Assert.That(exception!.Message, Does.Contain(ApiTestConfiguration.BookingDataVariable));
        Assert.That(exception.Message, Does.Not.Contain("{invalid"));
    }

    [TestCase("[]")]
    [TestCase("null")]
    public void Load_WhenBookingDataIsNotAnObject_FailsWithVariableName(string bookingData)
    {
        var values = ValidValues();
        values[ApiTestConfiguration.BookingDataVariable] = bookingData;

        var exception = Assert.Throws<InvalidOperationException>(
            () => ApiTestConfiguration.Load(ReadFrom(values)));

        Assert.That(exception!.Message, Does.Contain(ApiTestConfiguration.BookingDataVariable));
    }

    private static Dictionary<string, string?> ValidValues() => new()
    {
        [ApiTestConfiguration.ApiBaseUrlVariable] = "https://example.test",
        [ApiTestConfiguration.UsernameVariable] = "test-user",
        [ApiTestConfiguration.PasswordVariable] = "test-password",
        [ApiTestConfiguration.BookingDataVariable] = "{\"firstname\":\"Test\"}"
    };

    private static Func<string, string?> ReadFrom(IReadOnlyDictionary<string, string?> values) =>
        name => values.TryGetValue(name, out var value) ? value : null;
}
