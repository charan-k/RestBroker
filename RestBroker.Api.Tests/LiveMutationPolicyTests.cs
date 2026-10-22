using System.Text.Json;
using NUnit.Framework;

namespace RestBroker.Api.Tests;

[TestFixture]
public sealed class LiveMutationPolicyTests
{
    [Test]
    public void Load_WhenRecoveryReporterIsUnavailable_FailsBeforeReadingCredentials()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => LiveMutationOptions.Load(_ => null, orphanRecoveryReporterAvailable: false));

        Assert.That(exception!.Message, Does.Contain("orphan recovery reporter"));
    }

    [TestCase(LiveMutationOptions.EnabledVariable)]
    [TestCase(LiveMutationOptions.SerializationVariable)]
    [TestCase(LiveMutationOptions.RecoveryReporterVariable)]
    [TestCase("RESTBOOKER_LIVE_MUTATION_ENVIRONMENT")]
    [TestCase("GITHUB_ACTIONS")]
    [TestCase("GITHUB_EVENT_NAME")]
    [TestCase("GITHUB_REF")]
    [TestCase("GITHUB_DEFAULT_BRANCH")]
    [TestCase("GITHUB_RUN_ID")]
    [TestCase("GITHUB_RUN_ATTEMPT")]
    [TestCase(ApiTestConfiguration.ApiBaseUrlVariable)]
    [TestCase(ApiTestConfiguration.UsernameVariable)]
    [TestCase(ApiTestConfiguration.PasswordVariable)]
    [TestCase(ApiTestConfiguration.BookingDataVariable)]
    public void Load_WhenRequiredGateOrConfigurationIsMissing_FailsWithVariableName(string variable)
    {
        var values = ValidValues();
        values.Remove(variable);

        var exception = Assert.Throws<InvalidOperationException>(
            () => LiveMutationOptions.Load(ReadFrom(values), orphanRecoveryReporterAvailable: true));

        Assert.That(exception!.Message, Does.Contain(variable));
    }

    [TestCase("push")]
    [TestCase("pull_request_target")]
    public void Load_WhenWorkflowEventIsNotManual_Fails(string workflowEvent)
    {
        var values = ValidValues();
        values["GITHUB_EVENT_NAME"] = workflowEvent;

        var exception = Assert.Throws<InvalidOperationException>(
            () => LiveMutationOptions.Load(ReadFrom(values), orphanRecoveryReporterAvailable: true));

        Assert.That(exception!.Message, Does.Contain("GITHUB_EVENT_NAME"));
    }

    [Test]
    public void Load_WhenRunIsNotOnDefaultBranch_FailsWithoutExposingConfiguration()
    {
        var values = ValidValues();
        values["GITHUB_REF"] = "refs/heads/feature/test";
        values[ApiTestConfiguration.PasswordVariable] = "private-test-password";

        var exception = Assert.Throws<InvalidOperationException>(
            () => LiveMutationOptions.Load(ReadFrom(values), orphanRecoveryReporterAvailable: true));

        Assert.That(exception!.Message, Does.Contain("default branch"));
        Assert.That(exception.Message, Does.Not.Contain("private-test-password"));
    }

    [TestCase("http://restful-booker.herokuapp.com")]
    [TestCase("https://automationintesting.online")]
    [TestCase("https://restful-booker.herokuapp.com.evil.test")]
    public void Load_WhenApiHostIsNotApproved_Fails(string baseUrl)
    {
        var values = ValidValues();
        values[ApiTestConfiguration.ApiBaseUrlVariable] = baseUrl;

        var exception = Assert.Throws<InvalidOperationException>(
            () => LiveMutationOptions.Load(ReadFrom(values), orphanRecoveryReporterAvailable: true));

        Assert.That(exception!.Message, Does.Contain(ApiTestConfiguration.ApiBaseUrlVariable));
    }

    [Test]
    public void Load_WithApprovedProtectedManualRun_ReturnsRedactedOptions()
    {
        var options = LiveMutationOptions.Load(ReadFrom(ValidValues()), orphanRecoveryReporterAvailable: true);

        Assert.That(options.BaseUrl, Is.EqualTo(new Uri("https://restful-booker.herokuapp.com/")));
        Assert.That(options.ToString(), Does.Not.Contain("private-test-password"));
        Assert.That(options.ToString(), Does.Not.Contain("private-test-username"));
    }

    [Test]
    public void CreateRunMarker_ReturnsUnique128BitHexValues()
    {
        var first = LiveMutationOptions.CreateRunMarker();
        var second = LiveMutationOptions.CreateRunMarker();

        Assert.Multiple(() =>
        {
            Assert.That(first, Does.Match("^[0-9a-f]{32}$"));
            Assert.That(second, Does.Match("^[0-9a-f]{32}$"));
            Assert.That(second, Is.Not.EqualTo(first));
        });
    }

    [Test]
    public void CreateBookingDataWithMarker_UsesSyntheticAdditionalNeedsField()
    {
        var options = LiveMutationOptions.Load(ReadFrom(ValidValues()), orphanRecoveryReporterAvailable: true);
        const string marker = "0123456789abcdef0123456789abcdef";

        var booking = options.CreateBookingDataWithMarker(marker);

        Assert.That(booking.GetProperty("additionalneeds").GetString(),
            Is.EqualTo($"synthetic-run-marker:{marker}"));
    }

    private static Dictionary<string, string?> ValidValues() => new()
    {
        [LiveMutationOptions.EnabledVariable] = "true",
        [LiveMutationOptions.SerializationVariable] = "restbroker-live-api-mutations",
        [LiveMutationOptions.RecoveryReporterVariable] = "true",
        ["RESTBOOKER_LIVE_MUTATION_ENVIRONMENT"] = "restbroker-live-api-mutations",
        ["GITHUB_ACTIONS"] = "true",
        ["GITHUB_EVENT_NAME"] = "workflow_dispatch",
        ["GITHUB_REF"] = "refs/heads/main",
        ["GITHUB_DEFAULT_BRANCH"] = "main",
        ["GITHUB_RUN_ID"] = "123456",
        ["GITHUB_RUN_ATTEMPT"] = "1",
        [ApiTestConfiguration.ApiBaseUrlVariable] = "https://restful-booker.herokuapp.com",
        [ApiTestConfiguration.UsernameVariable] = "private-test-username",
        [ApiTestConfiguration.PasswordVariable] = "private-test-password",
        [ApiTestConfiguration.BookingDataVariable] =
            """{"firstname":"Synthetic","lastname":"Automation","totalprice":100,"depositpaid":false,"bookingdates":{"checkin":"2026-12-01","checkout":"2026-12-03"},"additionalneeds":"synthetic"}"""
    };

    private static Func<string, string?> ReadFrom(IReadOnlyDictionary<string, string?> values) =>
        name => values.TryGetValue(name, out var value) ? value : null;
}
