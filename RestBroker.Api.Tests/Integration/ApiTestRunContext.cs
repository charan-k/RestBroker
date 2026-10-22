using NUnit.Framework;

namespace RestBroker.Api.Tests.Integration;

public sealed class ApiTestRunContext
{
    private ApiTestRunContext(RestfulBookerApiClient apiClient, string authToken)
    {
        ApiClient = apiClient;
        AuthToken = authToken;
    }

    public RestfulBookerApiClient ApiClient { get; }

    public string AuthToken { get; }

    public static async Task<ApiTestRunContext> CreateAsync(
        ApiTestConfiguration configuration,
        RestfulBookerApiClient apiClient,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(apiClient);

        var authToken = await apiClient.AuthenticateAsync(
            configuration.Username,
            configuration.Password,
            cancellationToken);

        return new ApiTestRunContext(apiClient, authToken);
    }
}

[SetUpFixture]
public sealed class ApiTestRunSetup
{
    private RestfulBookerApiClient? _apiClient;

    public static ApiTestRunContext? Current { get; private set; }

    [OneTimeSetUp]
    public async Task InitializeAsync()
    {
        var configuration = ApiTestConfiguration.FromEnvironment();
        _apiClient = new RestfulBookerApiClient(configuration.BaseUrl);

        try
        {
            Current = await ApiTestRunContext.CreateAsync(configuration, _apiClient);
        }
        catch
        {
            _apiClient.Dispose();
            _apiClient = null;
            throw;
        }
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        Current = null;
        _apiClient?.Dispose();
        _apiClient = null;
    }
}
