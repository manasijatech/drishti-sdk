using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Manasija.Drishti.Exceptions;
using Manasija.Drishti.Models;
using Manasija.Drishti.Tests.Fakes;
using Manasija.Drishti.WebSocket;

namespace Manasija.Drishti.Tests.WebSocket;

[TestClass]
public sealed class WebSocketSessionTests
{
    [TestMethod]
    public void Constructor_BlankApiKey_ThrowsArgumentException()
    {
        // Arrange
        var options = FastOptions();
        options.ApiKey = " ";

        // Act
        var act = () => new DrishtiWebSocketSession(options, new ScriptedConnector());

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public async Task SubscribeAsync_UnknownProduct_ThrowsArgumentException()
    {
        // Arrange
        await using var session = new DrishtiWebSocketSession(FastOptions(), new ScriptedConnector());

        // Act
        var act = () => session.SubscribeAsync("weather");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*weather*");
    }

    [TestMethod]
    public async Task Start_ConnectsWithApiKeyAndSendsStoredSubscriptions()
    {
        // Arrange
        var connector = new ScriptedConnector();
        await using var session = new DrishtiWebSocketSession(FastOptions(), connector);
        var opened = 0;
        session.OnOpen(() => Task.FromResult(Interlocked.Increment(ref opened)));
        await session.SubscribeAsync(new SubscribeOptions("block_deals") { Symbols = [" tcs ", "TCS", "infy"] });

        // Act
        await session.StartAsync();
        await Eventually.True(() => Volatile.Read(ref opened) == 1, "the session opens");

        // Assert
        var attempt = connector.Attempts.Single();
        attempt.Uri.ToString().Should().Be("wss://api.test/v1/ws?api_key=test-key");
        attempt.Headers["X-API-Key"].Should().Be("test-key");
        attempt.Headers["X-Team"].Should().Be("ops");
        var sent = JsonAssert.Parse(connector.Latest.Sent.Single());
        JsonAssert.Equal(JsonAssert.Parse("{\"op\":\"subscribe\",\"product\":\"block-deals\",\"symbols\":[\"TCS\",\"INFY\"],\"detailed\":true}"), sent, "subscribe");
        session.IsConnected.Should().BeTrue();
    }

    [TestMethod]
    public async Task DataEvents_ReachTypedChannelAndGenericHandlersInOrder()
    {
        // Arrange
        var connector = new ScriptedConnector();
        await using var session = new DrishtiWebSocketSession(FastOptions(), connector);
        var received = new ConcurrentQueue<string>();
        session.OnNews(news => received.Enqueue("news:" + news.Title));
        session.OnData(data => { received.Enqueue("data:" + data.Channel); return Task.CompletedTask; });
        session.OnMessage(message => { received.Enqueue("message:" + message.Kind); return Task.CompletedTask; });
        await StartAndWaitForConnectionAsync(session, connector);

        // Act
        connector.Latest.ServerSend("{\"channel\":\"news\",\"data\":{\"id\":\"n1\",\"title\":\"TCS up\"}}");
        await Eventually.True(() => received.Count == 3, "all handlers ran");

        // Assert
        received.Should().Equal("data:news", "message:data", "news:TCS up");
    }

    [TestMethod]
    public async Task HandlerExceptions_DoNotStopLaterEvents()
    {
        // Arrange
        var connector = new ScriptedConnector();
        await using var session = new DrishtiWebSocketSession(FastOptions(), connector);
        var alerts = new ConcurrentQueue<Alert>();
        session.OnAlerts(alert =>
        {
            alerts.Enqueue(alert);
            if (alert.Id == "a1")
            {
                throw new InvalidOperationException("handler bug");
            }
        });
        await StartAndWaitForConnectionAsync(session, connector);

        // Act
        connector.Latest.ServerSend("{\"channel\":\"alerts\",\"data\":{\"id\":\"a1\"}}");
        connector.Latest.ServerSend("{\"channel\":\"alerts\",\"data\":{\"id\":\"a2\",\"alert_type\":\"price_alert\"}}");
        await Eventually.True(() => alerts.Count == 2, "both alerts dispatched");

        // Assert
        alerts.Last().Type.Should().Be("price_alert");
    }

    [TestMethod]
    public async Task SubscribeAsync_WhenConnected_WaitsForAcknowledgement()
    {
        // Arrange
        var connector = new ScriptedConnector();
        await using var session = new DrishtiWebSocketSession(FastOptions(), connector);
        SubscribedEvent? ack = null;
        session.OnSubscribed(e => { ack = e; return Task.CompletedTask; });
        await StartAndWaitForConnectionAsync(session, connector);

        // Act
        await session.SubscribeAsync(new SubscribeOptions("announcements") { Categories = ["Dividend", " Dividend "], Detailed = false });

        // Assert
        var sent = JsonAssert.Parse(connector.Latest.Sent.Last());
        sent.GetProperty("categories").EnumerateArray().Select(c => c.GetString()).Should().Equal("Dividend");
        sent.GetProperty("detailed").GetBoolean().Should().BeFalse();
        await Eventually.True(() => ack is not null, "the ack is dispatched");
        ack!.Product.Should().Be("announcements");
        session.Subscriptions.Should().ContainSingle(s => s.Product == "announcements");
    }

    [TestMethod]
    public async Task SubscribeAsync_PermanentRejection_FailsFastWithCode()
    {
        // Arrange
        var connector = new ScriptedConnector();
        await using var session = new DrishtiWebSocketSession(FastOptions(), connector);
        await StartAndWaitForConnectionAsync(session, connector);
        connector.Latest.Responder = _ => "{\"error\":\"Add-on required\",\"code\":\"forbidden\"}";

        // Act
        var act = () => session.SubscribeAsync("earnings");

        // Assert
        var error = (await act.Should().ThrowAsync<DrishtiWebSocketException>()).Which;
        error.Code.Should().Be("forbidden");
        error.Message.Should().Be("Add-on required");
        connector.Latest.Sent.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task SubscribeAsync_TransientRejection_IsRetried()
    {
        // Arrange
        var connector = new ScriptedConnector();
        await using var session = new DrishtiWebSocketSession(FastOptions(), connector);
        await StartAndWaitForConnectionAsync(session, connector);
        var calls = 0;
        connector.Latest.Responder = message => Interlocked.Increment(ref calls) == 1
            ? "{\"error\":\"busy\",\"code\":\"internal_error\"}"
            : ScriptedConnector.AckEverySubscribe(message);

        // Act
        await session.SubscribeAsync("concalls");

        // Assert
        connector.Latest.Sent.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task SubscribeAsync_NoAcknowledgement_TimesOutAfterMaxAttempts()
    {
        // Arrange
        var connector = new ScriptedConnector();
        await using var session = new DrishtiWebSocketSession(FastOptions(), connector);
        await StartAndWaitForConnectionAsync(session, connector);
        connector.Latest.Responder = _ => null;

        // Act
        var act = () => session.SubscribeAsync("news");

        // Assert
        await act.Should().ThrowAsync<DrishtiWebSocketException>().WithMessage("*timed out for news*");
        connector.Latest.Sent.Should().HaveCount(3);
    }

    [TestMethod]
    public async Task ServerClose_ReconnectsAndResubscribes()
    {
        // Arrange
        var connector = new ScriptedConnector();
        await using var session = new DrishtiWebSocketSession(FastOptions(), connector);
        var closeReasons = new ConcurrentQueue<string>();
        session.OnClose(reason => { closeReasons.Enqueue(reason); return Task.CompletedTask; });
        await session.SubscribeAsync("news", ["TCS"]);
        await StartAndWaitForConnectionAsync(session, connector);
        var first = connector.Latest;

        // Act
        first.ServerClose("WebSocket closed (code 1012): restart");
        await Eventually.True(() => connector.Connections.Count == 2 && connector.Latest.Sent.Count == 1, "the session reconnects and resubscribes");

        // Assert
        closeReasons.Should().Equal("WebSocket closed (code 1012): restart");
        first.Disposed.Should().BeTrue();
        JsonAssert.Parse(connector.Latest.Sent.Single()).GetProperty("symbols")[0].GetString().Should().Be("TCS");
    }

    [TestMethod]
    public async Task ConnectFailures_BackOffAndWarnAtMaxDelay()
    {
        // Arrange
        var connector = new ScriptedConnector().ThenFail("down").ThenFail("down").ThenFail("down").ThenFail("down");
        var options = FastOptions();
        options.ReconnectWarnAfterAttempts = 3;
        await using var session = new DrishtiWebSocketSession(options, connector);
        var delays = new ConcurrentQueue<TimeSpan>();
        var warnings = new ConcurrentQueue<int>();
        session.OnReconnectAttempt((_, delay, _) => { delays.Enqueue(delay); return Task.CompletedTask; });
        session.OnReconnectWarning((attempt, _) => { warnings.Enqueue(attempt); return Task.CompletedTask; });

        // Act
        await session.StartAsync();
        await Eventually.True(() => session.IsConnected, "the fifth attempt connects");

        // Assert: like the other SDKs, the first retry already waits initial x multiplier, and the
        // attempt after a warning runs immediately because the warning resets the attempt count.
        delays.Should().Equal(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(40));
        warnings.Should().Equal(3);
    }

    [TestMethod]
    public async Task HeartbeatWatchdog_ForcesReconnectAfterSilence()
    {
        // Arrange
        var connector = new ScriptedConnector();
        var options = FastOptions();
        options.HeartbeatTimeout = TimeSpan.FromMilliseconds(150);
        options.HeartbeatCheckInterval = TimeSpan.FromMilliseconds(20);
        await using var session = new DrishtiWebSocketSession(options, connector);
        var closeReasons = new ConcurrentQueue<string>();
        session.OnClose(reason => { closeReasons.Enqueue(reason); return Task.CompletedTask; });

        // Act
        await session.StartAsync();
        await Eventually.True(() => connector.Connections.Count >= 2, "the watchdog reconnects");

        // Assert
        closeReasons.First().Should().StartWith("heartbeat timeout after");
    }

    [TestMethod]
    public async Task Heartbeats_KeepTheConnectionAlive()
    {
        // Arrange
        var connector = new ScriptedConnector();
        var options = FastOptions();
        options.HeartbeatTimeout = TimeSpan.FromMilliseconds(200);
        options.HeartbeatCheckInterval = TimeSpan.FromMilliseconds(20);
        await using var session = new DrishtiWebSocketSession(options, connector);
        HeartbeatEvent? heartbeat = null;
        session.OnHeartbeat(e => { heartbeat = e; return Task.CompletedTask; });
        await StartAndWaitForConnectionAsync(session, connector);

        // Act
        for (var i = 0; i < 8; i++)
        {
            connector.Latest.ServerSend("{\"type\":\"heartbeat\",\"sent_at\":\"t" + i + "\"}");
            await Task.Delay(50);
        }

        // Assert
        connector.Connections.Should().HaveCount(1);
        session.LastHeartbeatReceivedAt.Should().NotBeNull();
        heartbeat!.SentAt.Should().Be("t7");
    }

    [TestMethod]
    public async Task ReadEventsAsync_StreamsEventsAndEndsOnStop()
    {
        // Arrange
        var connector = new ScriptedConnector();
        var session = new DrishtiWebSocketSession(FastOptions(), connector);
        await StartAndWaitForConnectionAsync(session, connector);
        var events = new List<WebSocketEvent>();
        var reader = Task.Run(async () =>
        {
            await foreach (var e in session.ReadEventsAsync())
            {
                events.Add(e);
            }
        });
        await Task.Delay(50);

        // Act
        connector.Latest.ServerSend("{\"type\":\"heartbeat\"}");
        connector.Latest.ServerSend("{\"channel\":\"earnings\",\"data\":{\"id\":\"e1\"}}");
        connector.Latest.ServerSend("{\"hello\":\"world\"}");
        await Eventually.True(() => events.Count == 2, "two events are read");
        await session.DisposeAsync();
        await reader.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        events[0].Should().BeOfType<DataEvent>().Which.Channel.Should().Be("earnings");
        events[1].Should().BeOfType<RawEvent>();
        session.IsConnected.Should().BeFalse();
    }

    [TestMethod]
    public async Task RunAsync_ReturnsWhenCancelled()
    {
        // Arrange
        var connector = new ScriptedConnector();
        await using var session = new DrishtiWebSocketSession(FastOptions(), connector);
        using var cancellation = new CancellationTokenSource();
        var run = session.RunAsync(cancellation.Token);
        await Eventually.True(() => session.IsConnected, "connected");

        // Act
        cancellation.Cancel();

        // Assert
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        session.IsConnected.Should().BeFalse();
    }

    [TestMethod]
    public async Task ClientCreateWebSocketSession_InheritsKeyBaseUrlAndHeaders()
    {
        // Arrange
        using var client = new DrishtiClient(new DrishtiClientOptions { ApiKey = "k1", BaseUrl = "http://localhost:8000/" });

        // Act
        await using var session = client.CreateWebSocketSession(o => o.HeartbeatTimeout = TimeSpan.Zero);

        // Assert
        session.Should().BeOfType<DrishtiWebSocketSession>();
        session.IsConnected.Should().BeFalse();
    }

    private static async Task StartAndWaitForConnectionAsync(IDrishtiWebSocketSession session, ScriptedConnector connector)
    {
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.OnOpen(() =>
        {
            opened.TrySetResult(true);
            return Task.CompletedTask;
        });
        await session.StartAsync();
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
        connector.Connections.Should().NotBeEmpty();
    }

    private static DrishtiWebSocketOptions FastOptions()
    {
        var options = new DrishtiWebSocketOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://api.test",
            ReconnectInitialDelay = TimeSpan.FromMilliseconds(10),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(40),
            ReconnectJitterRatio = 0,
            HeartbeatTimeout = TimeSpan.Zero,
            SubscribeAckTimeout = TimeSpan.FromMilliseconds(100),
            SubscribeMaxAttempts = 3,
            SubscribeRetryInitialDelay = TimeSpan.FromMilliseconds(5),
            SubscribeRetryMaxDelay = TimeSpan.FromMilliseconds(20),
        };
        options.Headers["X-Team"] = "ops";
        return options;
    }
}
