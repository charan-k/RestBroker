using System.Net.Http;
using System.Text.Json;
using RestSharp;

namespace RestBroker.Api.Tests;

public sealed class RestfulBookerApiClient : IDisposable
{
    private readonly RestClient _restClient;

    public RestfulBookerApiClient(Uri baseUrl)
        : this(new RestClient(new RestClientOptions(baseUrl)))
    {
    }

    internal RestfulBookerApiClient(RestClient restClient)
    {
        ArgumentNullException.ThrowIfNull(restClient);
        _restClient = restClient;
    }

    public async Task<string> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("The API username must not be blank.", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("The API password must not be blank.", nameof(password));
        }

        var request = new RestRequest("auth", Method.Post)
            .AddJsonBody(new { username, password });

        RestResponse response;
        try
        {
            response = await _restClient.ExecuteAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                $"API authentication request failed ({exception.GetType().Name}).");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"API authentication request timed out ({exception.GetType().Name}).");
        }

        if (response.ErrorException is { } responseException && (response.StatusCode == 0 || response.IsSuccessful))
        {
            throw new InvalidOperationException(
                $"API authentication request failed ({responseException.GetType().Name}).");
        }

        if (!response.IsSuccessful)
        {
            throw new InvalidOperationException(
                $"API authentication failed with HTTP status {(int)response.StatusCode}.");
        }

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            throw new InvalidOperationException("API authentication returned an empty response.");
        }

        try
        {
            using var document = JsonDocument.Parse(response.Content);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("token", out var tokenElement)
                || tokenElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(tokenElement.GetString()))
            {
                throw new InvalidOperationException(
                    "API authentication response did not contain a valid token.");
            }

            return tokenElement.GetString()!;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("API authentication returned invalid JSON.");
        }
    }

    public Task<RestResponse> CreateBookingAsync(
        JsonElement bookingData,
        CancellationToken cancellationToken = default) =>
        ExecuteBookingRequestAsync(
            new RestRequest("booking", Method.Post).AddStringBody(bookingData.GetRawText(), ContentType.Json),
            cancellationToken);

    public Task<RestResponse> GetBookingAsync(
        int bookingId,
        CancellationToken cancellationToken = default) =>
        ExecuteBookingRequestAsync(
            new RestRequest($"booking/{bookingId}", Method.Get),
            cancellationToken);

    public Task<RestResponse> ReplaceBookingAsync(
        int bookingId,
        JsonElement bookingData,
        string? authToken,
        CancellationToken cancellationToken = default) =>
        ExecuteBookingRequestAsync(
            AddTokenCookie(
                new RestRequest($"booking/{bookingId}", Method.Put)
                    .AddStringBody(bookingData.GetRawText(), ContentType.Json),
                authToken),
            cancellationToken);

    public Task<RestResponse> PatchBookingAsync(
        int bookingId,
        JsonElement bookingData,
        string? authToken,
        CancellationToken cancellationToken = default) =>
        ExecuteBookingRequestAsync(
            AddTokenCookie(
                new RestRequest($"booking/{bookingId}", Method.Patch)
                    .AddStringBody(bookingData.GetRawText(), ContentType.Json),
                authToken),
            cancellationToken);

    public Task<RestResponse> DeleteBookingAsync(
        int bookingId,
        string? authToken,
        CancellationToken cancellationToken = default) =>
        ExecuteBookingRequestAsync(
            AddTokenCookie(new RestRequest($"booking/{bookingId}", Method.Delete), authToken),
            cancellationToken);

    private async Task<RestResponse> ExecuteBookingRequestAsync(
        RestRequest request,
        CancellationToken cancellationToken)
    {
        RestResponse response;
        try
        {
            response = await _restClient.ExecuteAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                $"API booking request failed ({exception.GetType().Name}).");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"API booking request timed out ({exception.GetType().Name}).");
        }

        var responseExceptionType = response.ErrorException?.GetType().Name;
        if (response.StatusCode == 0 || (response.ErrorException is not null && response.IsSuccessful))
        {
            throw new InvalidOperationException(
                $"API booking request failed ({responseExceptionType ?? "UnknownTransportError"}).");
        }

        return response;
    }

    private static RestRequest AddTokenCookie(RestRequest request, string? authToken)
    {
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            request.AddHeader("Cookie", $"token={authToken}");
        }

        return request;
    }

    public void Dispose() => _restClient.Dispose();
}
