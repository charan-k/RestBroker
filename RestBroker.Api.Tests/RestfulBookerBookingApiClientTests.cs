using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using RestSharp;

namespace RestBroker.Api.Tests;

[TestFixture]
public sealed class RestfulBookerBookingApiClientTests
{
    private const string ValidToken = "synthetic-valid-token";
    private const string InvalidToken = "synthetic-invalid-token";
    private static readonly JsonElement OriginalBooking = JsonSerializer.SerializeToElement(new
    {
        firstname = "Synthetic",
        lastname = "Original",
        totalprice = 123,
        depositpaid = true,
        bookingdates = new { checkin = "2026-10-01", checkout = "2026-10-05" },
        additionalneeds = "Synthetic request"
    });

    private StubRestfulBookerHandler _handler = null!;
    private RestfulBookerApiClient _client = null!;
    private int _bookingId;

    [SetUp]
    public async Task SetUpAsync()
    {
        _handler = new StubRestfulBookerHandler(ValidToken);
        _client = new RestfulBookerApiClient(new RestClient(
            new HttpClient(_handler),
            new RestClientOptions("https://example.test"),
            disposeHttpClient: true));

        var response = await _client.CreateBookingAsync(OriginalBooking);
        Assert.That(
            response.StatusCode,
            Is.EqualTo(HttpStatusCode.OK),
            "Creating the isolated test booking must succeed.");
        _bookingId = ReadBookingId(response);
        AssertJsonEqual(ReadCreatedBooking(response), OriginalBooking);
        Assert.That(
            _handler.ContainsBooking(_bookingId),
            Is.True,
            "The created test booking must exist in the fake service.");
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        try
        {
            if (_bookingId > 0 && _handler.ContainsBooking(_bookingId))
            {
                var cleanupResponse = await _client.DeleteBookingAsync(_bookingId, ValidToken);
                Assert.That(
                    cleanupResponse.StatusCode,
                    Is.EqualTo(HttpStatusCode.Created),
                    "Cleaning up the isolated test booking must succeed.");
                Assert.That(
                    _handler.ContainsBooking(_bookingId),
                    Is.False,
                    "Cleanup must remove the isolated test booking.");
            }
        }
        finally
        {
            _client.Dispose();
        }
    }

    [Test]
    public async Task CreateBookingAndReadBooking_ReturnsCreatedData()
    {
        var created = await _client.GetBookingAsync(_bookingId);
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertJsonEqual(ReadBookingObject(created), OriginalBooking);
    }

