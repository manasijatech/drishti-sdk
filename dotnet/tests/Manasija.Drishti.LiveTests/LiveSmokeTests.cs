using FluentAssertions;
using Manasija.Drishti.Exceptions;
using Manasija.Drishti.Params;
using Manasija.Drishti.WebSocket;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Manasija.Drishti.LiveTests;

/// <summary>
/// Calls the real API. Runs only when <c>DRISHTI_API_KEY</c> is set (and honours
/// <c>DRISHTI_BASE_URL</c>); otherwise every test is reported as inconclusive.
/// </summary>
/// <remarks>Run with <c>dotnet test --filter TestCategory=Live</c>.</remarks>
[TestClass]
[TestCategory("Live")]
public sealed class LiveSmokeTests
{
    private static DrishtiClient? _client;

    private static DrishtiClient Client => _client ?? throw new AssertInconclusiveException("Set DRISHTI_API_KEY to run live tests.");

    /// <summary>
    /// Sandbox keys allow 10 requests per minute, fewer than this suite makes, so a rate-limited
    /// call waits for the server's Retry-After and is tried again.
    /// </summary>
    private static async Task<T> Paced<T>(Func<Task<T>> call)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (DrishtiApiException ex) when (ex.StatusCode == 429 && attempt < 3)
            {
                var wait = ex.RetryAfter ?? TimeSpan.FromSeconds(60);
                await Task.Delay(wait > TimeSpan.FromSeconds(65) ? TimeSpan.FromSeconds(65) : wait);
            }
        }
    }

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DrishtiConstants.ApiKeyEnvironmentVariable)))
        {
            _client = DrishtiClient.FromEnvironment();
        }
    }

    [ClassCleanup]
    public static void ClassCleanup() => _client?.Dispose();

    [TestMethod]
    [Timeout(200_000)]
    public async Task AccountEndpoints_Respond()
    {
        // Act
        var account = await Paced(() => Client.GetAccountAsync());
        var usage = await Paced(() => Client.GetAccountUsageAsync());
        var limits = await Paced(() => Client.GetAccountLimitsAsync());

        // Assert
        account!.Data!.AccountId.Should().NotBeNullOrWhiteSpace();
        usage.Should().NotBeNull();
        limits.Should().NotBeNull();
    }

    [TestMethod]
    [Timeout(200_000)]
    public async Task ReferenceAndFeedEndpoints_Respond()
    {
        // Act
        var categories = await Paced(() => Client.GetAnnouncementsCategoriesAsync());
        var news = await Paced(() => Client.GetNewsAsync(new NewsParams { Symbols = ["RELIANCE"], Limit = 2 }));
        var metadata = await Paced(() => Client.GetSymbolsMetadataAsync(new SymbolsMetadataParams { Symbols = ["TCS"] }));

        // Assert
        categories!.Data!.Important.Should().NotBeNull();
        (categories.Data.Important.Count + categories.Data.NotImportant.Count).Should().BePositive();
        news!.Data.Count.Should().BeLessThanOrEqualTo(2);
        news.Data.Should().OnlyContain(item => item.Id != null);
        metadata!.Data.Should().Contain(item => item.Symbol == "TCS");
    }

    [TestMethod]
    [Timeout(400_000)]
    public async Task MarketDataEndpoints_AcceptListFiltersAndReturnPages()
    {
        // Arrange
        string[] symbols = ["TCS", "INFY", "RELIANCE"];

        // Act
        var news = await Paced(() => Client.GetNewsAsync(new NewsParams { Symbols = symbols, Limit = 3 }));
        var deals = await Paced(() => Client.GetBlockDealsAsync(new BlockDealsParams { Limit = 1 }));
        var announcements = await Paced(() => Client.GetAnnouncementsAsync(new AnnouncementsParams { Symbols = symbols, Detailed = true, Limit = 1 }));
        var earningsIndex = await Paced(() => Client.GetEarningsIndexAsync(new IndexParams { Symbols = symbols, Limit = 1 }));
        var concallsIndex = await Paced(() => Client.GetConcallsIndexAsync(new IndexParams { Symbols = symbols, Limit = 1 }));
        var upcomingEarnings = await Paced(() => Client.GetUpcomingEarningsAsync(new UpcomingEarningsParams { Limit = 1 }));
        var alerts = await Paced(() => Client.GetAlertsAsync(new AlertsParams { Types = ["52w_high", "price_alert"], Limit = 1 }));
        var ledger = await Paced(() => Client.GetAccountLedgerAsync(1));
        var batchJobs = await Paced(() => Client.GetBatchJobsAsync(1));

        // Assert
        // Multi-company articles join symbols with ':' (for example "TCS:HCLTECH").
        news!.Data.Should().OnlyContain(item => item.Symbol == null || item.Symbol.Split(':').Any(symbols.Contains));
        deals.Should().NotBeNull();
        announcements!.Data.Should().OnlyContain(item => symbols.Contains(item.Symbol));
        earningsIndex!.Data.Should().OnlyContain(item => symbols.Contains(item.Symbol));
        concallsIndex.Should().NotBeNull();
        upcomingEarnings.Should().NotBeNull();
        alerts.Should().NotBeNull();
        ledger.Should().NotBeNull();
        batchJobs.Should().NotBeNull();
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task InvalidKey_IsRejectedWithApiError()
    {
        // Arrange
        using var client = new DrishtiClient(new DrishtiClientOptions
        {
            ApiKey = "invalid-key-for-sdk-test",
            BaseUrl = Client.BaseUrl,
            MaxRetries = 0,
        });

        // Act
        var act = () => client.GetAccountAsync();

        // Assert
        var error = (await act.Should().ThrowAsync<DrishtiApiException>()).Which;
        error.StatusCode.Should().BeOneOf(401, 403);
        error.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    [Timeout(200_000)]
    public async Task WebSocket_EveryEntitledProductIsAcknowledgedAndHeartbeatsArrive()
    {
        // Arrange
        var limits = await Paced(() => Client.GetAccountLimitsAsync());
        var entitled = limits!.WebSocket is { } ws && ws.TryGetProperty("products", out var products)
            ? products.EnumerateArray().Select(p => WebSocketMessages.NormalizeProduct(p.GetString()!)).ToList()
            : [];
        if (entitled.Count == 0)
        {
            Assert.Inconclusive("This API key has no WebSocket products.");
        }

        var acks = new System.Collections.Concurrent.ConcurrentDictionary<string, SubscribedEvent>();
        var errors = new System.Collections.Concurrent.ConcurrentQueue<ErrorEvent>();
        var heartbeat = new TaskCompletionSource<HeartbeatEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = Client.CreateWebSocketSession();
        session
            .OnSubscribed(e => Task.FromResult(acks[e.Product] = e))
            .OnError(e => { errors.Enqueue(e); return Task.CompletedTask; })
            .OnHeartbeat(e => Task.FromResult(heartbeat.TrySetResult(e)))
            .OnOpen(() => Task.FromResult(opened.TrySetResult(true)));
        foreach (var product in entitled)
        {
            await session.SubscribeAsync(new SubscribeOptions(product) { Symbols = ["RELIANCE", "TCS"] });
        }

        // Act: OnOpen fires only after every stored subscription has been acknowledged.
        await session.StartAsync();
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(60));
        var beat = await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(120));

        // Assert
        errors.Should().BeEmpty();
        session.IsConnected.Should().BeTrue();
        acks.Keys.Should().BeEquivalentTo(entitled);
        acks.Values.Should().OnlyContain(ack => !string.IsNullOrWhiteSpace(ack.Tier));
        beat.Should().NotBeNull();
        session.LastHeartbeatReceivedAt.Should().NotBeNull();

        await session.StopAsync();
        session.IsConnected.Should().BeFalse();
    }

    [TestMethod]
    [Timeout(200_000)]
    public async Task WebSocketSubscribe_IsAcknowledgedOrRejectedWithAReason()
    {
        // Arrange
        var account = await Paced(() => Client.GetAccountAsync());
        var entitled = account!.Data!.WebSocketAddons.Any(a => a.Product == "announcements" && a.Enabled);
        var answered = new TaskCompletionSource<WebSocketEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = Client.CreateWebSocketSession(o => o.SubscribeMaxAttempts = 1);
        session.OnSubscribed(e => Task.FromResult(answered.TrySetResult(e)));
        session.OnError(e => Task.FromResult(answered.TrySetResult(e)));
        await session.SubscribeAsync("announcements", ["RELIANCE", "TCS"]);

        // Act
        await session.StartAsync();
        var result = await answered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        if (entitled)
        {
            var ack = result.Should().BeOfType<SubscribedEvent>().Subject;
            ack.Tier.Should().NotBeNullOrWhiteSpace();
            ack.Symbols.Should().BeEquivalentTo("RELIANCE", "TCS");
        }
        else
        {
            result.Should().Match<WebSocketEvent>(e => e is SubscribedEvent || (e is ErrorEvent && ((ErrorEvent)e).Message.Length > 0));
        }
    }
}
