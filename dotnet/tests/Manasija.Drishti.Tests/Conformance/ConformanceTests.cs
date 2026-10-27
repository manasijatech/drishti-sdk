using System.Text;
using System.Text.Json;
using FluentAssertions;
using Manasija.Drishti.Batch;
using Manasija.Drishti.Exceptions;
using Manasija.Drishti.Http;
using Manasija.Drishti.Models;
using Manasija.Drishti.Params;
using Manasija.Drishti.Tests.Fakes;
using Manasija.Drishti.WebSocket;

namespace Manasija.Drishti.Tests.Conformance;

/// <summary>
/// Runs the language-neutral cases in <c>/conformance</c> that every Drishti SDK must pass.
/// See <c>conformance/README.md</c> for the case format.
/// </summary>
[TestClass]
public sealed class ConformanceTests
{
    public static IEnumerable<object[]> RestCases => Cases("rest");

    public static IEnumerable<object[]> HelperCases => Cases("helpers");

    public static IEnumerable<object[]> ParseCases => Cases("websocket", "parse.json");

    public static IEnumerable<object[]> SubscribeCases => Cases("websocket", "subscribe.json");

    public static IEnumerable<object[]> UrlCases => Cases("websocket", "url.json");

    public static string CaseName(System.Reflection.MethodInfo method, object[] data) => (string)data[0];

    [TestMethod]
    [DynamicData(nameof(RestCases), DynamicDataDisplayName = nameof(CaseName))]
    public async Task Rest(string id, string caseJson)
    {
        // Arrange
        var testCase = JsonAssert.Parse(caseJson);
        var transport = new FakeTransport();
        foreach (var response in ScriptedResponses(testCase))
        {
            if (response.TryGetProperty("network_error", out var networkError) && networkError.GetBoolean())
            {
                transport.EnqueueNetworkError();
            }
            else
            {
                transport.Enqueue(ToTransportResponse(response));
            }
        }

        object? result = null;
        Exception? error = null;

        // Act
        try
        {
            using var client = CreateClient(testCase, transport);
            result = await Invoke(client, testCase.GetProperty("call"));
        }
        catch (Exception ex)
        {
            error = ex;
        }

        // Assert
        AssertRequests(id, testCase, transport.Requests);
        if (testCase.TryGetProperty("expect_error", out var expectedError))
        {
            AssertError(id, expectedError, error);
            return;
        }

        error.Should().BeNull($"{id} should not throw");
        if (testCase.TryGetProperty("expect_result", out var expectedResult))
        {
            AssertResult(id, expectedResult, result);
        }
    }

    [TestMethod]
    [DynamicData(nameof(HelperCases), DynamicDataDisplayName = nameof(CaseName))]
    public void Helpers(string id, string caseJson)
    {
        // Arrange
        var testCase = JsonAssert.Parse(caseJson);
        var function = testCase.GetProperty("function").GetString();
        var input = testCase.GetProperty("input");

        // Act
        Func<object> act = function switch
        {
            "build_batch_input_jsonl" => () => BatchJsonl.BuildInput(input.EnumerateArray().Select(e => (object)e).ToList()),
            "parse_batch_result_jsonl" => () => BatchJsonl.ParseResults(input.GetString()!),
            _ => throw new AssertFailedException($"Unknown helper {function}"),
        };

        // Assert
        if (testCase.TryGetProperty("expect_error", out _))
        {
            act.Should().Throw<ArgumentException>(id);
            return;
        }

        var result = act();
        if (testCase.TryGetProperty("expect_lines", out var expectedLines))
        {
            var text = (string)result;
            text.Should().EndWith("\n", id);
            var lines = text.TrimEnd('\n').Split('\n');
            lines.Should().HaveCount(expectedLines.GetArrayLength(), id);
            for (var i = 0; i < lines.Length; i++)
            {
                JsonAssert.Equal(expectedLines[i], JsonAssert.Parse(lines[i]), $"{id} line {i}");
            }
        }

        if (testCase.TryGetProperty("expect_result", out var expectedResult))
        {
            AssertResult(id, expectedResult, result);
        }
    }