    [Test]
    public async Task GetBooking_WhenIdDoesNotExist_ReturnsNotFound()
    {
        var response = await _client.GetBookingAsync(int.MaxValue);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ReplaceBooking_ReplacesAllFields()
    {
        using var replacementDocument = JsonDocument.Parse("""
            {
              "firstname": "Replacement",
              "lastname": "Complete",
              "totalprice": 456,
              "depositpaid": false,
              "bookingdates": { "checkin": "2026-11-10", "checkout": "2026-11-14" },
              "additionalneeds": "Full replacement"
            }
            """);

        var update = await _client.ReplaceBookingAsync(_bookingId, replacementDocument.RootElement, ValidToken);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var read = await _client.GetBookingAsync(_bookingId);
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertJsonEqual(ReadBookingObject(read), replacementDocument.RootElement);
    }

    [Test]
    public async Task PatchBooking_ChangesOnlySpecifiedFields()
    {
        using var patchDocument = JsonDocument.Parse("""{ "firstname": "Patched", "totalprice": 789 }""");
        var update = await _client.PatchBookingAsync(_bookingId, patchDocument.RootElement, ValidToken);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var read = await _client.GetBookingAsync(_bookingId);
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var actual = JsonDocument.Parse(read.Content!);
        using var expected = JsonDocument.Parse("""
            {
              "firstname": "Patched",
              "lastname": "Original",
              "totalprice": 789,
              "depositpaid": true,
              "bookingdates": { "checkin": "2026-10-01", "checkout": "2026-10-05" },
              "additionalneeds": "Synthetic request"
            }
            """);
        AssertJsonEqual(actual.RootElement, expected.RootElement);
    }

    [TestCase(InvalidToken, TestName = "Put_WithInvalidToken_ReturnsUnauthorized")]
    [TestCase(null, TestName = "Put_WithMissingToken_ReturnsUnauthorized")]
    public async Task ReplaceBooking_WithoutValidToken_ReturnsUnauthorized(string? token)
    {
        var response = await _client.ReplaceBookingAsync(_bookingId, OriginalBooking, token);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase(InvalidToken, TestName = "Patch_WithInvalidToken_ReturnsUnauthorized")]
    [TestCase(null, TestName = "Patch_WithMissingToken_ReturnsUnauthorized")]
    public async Task PatchBooking_WithoutValidToken_ReturnsUnauthorized(string? token)
    {
        var response = await _client.PatchBookingAsync(_bookingId, OriginalBooking, token);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase(InvalidToken, TestName = "Delete_WithInvalidToken_ReturnsUnauthorized")]
    [TestCase(null, TestName = "Delete_WithMissingToken_ReturnsUnauthorized")]
    public async Task DeleteBooking_WithoutValidToken_ReturnsUnauthorized(string? token)
    {
        var response = await _client.DeleteBookingAsync(_bookingId, token);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetBooking_WhenTransportFails_ReportsFailureWithoutDetails()
    {
        _handler.FailNextRequest = true;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _client.GetBookingAsync(_bookingId));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("request failed"));
            Assert.That(exception.Message, Does.Not.Contain("synthetic transport detail"));
        });
    }

    [Test]
    public async Task DeleteBooking_ThenGet_ReturnsNotFound()
    {
        var deletion = await _client.DeleteBookingAsync(_bookingId, ValidToken);
        Assert.That(deletion.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(_handler.ContainsBooking(_bookingId), Is.False);

        var read = await _client.GetBookingAsync(_bookingId);
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static int ReadBookingId(RestResponse response)
    {
        Assert.That(response.Content, Is.Not.Null.And.Not.Empty, "Booking creation must return a response body.");
        using var document = JsonDocument.Parse(response.Content!);
        Assert.That(document.RootElement.TryGetProperty("bookingid", out var bookingId), Is.True);
        return bookingId.GetInt32();
    }

    private static JsonElement ReadBookingObject(RestResponse response)
    {
        Assert.That(response.Content, Is.Not.Null.And.Not.Empty, "Booking read must return a response body.");
        using var document = JsonDocument.Parse(response.Content!);
        return document.RootElement.Clone();
    }

    private static JsonElement ReadCreatedBooking(RestResponse response)
    {
        Assert.That(response.Content, Is.Not.Null.And.Not.Empty, "Booking creation must return a response body.");
        using var document = JsonDocument.Parse(response.Content!);
        return document.RootElement.GetProperty("booking").Clone();
    }

    private static void AssertJsonEqual(JsonElement actual, JsonElement expected)
    {
        var actualNode = JsonNode.Parse(actual.GetRawText());
        var expectedNode = JsonNode.Parse(expected.GetRawText());
        Assert.That(JsonNode.DeepEquals(actualNode, expectedNode), Is.True);
    }

    private sealed class StubRestfulBookerHandler(string validToken) : HttpMessageHandler
    {
        private readonly Dictionary<int, JsonObject> _bookings = new();
        private int _nextBookingId = 1000;

        public bool FailNextRequest { get; set; }

        public bool ContainsBooking(int bookingId) => _bookings.ContainsKey(bookingId);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (FailNextRequest)
            {
                FailNextRequest = false;
                throw new HttpRequestException("synthetic transport detail");
            }

            var segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            if (segments.Length == 1 && segments[0] == "booking" && request.Method == HttpMethod.Post)
            {
                var booking = await ReadRequestObjectAsync(request, cancellationToken);
                var bookingId = ++_nextBookingId;
                _bookings.Add(bookingId, booking);
                return JsonResponse(HttpStatusCode.OK, new JsonObject
                {
                    ["bookingid"] = bookingId,
                    ["booking"] = booking.DeepClone()
                }.ToJsonString());
            }

            if (segments.Length != 2 || segments[0] != "booking" || !int.TryParse(segments[1], out var id))
            {
                return JsonResponse(HttpStatusCode.NotFound, "Not Found");
            }

            if (request.Method == HttpMethod.Get)
            {
                return _bookings.TryGetValue(id, out var booking)
                    ? JsonResponse(HttpStatusCode.OK, booking.ToJsonString())
                    : JsonResponse(HttpStatusCode.NotFound, "Not Found");
            }

            if (request.Method == HttpMethod.Put
                || request.Method == HttpMethod.Patch
                || request.Method == HttpMethod.Delete)
            {
                if (!HasValidToken(request))
                {
                    return JsonResponse(HttpStatusCode.Unauthorized, "Unauthorized");
                }

                if (!_bookings.TryGetValue(id, out var existing))
                {
                    return JsonResponse(HttpStatusCode.NotFound, "Not Found");
                }

                if (request.Method == HttpMethod.Delete)
                {
                    _bookings.Remove(id);
                    return JsonResponse(HttpStatusCode.Created, "Created");
                }

                var update = await ReadRequestObjectAsync(request, cancellationToken);
                _bookings[id] = request.Method == HttpMethod.Put ? update : Merge(existing, update);
                return JsonResponse(HttpStatusCode.OK, "");
            }

            return JsonResponse(HttpStatusCode.NotFound, "Not Found");
        }

        private static async Task<JsonObject> ReadRequestObjectAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonNode.Parse(content) as JsonObject
                ?? throw new InvalidOperationException("Expected a JSON booking object.");
        }

        private bool HasValidToken(HttpRequestMessage request) =>
            request.Headers.TryGetValues("Cookie", out var cookies)
            && cookies.Contains($"token={validToken}");

        private static JsonObject Merge(JsonObject existing, JsonObject patch)
        {
            var result = (JsonObject)existing.DeepClone();
            foreach (var (key, value) in patch)
            {
                if (value is JsonObject patchObject && result[key] is JsonObject existingObject)
                {
                    result[key] = Merge(existingObject, patchObject);
                }
                else
                {
                    result[key] = value?.DeepClone();
                }
            }

            return result;
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body)
        {
            var contentType = body.StartsWith('{') || body.StartsWith('[')
                ? "application/json"
                : "text/plain";
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType)
            };
        }
    }
}
