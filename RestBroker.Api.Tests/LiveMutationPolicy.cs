using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RestBroker.Api.Tests;

public sealed record LiveMutationOptions(Uri BaseUrl, string Username, string Password, JsonElement BookingData)
{
    public const string EnabledVariable = "RESTBOOKER_LIVE_API_MUTATIONS_ENABLED";
    public const string SerializationVariable = "RESTBOOKER_LIVE_MUTATION_SERIALIZATION";
    public const string RecoveryReporterVariable = "RESTBOOKER_ORPHAN_RECOVERY_REPORTER_READY";

    public override string ToString() =>
        $"{nameof(LiveMutationOptions)} {{ {nameof(BaseUrl)} = [redacted], {nameof(Username)} = [redacted], {nameof(Password)} = [redacted], {nameof(BookingData)} = [redacted] }}";

    public static LiveMutationOptions Load(
        Func<string, string?> getEnvironmentVariable,
        bool orphanRecoveryReporterAvailable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        if (!orphanRecoveryReporterAvailable)
        {
            throw new InvalidOperationException(
                "Live API mutations are disabled until the service-owner-approved orphan recovery reporter is configured.");
        }

        RequireExactValue(getEnvironmentVariable, EnabledVariable, "true");
        RequireExactValue(getEnvironmentVariable, RecoveryReporterVariable, "true");
        RequireExactValue(getEnvironmentVariable, SerializationVariable, "restbroker-live-api-mutations");
        RequireExactValue(getEnvironmentVariable, "RESTBOOKER_LIVE_MUTATION_ENVIRONMENT", "restbroker-live-api-mutations");
        RequireExactValue(getEnvironmentVariable, "GITHUB_ACTIONS", "true");
        RequireExactValue(getEnvironmentVariable, "GITHUB_EVENT_NAME", "workflow_dispatch");

        var defaultBranch = GetRequiredValue(getEnvironmentVariable, "GITHUB_DEFAULT_BRANCH");
        var reference = GetRequiredValue(getEnvironmentVariable, "GITHUB_REF");
        if (!string.Equals(reference, $"refs/heads/{defaultBranch}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Live API mutations are allowed only from the repository default branch.");
        }

        var runId = GetRequiredValue(getEnvironmentVariable, "GITHUB_RUN_ID");
        var runAttempt = GetRequiredValue(getEnvironmentVariable, "GITHUB_RUN_ATTEMPT");
        if (!IsPositiveInteger(runId) || !IsPositiveInteger(runAttempt))
        {
            throw new InvalidOperationException("GitHub run ID and attempt must be positive integers.");
        }

        var baseUrlText = GetRequiredValue(getEnvironmentVariable, ApiTestConfiguration.ApiBaseUrlVariable);
        if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUrl)
            || baseUrl != new Uri("https://restful-booker.herokuapp.com/"))
        {
            throw new InvalidOperationException(
                $"Environment variable '{ApiTestConfiguration.ApiBaseUrlVariable}' must target the approved HTTPS API host.");
        }

        var username = GetRequiredValue(getEnvironmentVariable, ApiTestConfiguration.UsernameVariable);
        var password = GetRequiredValue(getEnvironmentVariable, ApiTestConfiguration.PasswordVariable);
        var bookingDataText = GetRequiredValue(getEnvironmentVariable, ApiTestConfiguration.BookingDataVariable);
        JsonElement bookingData;
        try
        {
            using var document = JsonDocument.Parse(bookingDataText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Environment variable '{ApiTestConfiguration.BookingDataVariable}' must contain a JSON object.");
            }

            bookingData = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Environment variable '{ApiTestConfiguration.BookingDataVariable}' must contain valid JSON.",
                exception);
        }

        return new LiveMutationOptions(baseUrl, username, password, bookingData);
    }

    public static string CreateRunMarker() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public JsonElement CreateBookingDataWithMarker(string marker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);
        var booking = JsonNode.Parse(BookingData.GetRawText()) as JsonObject
            ?? throw new InvalidOperationException("Configured booking data must be a JSON object.");
        booking["additionalneeds"] = $"synthetic-run-marker:{marker}";
        using var document = JsonDocument.Parse(booking.ToJsonString());
        return document.RootElement.Clone();
    }

    private static void RequireExactValue(
        Func<string, string?> getEnvironmentVariable,
        string name,
        string expected)
    {
        if (!string.Equals(getEnvironmentVariable(name), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Live API mutation gate '{name}' is not enabled with its required value.");
        }
    }

    private static string GetRequiredValue(Func<string, string?> getEnvironmentVariable, string name)
    {
        var value = getEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required environment variable '{name}' is not set or is blank.");
        }

        return value;
    }

    private static bool IsPositiveInteger(string value) =>
        long.TryParse(value, out var parsed) && parsed > 0;
}
