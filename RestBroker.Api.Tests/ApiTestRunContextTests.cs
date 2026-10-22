using System.Net;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using RestBroker.Api.Tests.Integration;
using RestSharp;

namespace RestBroker.Api.Tests;

[TestFixture]
public sealed class ApiTestRunContextTests
{
    [Test]
    public async Task CreateAsync_AuthenticatesOnceAndSharesTokenAndClient()
    {
        var requestCount = 0;
        using var handler = new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"token\":\"context-test-token\"}", Encoding.UTF8, "application/json")
            };
        });
        using var apiClient = new RestfulBookerApiClient(CreateRestClient(handler));
        var configuration = CreateConfiguration();
        var context = await ApiTestRunContext.CreateAsync(configuration, apiClient);
        var firstDependentTestToken = context.AuthToken;
        var secondDependentTestToken = context.AuthToken;

        Assert.Multiple(() =>
        {
            Assert.That(context.ApiClient, Is.SameAs(apiClient));
            Assert.That(firstDependentTestToken, Is.EqualTo("context-test-token"));
            Assert.That(secondDependentTestToken, Is.SameAs(firstDependentTestToken));
            Assert.That(requestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CreateAsync_WhenAuthenticationFailsDoesNotCreateContext()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("service detail", Encoding.UTF8, "text/plain")
        });
        using var apiClient = new RestfulBookerApiClient(CreateRestClient(handler));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await ApiTestRunContext.CreateAsync(CreateConfiguration(), apiClient));
        Assert.That(exception!.Message, Does.Contain("503"));
        Assert.That(exception.Message, Does.Not.Contain("service detail"));
    }

    private static ApiTestConfiguration CreateConfiguration()
    {
        using var bookingData = JsonDocument.Parse("{}");
        return new ApiTestConfiguration(new Uri("https://example.test"), "synthetic-context-user", "synthetic-context-password", bookingData.RootElement.Clone());
    }

    private static RestClient CreateRestClient(HttpMessageHandler handler) => new(
        new HttpClient(handler), new RestClientOptions("https://example.test"), disposeHttpClient: true);

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
