namespace RestBroker.Ui.Tests;

public sealed record UiTestConfiguration(Uri BaseUrl)
{
    public const string BaseUrlVariable = "RESTBOOKER_UI_BASE_URL";
    private const string SharedPublicUiHost = "automationintesting.online";

    public override string ToString() =>
        $"{nameof(UiTestConfiguration)} {{ {nameof(BaseUrl)} = [private URL redacted] }}";

    public static UiTestConfiguration FromEnvironment() => Load(Environment.GetEnvironmentVariable);

    public static UiTestConfiguration Load(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        var value = getEnvironmentVariable(BaseUrlVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required environment variable '{BaseUrlVariable}' is not set; the private UI target is never defaulted.");
        }

        if (value != value.Trim()
            || !Uri.TryCreate(value, UriKind.Absolute, out var baseUrl)
            || baseUrl.Scheme != Uri.UriSchemeHttp
            || !baseUrl.IsLoopback
            || !string.IsNullOrEmpty(baseUrl.UserInfo)
            || !string.IsNullOrEmpty(baseUrl.Query)
            || !string.IsNullOrEmpty(baseUrl.Fragment)
            || (baseUrl.AbsolutePath != "/" && baseUrl.AbsolutePath.Length != 0)
            || string.Equals(baseUrl.Host, SharedPublicUiHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Environment variable '{BaseUrlVariable}' must be a private loopback HTTP URL without credentials, path, query, or fragment.");
        }

        return new UiTestConfiguration(baseUrl);
    }
}