    [TestMethod]
    [DynamicData(nameof(ParseCases), DynamicDataDisplayName = nameof(CaseName))]
    public void WebSocketParse(string id, string caseJson)
    {
        // Arrange
        var testCase = JsonAssert.Parse(caseJson);

        // Act
        var parsed = WebSocketMessages.Parse(testCase.GetProperty("raw").GetString()!);

        // Assert
        JsonAssert.Equal(testCase.GetProperty("expect"), JsonAssert.Parse(parsed.ToJson().ToJsonString()), id);
    }

    [TestMethod]
    [DynamicData(nameof(SubscribeCases), DynamicDataDisplayName = nameof(CaseName))]
    public void WebSocketSubscribe(string id, string caseJson)
    {
        // Arrange
        var options = JsonAssert.Parse(caseJson).GetProperty("options");
        var subscribe = new SubscribeOptions(options.GetProperty("product").GetString()!)
        {
            Symbols = StrList(options, "symbols"),
            Categories = StrList(options, "categories"),
            Detailed = Bool(options, "detailed") ?? true,
        };

        // Act
        var message = WebSocketMessages.BuildSubscribeMessage(subscribe);

        // Assert
        JsonAssert.Equal(JsonAssert.Parse(caseJson).GetProperty("expect"), JsonAssert.Parse(message.ToJsonString()), id);
    }

    [TestMethod]
    [DynamicData(nameof(UrlCases), DynamicDataDisplayName = nameof(CaseName))]
    public void WebSocketUrl(string id, string caseJson)
    {
        // Arrange
        var testCase = JsonAssert.Parse(caseJson);

        // Act
        var url = WebSocketMessages.BuildUrl(testCase.GetProperty("base_url").GetString()!, Str(testCase, "api_key"));

        // Assert
        url.Should().Be(testCase.GetProperty("expect").GetString(), id);
    }

    [TestMethod]
    public void Constants_MatchSharedDefinitions()
    {
        // Arrange
        var constants = JsonAssert.Parse(File.ReadAllText(Path.Combine(ConformanceDirectory, "constants.json")));

        // Act and assert
        DrishtiConstants.DefaultBaseUrl.Should().Be(constants.GetProperty("default_base_url").GetString());
        DrishtiConstants.WebSocketProducts.Should().Equal(StrList(constants, "ws_products"));
        DrishtiConstants.PermanentSubscribeErrorCodes.Should().BeEquivalentTo(StrList(constants, "permanent_subscribe_error_codes"));
        DrishtiConstants.AlertTypes.Should().Equal(StrList(constants, "alert_types"));
        DrishtiConstants.BatchJobStatuses.Should().Equal(StrList(constants, "batch_job_statuses"));
        DrishtiConstants.BatchJobTerminalStatuses.Should().Equal(StrList(constants, "batch_job_terminal_statuses"));
    }

    [TestMethod]
    public void Fixtures_AreFound()
    {
        // Guards against a silently empty data source, which would make every data-driven test vacuous.
        RestCases.Should().HaveCountGreaterThan(50);
        HelperCases.Should().NotBeEmpty();
        ParseCases.Should().NotBeEmpty();
    }

    // --- Case loading ---------------------------------------------------------------------------

