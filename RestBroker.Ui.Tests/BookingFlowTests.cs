using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using System.Collections.Concurrent;
using System.Web;

namespace RestBroker.Ui.Tests;

[TestFixture]
[NonParallelizable]
public sealed class BookingFlowTests : PageTest
{
    private UiTestConfiguration _configuration = null!;
    private readonly ConcurrentQueue<string> _browserDiagnostics = new();
    private bool _traceStarted;
    private bool _captureReservationDiagnostics;

    [SetUp]
    public void ConfigurePrivateUiTarget()
    {
        _configuration = UiTestConfiguration.FromEnvironment();
    }

    [Test]
    public async Task GuestCanBrowseRoomsAndConfirmBooking()
    {
        await Context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });
        _traceStarted = true;

        await Page.GotoAsync(_configuration.BaseUrl.ToString());

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Our Rooms" }))
            .ToBeVisibleAsync();

        var roomCards = Page.Locator("#rooms .room-card");
        await Expect(roomCards.First).ToBeVisibleAsync();
        var roomCount = await roomCards.CountAsync();
        Assert.That(roomCount, Is.GreaterThan(0), "The room list should contain at least one named room.");

        for (var index = 0; index < roomCount; index++)
        {
            var roomCard = roomCards.Nth(index);
            Assert.That(await roomCard.GetByRole(AriaRole.Heading, new() { Level = 5 }).CountAsync(),
                Is.EqualTo(1), "Every room card should have one room heading.");
            Assert.That(await roomCard.GetByRole(AriaRole.Link, new() { Name = "Book now" }).CountAsync(),
                Is.EqualTo(1), "Every room card should have one booking link.");
            Assert.That(await roomCard.GetByText("per night", new() { Exact = true }).CountAsync(),
                Is.EqualTo(1), "Every room card should display its nightly price.");
            Assert.That(await roomCard.Locator(".card-body p").CountAsync(), Is.GreaterThan(0),
                "A room summary should include a description.");
            Assert.That(await roomCard.Locator(".card-body span").CountAsync(), Is.GreaterThan(0),
                "A room summary should list room features.");
            Assert.That(await roomCard.GetByText("£", new() { Exact = false }).CountAsync(),
                Is.GreaterThan(0), "Every room card should display its price.");
        }

        var bookingLink = roomCards.First.GetByRole(AriaRole.Link, new() { Name = "Book now" });
        var reservationPath = await bookingLink.GetAttributeAsync("href")
            ?? throw new InvalidOperationException("Room booking link did not have a destination.");
        var query = HttpUtility.ParseQueryString(new Uri(_configuration.BaseUrl, reservationPath).Query);
        var checkIn = query["checkin"]
            ?? throw new InvalidOperationException("Room booking link omitted the check-in date.");
        var checkOut = query["checkout"]
            ?? throw new InvalidOperationException("Room booking link omitted the check-out date.");

        var reservationUri = new Uri(_configuration.BaseUrl, reservationPath);
        var roomId = reservationUri.AbsolutePath.TrimEnd('/').Split('/').Last();
        AttachReservationDiagnostics(roomId);
        _captureReservationDiagnostics = true;

        await bookingLink.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Book This Room" }))
            .ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reserve Now" }).ClickAsync();

        await Page.GetByLabel("Firstname").FillAsync("Synthetic");
        await Page.GetByLabel("Lastname").FillAsync("Automation");
        await Page.GetByLabel("Email").FillAsync($"synthetic-{Guid.NewGuid():N}@example.test");
        await Page.GetByLabel("Phone").FillAsync("01234567890");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reserve Now" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Booking Confirmed" }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Return home" }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByText($"{checkIn} - {checkOut}", new() { Exact = true }))
            .ToBeVisibleAsync();
    }

    [TearDown]
    public async Task CaptureMaskedFailureScreenshot()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status != NUnit.Framework.Interfaces.TestStatus.Failed)
        {
            if (_traceStarted)
            {
                await Context.Tracing.StopAsync();
            }

            return;
        }

        var diagnosticsDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "UiFailureDiagnostics");
        Directory.CreateDirectory(diagnosticsDirectory);

        if (_traceStarted)
        {
            var tracePath = Path.Combine(
                diagnosticsDirectory,
                $"{nameof(BookingFlowTests)}-{Guid.NewGuid():N}.zip");
            await Context.Tracing.StopAsync(new TracingStopOptions { Path = tracePath });
            _browserDiagnostics.Enqueue($"TRACE: {tracePath}");
        }

        _browserDiagnostics.Enqueue($"FINAL URL: {Page.Url}");
        _browserDiagnostics.Enqueue(
            $"STATUS TEXT: {string.Join(" | ", await Page.GetByRole(AriaRole.Status).AllInnerTextsAsync())}");
        _browserDiagnostics.Enqueue(
            $"ALERT TEXT: {string.Join(" | ", await Page.GetByRole(AriaRole.Alert).AllInnerTextsAsync())}");

        var diagnosticsPath = Path.Combine(
            diagnosticsDirectory,
            $"{nameof(BookingFlowTests)}-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(diagnosticsPath, _browserDiagnostics);
        TestContext.Progress.WriteLine($"Browser diagnostics: {diagnosticsPath}");

        var screenshotDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "UiFailureScreenshots");
        Directory.CreateDirectory(screenshotDirectory);
        var screenshotPath = Path.Combine(
            screenshotDirectory,
            $"{nameof(BookingFlowTests)}-{Guid.NewGuid():N}.png");

        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = screenshotPath,
            FullPage = true,
            Mask = [Page.Locator("input"), Page.Locator("textarea")]
        });
    }

    private void AttachReservationDiagnostics(string roomId)
    {
        Page.PageError += (_, error) =>
            _browserDiagnostics.Enqueue($"PAGE ERROR: {error}");

        Page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                _browserDiagnostics.Enqueue($"CONSOLE ERROR: {message.Text}");
            }
        };

        Page.Request += (_, request) =>
        {
            if (IsRoomOrBrandingRequest(request.Url, roomId))
            {
                _browserDiagnostics.Enqueue(
                    $"REQUEST: {request.Method} {request.ResourceType} {GetSafeUrl(request.Url)}");
            }
        };

        Page.Response += (_, response) =>
        {
            if (IsRoomOrBrandingRequest(response.Url, roomId))
            {
                _browserDiagnostics.Enqueue(
                    $"RESPONSE: {response.Status} {GetSafeUrl(response.Url)}");
            }
        };

        Page.RequestFailed += (_, request) =>
        {
            if (_captureReservationDiagnostics)
            {
                _browserDiagnostics.Enqueue(
                    $"REQUEST FAILED: {request.Method} {GetSafeUrl(request.Url)}; failure={request.Failure ?? "Not Found"}");
            }
        };
    }

    private static bool IsRoomOrBrandingRequest(string url, string roomId)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var requestUri))
        {
            return false;
        }

        var path = requestUri.AbsolutePath.TrimEnd('/');
        return path.Equals($"/api/room/{roomId}", StringComparison.Ordinal)
            || path.Equals("/api/branding", StringComparison.Ordinal);
    }

    private static string GetSafeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "[invalid URL]";
        }

        return $"{uri.GetLeftPart(UriPartial.Authority)}{uri.AbsolutePath}";
    }

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
        RecordVideoDir = null
    };
}
