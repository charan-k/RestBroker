using System.Net;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using RestSharp;

namespace RestBroker.Api.Tests;

[TestFixture]
public sealed class RestfulBookerApiClientTests
{
    private const string SyntheticUsername = "synthetic-user-marker";
    private const string SyntheticPassword = "synthetic-password-marker";
    private const string SyntheticToken = "synthetic-token-marker";

    [Test]
    public async Task AuthenticateAsync_WithValidCredentialsPostsJsonAndReturnsToken()
    {
        using var handler = new StubHttpMessageHandler(request =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo("/auth"));
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.That(body.RootElement.GetProperty("username").GetString(), Is.EqualTo(SyntheticUsername));
            Assert.That(body.RootElement.GetProperty("password").GetString(), Is.EqualTo(SyntheticPassword));
            return JsonResponse(HttpStatusCode.OK, $"{{\"token\":\"{SyntheticToken}\"}}");
        });
        using var client = CreateClient(handler);
        var token = await client.AuthenticateAsync(SyntheticUsername, SyntheticPassword);
        Assert.That(token, Is.EqualTo(SyntheticToken));
    }

    [Test]
    public async Task AuthenticateAsync_WhenServiceRejectsCredentialsFailsWithoutResponseLeakage()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(HttpStatusCode.Unauthorized, $"{{\"reason\":\"{SyntheticPassword}\"}}"));
        using var client = CreateClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.AuthenticateAsync(SyntheticUsername, SyntheticPassword));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("401"));
            Assert.That(exception.Message, Does.Not.Contain(SyntheticUsername));
            Assert.That(exception.Message, Does.Not.Contain(SyntheticPassword));
            Assert.That(exception.Message, Does.Not.Contain(SyntheticToken));
            Assert.That(exception.InnerException, Is.Null);
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task AuthenticateAsync_WhenResponseIsEmptyFails(string responseBody)
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, responseBody));
        using var client = CreateClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.AuthenticateAsync(SyntheticUsername, SyntheticPassword));
        Assert.That(exception!.Message, Does.Contain("empty response"));
    }

    [Test]
    public async Task AuthenticateAsync_WhenResponseIsInvalidJsonFailsWithoutEchoingBody()
    {
        var responseBody = $"{{malformed {SyntheticToken}";
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, responseBody));
        using var client = CreateClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.AuthenticateAsync(SyntheticUsername, SyntheticPassword));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("invalid JSON"));
            Assert.That(exception.Message, Does.Not.Contain(responseBody));
            Assert.That(exception.Message, Does.Not.Contain(SyntheticToken));
            Assert.That(exception.InnerException, Is.Null);
        });
    }

    [TestCase("{}")]
    [TestCase("{\"token\":null}")]
    [TestCase("{\"token\":\"\"}")]
    [TestCase("{\"token\":\"   \"}")]
    [TestCase("[]")]
    public async Task AuthenticateAsync_WhenResponseHasNoValidTokenFails(string responseBody)
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, responseBody));
        using var client = CreateClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.AuthenticateAsync(SyntheticUsername, SyntheticPassword));
        Assert.That(exception!.Message, Does.Contain("valid token"));
    }

    [Test]
    public async Task AuthenticateAsync_WhenTransportFailsReportsFailureWithoutLeakingData()
    {
        using var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException(SyntheticPassword));
        using var client = CreateClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.AuthenticateAsync(SyntheticUsername, SyntheticPassword));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("request failed"));
            Assert.That(exception.Message, Does.Not.Contain(SyntheticUsername));
            Assert.That(exception.Message, Does.Not.Contain(SyntheticPassword));
            Assert.That(exception.InnerException, Is.Null);
        });
    }

    [TestCase("", "valid-password")]
    [TestCase("valid-username", " ")]
    public async Task AuthenticateAsync_WhenCredentialsAreBlankFailsBeforeSendingRequest(string username, string password)
    {
        var requestCount = 0;
        using var handler = new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            return JsonResponse(HttpStatusCode.OK, $"{{\"token\":\"{SyntheticToken}\"}}");
        });
        using var client = CreateClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(async () => await client.AuthenticateAsync(username, password));
        Assert.That(requestCount, Is.Zero);
    }

    private static RestfulBookerApiClient CreateClient(HttpMessageHandler handler) => new(
        new RestClient(new HttpClient(handler), new RestClientOptions("https://example.test"), disposeHttpClient: true));

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
