using System.Text.Json;

namespace RestBroker.Api.Tests;

public sealed record ApiTestConfiguration(
    Uri BaseUrl,
    string Username,
    string Password,
    JsonElement BookingData)
{
    public const string ApiBaseUrlVariable = "RESTBOOKER_API_BASE_URL";
    public const string UsernameVariable = "RESTBOOKER_API_USERNAME";
    public const string PasswordVariable = "RESTBOOKER_API_PASSWORD";
    public const string BookingDataVariable = "RESTBOOKER_API_BOOKING_DATA";

    public override string ToString() =>
        $"{nameof(ApiTestConfiguration)} {{ {nameof(BaseUrl)} = [redacted], {nameof(Username)} = [redacted], {nameof(Password)} = [redacted], {nameof(BookingData)} = [redacted] }}";

    public static ApiTestConfiguration FromEnvironment() => Load(Environment.GetEnvironmentVariable);

    public static ApiTestConfiguration Load(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var baseUrlValue = GetRequiredValue(getEnvironmentVariable, ApiBaseUrlVariable);
        if (baseUrlValue != baseUrlValue.Trim()
            || !Uri.TryCreate(baseUrlValue, UriKind.Absolute, out var baseUrl)
            || (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Environment variable '{ApiBaseUrlVariable}' must be an absolute HTTP or HTTPS URL.");
        }

        var username = GetRequiredValue(getEnvironmentVariable, UsernameVariable);
        var password = GetRequiredValue(getEnvironmentVariable, PasswordVariable);
        var bookingDataJson = GetRequiredValue(getEnvironmentVariable, BookingDataVariable);

        JsonElement bookingData;
        try
        {
            using var document = JsonDocument.Parse(bookingDataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Environment variable '{BookingDataVariable}' must contain a JSON object.");
            }

            bookingData = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Environment variable '{BookingDataVariable}' must contain valid JSON.",
                exception);
        }

        return new ApiTestConfiguration(baseUrl, username, password, bookingData);
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
}