    private static string ConformanceDirectory
        => Environment.GetEnvironmentVariable("DRISHTI_CONFORMANCE_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(AppContext.BaseDirectory, "conformance");

    private static IEnumerable<object[]> Cases(string folder, string? file = null)
    {
        var directory = Path.Combine(ConformanceDirectory, folder);
        IEnumerable<string> files = file is null ? Directory.GetFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal) : [Path.Combine(directory, file)];
        foreach (var path in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var testCase in document.RootElement.GetProperty("cases").EnumerateArray())
            {
                yield return [testCase.GetProperty("id").GetString()!, testCase.GetRawText()];
            }
        }
    }

    // --- Client and invocation ------------------------------------------------------------------

    private static DrishtiClient CreateClient(JsonElement testCase, FakeTransport transport)
        => FakeTransport.CreateClient(transport, options =>
        {
            if (!testCase.TryGetProperty("client", out var client))
            {
                return;
            }

            options.ApiKey = Str(client, "api_key") ?? options.ApiKey;
            options.BaseUrl = Str(client, "base_url") ?? options.BaseUrl;
            options.MaxRetries = Int(client, "max_retries") ?? options.MaxRetries;
            if (client.TryGetProperty("headers", out var headers))
            {
                foreach (var header in headers.EnumerateObject())
                {
                    options.Headers[header.Name] = header.Value.GetString()!;
                }
            }
        });

    private static async Task<object?> Invoke(DrishtiClient client, JsonElement call)
    {
        var method = call.GetProperty("method").GetString();
        var a = call.TryGetProperty("args", out var args) ? args : JsonAssert.Parse("{}");
        var positional = call.TryGetProperty("positional", out var p) ? p.EnumerateArray().ToList() : [];

        return method switch
        {
            "get_news" => await client.GetNewsAsync(new NewsParams
            {
                Symbols = StrList(a, "symbols"), ScripCodes = StrList(a, "scrip_codes"), Sentiment = Str(a, "sentiment"),
                From = Str(a, "from"), To = Str(a, "to"), Page = Int(a, "page"), Limit = Int(a, "limit"),
            }),
            "get_block_deals" => await client.GetBlockDealsAsync(new BlockDealsParams
            {
                Symbols = StrList(a, "symbols"), Exchange = Str(a, "exchange"), From = Str(a, "from"), To = Str(a, "to"),
                Page = Int(a, "page"), Limit = Int(a, "limit"),
            }),
            "get_symbols_metadata" => await client.GetSymbolsMetadataAsync(new SymbolsMetadataParams
            {
                Symbols = StrList(a, "symbols"), ScripCodes = StrList(a, "scrip_codes"),
            }),
            "get_symbols" => Str(a, "format") == "json" ? await client.GetSymbolsJsonAsync() : await client.GetSymbolsAsync(),
            "get_announcements_categories" => await client.GetAnnouncementsCategoriesAsync(),
            "get_announcements" => await client.GetAnnouncementsAsync(new AnnouncementsParams
            {
                Symbols = StrList(a, "symbols"), ScripCodes = StrList(a, "scrip_codes"), Categories = StrList(a, "categories"),
                From = Str(a, "from"), To = Str(a, "to"), Detailed = Bool(a, "detailed"),
                Page = Int(a, "page"), Limit = Int(a, "limit"),
            }),
            "get_announcements_attachments" => await client.GetAnnouncementsAttachmentsAsync(StrList(a, "ids")!),
            "get_announcement_citation_pdf" => await client.GetAnnouncementCitationPdfAsync(positional[0].GetString()!),
            "post_daily_summary" => await client.PostDailySummaryAsync(a.GetProperty("body"), Retention(a)),
            "get_earnings_index" => await client.GetEarningsIndexAsync(IndexArgs(a)),
            "get_earnings" => await client.GetEarningsAsync(new EarningsParams
            {
                Ids = StrList(a, "ids"), Symbols = StrList(a, "symbols"), ScripCodes = StrList(a, "scrip_codes"),
                From = Str(a, "from"), To = Str(a, "to"), Quarter = Str(a, "quarter"), Detailed = Bool(a, "detailed"),
                Page = Int(a, "page"), Limit = Int(a, "limit"),
            }),
            "get_earnings_detail" => await client.GetEarningsDetailAsync(DetailArgs(a)),
            "get_earnings_attachments" => await client.GetEarningsAttachmentsAsync(StrList(a, "ids")!),
            "get_earnings_citation_pdf" => await client.GetEarningsCitationPdfAsync(positional[0].GetString()!, Int(a, "page")),
            "get_earnings_citation_page" => await client.GetEarningsCitationPageAsync(positional[0].GetString()!, positional[1].GetInt32()),
            "get_upcoming_earnings" => await client.GetUpcomingEarningsAsync(new UpcomingEarningsParams
            {
                Symbols = StrList(a, "symbols"), ScripCodes = StrList(a, "scrip_codes"), Page = Int(a, "page"), Limit = Int(a, "limit"),
            }),
            "get_concalls" => await client.GetConcallsAsync(new ConcallsParams
            {
                Symbols = StrList(a, "symbols"), ScripCodes = StrList(a, "scrip_codes"), From = Str(a, "from"), To = Str(a, "to"),
                Detailed = Bool(a, "detailed"), Page = Int(a, "page"), Limit = Int(a, "limit"),
            }),
            "get_concall_citation_pdf" => await client.GetConcallCitationPdfAsync(positional[0].GetString()!),
            "get_concalls_index" => await client.GetConcallsIndexAsync(IndexArgs(a)),
            "get_upcoming_concalls" => await client.GetUpcomingConcallsAsync(new UpcomingConcallsParams
            {
                Symbols = StrList(a, "symbols"), ScripCodes = StrList(a, "scrip_codes"), Detailed = Bool(a, "detailed"),
                Page = Int(a, "page"), Limit = Int(a, "limit"),
            }),
            "get_concalls_detail" => await client.GetConcallsDetailAsync(DetailArgs(a)),
            "get_concalls_transcript" => await client.GetConcallsTranscriptAsync(new TranscriptParams(Str(a, "quarter")!)
            {
                Symbol = Str(a, "symbol"), ScripCode = Str(a, "scrip_code"),
            }),
            "post_concalls_transcripts" => await client.PostConcallsTranscriptsAsync(
                a.GetProperty("items").EnumerateArray().Select(i => new TranscriptLookup(Str(i, "symbol")!, Str(i, "quarter")!)).ToList()),
            "get_alerts" => await client.GetAlertsAsync(new AlertsParams
            {
                Symbols = StrList(a, "symbols"), ScripCodes = StrList(a, "scrip_codes"), Types = StrList(a, "type"),
                From = Str(a, "from"), To = Str(a, "to"), Important = Bool(a, "important"), Page = Int(a, "page"), Limit = Int(a, "limit"),
            }),
            "get_account" => await client.GetAccountAsync(),
            "get_account_limits" => await client.GetAccountLimitsAsync(),
            "get_account_usage" => await client.GetAccountUsageAsync(),
            "get_account_ledger" => await client.GetAccountLedgerAsync(Int(a, "limit")),
            "post_batch_jobs" or "post_batch_jobs_file" => await client.PostBatchJobsAsync(BatchArgs(a)),
            "get_batch_jobs" => await client.GetBatchJobsAsync(Int(a, "limit")),
            "get_batch_jobs_job_id" => await client.GetBatchJobAsync(Str(a, "job_id")!),
            "delete_batch_jobs_job_id" => await client.CancelBatchJobAsync(Str(a, "job_id")!),
            "get_batch_jobs_job_id_results" => await client.GetBatchJobResultsAsync(Str(a, "job_id")!),
            "get_batch_jobs_job_id_results_parsed" => await client.GetBatchJobResultsParsedAsync(Str(a, "job_id")!),
            "wait_for_batch_job_completion" => await client.WaitForBatchJobCompletionAsync(Str(a, "job_id")!, PollInterval(a)),
            "submit_batch_job_and_wait" => await client.SubmitBatchJobAndWaitAsync(BatchArgs(a), PollInterval(a)),
            _ => throw new AssertFailedException($"The runner does not map method {method}"),
        };
    }

    private static IndexParams IndexArgs(JsonElement a) => new()
    {
        Symbols = StrList(a, "symbols"), ScripCodes = StrList(a, "scrip_codes"), From = Str(a, "from"), To = Str(a, "to"),
        Quarter = Str(a, "quarter"), Page = Int(a, "page"), Limit = Int(a, "limit"),
    };

    private static DetailParams DetailArgs(JsonElement a) => new(Str(a, "quarter")!)
    {
        Symbol = Str(a, "symbol"), ScripCode = Str(a, "scrip_code"), Detailed = Bool(a, "detailed"),
    };

    private static BatchJobRequest BatchArgs(JsonElement a) => new(Str(a, "file_name")!, Str(a, "file_content")!)
    {
        DisplayName = Str(a, "display_name"),
        Metadata = Str(a, "metadata"),
        ContentRetention = Retention(a),
    };

    private static ContentRetention? Retention(JsonElement a) => Str(a, "content_retention") == "none" ? ContentRetention.None : null;

    private static TimeSpan? PollInterval(JsonElement a) => Int(a, "poll_interval_ms") is { } ms ? TimeSpan.FromMilliseconds(ms) : null;

    // --- Assertions -----------------------------------------------------------------------------

    private static IEnumerable<JsonElement> ScriptedResponses(JsonElement testCase)
    {
        if (testCase.TryGetProperty("responses", out var responses))
        {
            return responses.EnumerateArray().ToList();
        }

        return testCase.TryGetProperty("response", out var response) ? [response] : [];
    }

    private static TransportResponse ToTransportResponse(JsonElement response)
    {
        var status = response.TryGetProperty("status", out var s) ? s.GetInt32() : 200;
        var contentType = Str(response, "content_type") ?? "application/json";
        byte[] body = response.TryGetProperty("json", out var json)
            ? Encoding.UTF8.GetBytes(json.GetRawText())
            : Encoding.UTF8.GetBytes(Str(response, "text") ?? string.Empty);
        return new TransportResponse(status, new Dictionary<string, string> { ["Content-Type"] = contentType }, body);
    }

    private static void AssertRequests(string id, JsonElement testCase, IReadOnlyList<TransportRequest> requests)
    {
        if (testCase.TryGetProperty("expect_request_count", out var count))
        {
            requests.Should().HaveCount(count.GetInt32(), id);
        }

        List<JsonElement> expected;
        if (testCase.TryGetProperty("expect_requests", out var many))
        {
            expected = many.EnumerateArray().ToList();
        }
        else if (testCase.TryGetProperty("expect_request", out var one))
        {
            expected = [one];
        }
        else
        {
            return;
        }

        requests.Should().HaveCount(expected.Count, $"{id} request count");
        for (var i = 0; i < expected.Count; i++)
        {
            AssertRequest($"{id} request {i}", expected[i], requests[i]);
        }
    }

    private static void AssertRequest(string context, JsonElement expected, TransportRequest actual)
    {
        actual.Method.Should().Be(expected.GetProperty("method").GetString(), context);

        var original = actual.Uri.OriginalString;
        var withoutOrigin = original.Substring(original.IndexOf('/', original.IndexOf("//", StringComparison.Ordinal) + 2));
        var queryStart = withoutOrigin.IndexOf('?');
        var path = queryStart < 0 ? withoutOrigin : withoutOrigin.Substring(0, queryStart);
        path.Should().Be(expected.GetProperty("path").GetString(), $"{context} path");

        if (expected.TryGetProperty("query", out var expectedQuery))
        {
            var actualQuery = ParseQuery(queryStart < 0 ? string.Empty : withoutOrigin.Substring(queryStart + 1));
            var expectedMap = expectedQuery.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(v => v.GetString()!).ToList());
            actualQuery.Should().BeEquivalentTo(expectedMap, $"{context} query");
        }

        if (expected.TryGetProperty("headers", out var headers))
        {
            foreach (var header in headers.EnumerateObject())
            {
                actual.Header(header.Name).Should().Be(header.Value.GetString(), $"{context} header {header.Name}");
            }
        }

        if (expected.TryGetProperty("json_body", out var jsonBody))
        {
            actual.Body.Should().NotBeNull(context);
            JsonAssert.Equal(jsonBody, JsonAssert.Parse(Encoding.UTF8.GetString(actual.Body!)), $"{context} json_body");
        }

        if (expected.TryGetProperty("multipart", out var multipart))
        {
            AssertMultipart(context, multipart, actual);
        }
    }

    private static Dictionary<string, List<string>> ParseQuery(string query)
    {
        var result = new Dictionary<string, List<string>>();
        foreach (var pair in query.Split(['&'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = Uri.UnescapeDataString(separator < 0 ? pair : pair.Substring(0, separator));
            var value = separator < 0 ? string.Empty : Uri.UnescapeDataString(pair.Substring(separator + 1));
            if (!result.TryGetValue(name, out var values))
            {
                result[name] = values = [];
            }

            values.Add(value);
        }

        return result;
    }

    private static void AssertMultipart(string context, JsonElement expected, TransportRequest actual)
    {
        var contentType = actual.Header("Content-Type");
        contentType.Should().StartWith("multipart/form-data; boundary=", context);
        var boundary = contentType!.Substring("multipart/form-data; boundary=".Length);
        var fields = new Dictionary<string, string>();
        var files = new Dictionary<string, (string FileName, string Content)>();
        var body = Encoding.UTF8.GetString(actual.Body!);
        foreach (var part in body.Split(["--" + boundary], StringSplitOptions.None).Skip(1))
        {
            if (part.StartsWith("--", StringComparison.Ordinal))
            {
                break;
            }

            var headerEnd = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var partHeaders = part.Substring(0, headerEnd);
            var content = part.Substring(headerEnd + 4);
            content = content.Substring(0, content.Length - 2);
            var name = Between(partHeaders, "name=\"", "\"");
            if (partHeaders.Contains("filename=\"", StringComparison.Ordinal))
            {
                files[name] = (Between(partHeaders, "filename=\"", "\""), content);
            }
            else
            {
                fields[name] = content;
            }
        }

        var expectedFields = expected.GetProperty("fields").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        fields.Should().BeEquivalentTo(expectedFields, $"{context} multipart fields");
        foreach (var file in expected.GetProperty("files").EnumerateObject())
        {
            files.Should().ContainKey(file.Name, context);
            files[file.Name].FileName.Should().Be(file.Value.GetProperty("filename").GetString(), context);
            files[file.Name].Content.Should().Be(file.Value.GetProperty("content").GetString(), context);
        }
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal) + start.Length;
        return text.Substring(from, text.IndexOf(end, from, StringComparison.Ordinal) - from);
    }

    private static void AssertError(string id, JsonElement expected, Exception? error)
    {
        error.Should().NotBeNull($"{id} should throw");
        switch (expected.GetProperty("type").GetString())
        {
            case "invalid_argument":
                error.Should().BeAssignableTo<ArgumentException>(id);
                break;
            case "api_error":
                var apiError = error.Should().BeOfType<DrishtiApiException>(id).Subject;
                apiError.StatusCode.Should().Be(expected.GetProperty("status").GetInt32(), id);
                var body = expected.GetProperty("body");
                if (body.ValueKind == JsonValueKind.String)
                {
                    apiError.BodyText.Should().Be(body.GetString(), id);
                }
                else
                {
                    apiError.BodyJson.Should().NotBeNull(id);
                    JsonAssert.Equal(body, apiError.BodyJson!.Value, $"{id} error body");
                }

                break;
            default:
                throw new AssertFailedException($"Unknown error type in {id}");
        }
    }

    private static void AssertResult(string id, JsonElement expected, object? result)
    {
        if (expected.ValueKind == JsonValueKind.Null)
        {
            result.Should().BeNull(id);
            return;
        }

        var actual = result switch
        {
            DrishtiObject model => model.Json,
            string text => JsonAssert.Parse(JsonSerializer.Serialize(text)),
            byte[] bytes => JsonAssert.Parse(JsonSerializer.Serialize(Encoding.UTF8.GetString(bytes))),
            IEnumerable<DrishtiObject> models => JsonAssert.Parse("[" + string.Join(",", models.Select(m => m.Json.GetRawText())) + "]"),
            _ => throw new AssertFailedException($"{id} returned unexpected type {result?.GetType().Name ?? "null"}"),
        };
        JsonAssert.Equal(expected, actual, $"{id} result");
    }

    // --- Argument helpers -----------------------------------------------------------------------

    private static string? Str(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        } : null;

    private static int? Int(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static bool? Bool(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static List<string>? StrList(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(v => v.GetString()!).ToList()
            : null;
}
