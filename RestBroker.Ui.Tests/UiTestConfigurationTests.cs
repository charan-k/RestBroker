using NUnit.Framework;

namespace RestBroker.Ui.Tests;

[TestFixture]
public sealed class UiTestConfigurationTests
{
    [Test]
    public void Load_WhenBaseUrlIsMissing_FailsWithoutFallback()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => UiTestConfiguration.Load(_ => null));

        Assert.That(exception!.Message, Does.Contain(UiTestConfiguration.BaseUrlVariable));
        Assert.That(exception.Message, Does.Contain("never defaulted"));
    }

    [TestCase("https://automationintesting.online")]
    [TestCase("http://automationintesting.online")]
    [TestCase("http://automationintesting.online.evil.test")]
    [TestCase("http://192.0.2.10:8080")]
    [TestCase("http://127.0.0.1:8080/path")]
    [TestCase("http://user:password@127.0.0.1:8080")]
    [TestCase("http://127.0.0.1:8080?target=external")]
    public void Load_WhenTargetIsNotPrivateLoopback_FailsWithVariableName(string baseUrl)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => UiTestConfiguration.Load(ReadFrom(baseUrl)));

        Assert.That(exception!.Message, Does.Contain(UiTestConfiguration.BaseUrlVariable));
        Assert.That(exception.Message, Does.Not.Contain("password"));
    }

    [TestCase("http://127.0.0.1:35123")]
    [TestCase("http://[::1]:35123")]
    [TestCase("http://localhost:35123")]
    public void Load_WithPrivateLoopbackUrl_ReturnsConfiguration(string baseUrl)
    {
        var configuration = UiTestConfiguration.Load(ReadFrom(baseUrl));

        Assert.That(configuration.BaseUrl, Is.EqualTo(new Uri(baseUrl)));
        Assert.That(configuration.ToString(), Does.Not.Contain(baseUrl));
    }

    private static Func<string, string?> ReadFrom(string value) =>
        name => name == UiTestConfiguration.BaseUrlVariable ? value : null;
}
