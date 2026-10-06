using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using System.Web;

namespace RestBroker.Ui.Tests;

[TestFixture]
[NonParallelizable]
public sealed class BookingFlowTests : PageTest
{
    private UiTestConfiguration _configuration = null!;

    [SetUp]
    public void ConfigurePrivateUiTarget()
    {
        _configuration = UiTestConfiguration.FromEnvironment();
    }

    [Test]
    public async Task GuestCanBrowseRoomsAndConfirmBooking()
    {
        await Page.GotoAsync(_configuration.BaseUrl.ToString());

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Our Rooms" }))
            .ToBeVisibleAsync();

        var roomHeadings = Page.GetByRole(AriaRole.Heading, new() { Level = 5 });
        await Expect(roomHeadings.First).ToBeVisibleAsync();
        var roomCount = await roomHeadings.CountAsync();
        Assert.That(roomCount, Is.GreaterThan(0), "The room list should contain at least one named room.");
        Assert.That(await Page.GetByRole(AriaRole.Link, new() { Name = "Book now" }).CountAsync(),
            Is.EqualTo(roomCount), "Every listed room should have a booking link.");
        Assert.That(await Page.GetByText("per night", new() { Exact = true }).CountAsync(),
            Is.EqualTo(roomCount), "Every listed room should display its nightly price.");

        var roomSummary = roomHeadings.First.Locator("..");
        Assert.That(await roomSummary.Locator("p").CountAsync(), Is.GreaterThan(0),
            "A room summary should include a description.");
        Assert.That(await roomSummary.Locator("span").CountAsync(), Is.GreaterThan(0),
            "A room summary should list room features.");
        Assert.That(await Page.GetByText("£", new() { Exact = false }).CountAsync(),
            Is.GreaterThan(0), "The room list should display nightly prices.");

        var bookingLink = Page.GetByRole(AriaRole.Link, new() { Name = "Book now" }).First;
        var reservationPath = await bookingLink.GetAttributeAsync("href")
            ?? throw new InvalidOperationException("Room booking link did not have a destination.");
        var query = HttpUtility.ParseQueryString(new Uri(_configuration.BaseUrl, reservationPath).Query);
        var checkIn = query["checkin"]
            ?? throw new InvalidOperationException("Room booking link omitted the check-in date.");
        var checkOut = query["checkout"]
            ?? throw new InvalidOperationException("Room booking link omitted the check-out date.");

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
            return;
        }

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

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
        RecordVideoDir = null
    };
}
