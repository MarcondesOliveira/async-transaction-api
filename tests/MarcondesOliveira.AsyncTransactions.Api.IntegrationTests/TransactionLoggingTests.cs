using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using MarcondesOliveira.AsyncTransactions.Api.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MarcondesOliveira.AsyncTransactions.Api.IntegrationTests;

public sealed class TransactionLoggingTests
{
    private static readonly int[] ExpectedCreateStatuses = [202, 409, 400];

    [Fact]
    public async Task PostReplayAndGetKeepStoredAndRequestCorrelationsSeparate()
    {
        using var logs = new RecordingProvider();
        using var factory = Factory(logs);
        using var client = factory.CreateClient();
        var originalCorrelation = Guid.NewGuid();
        var requestCorrelation = Guid.NewGuid();
        using var first = await client.SendAsync(Post(originalCorrelation), CancellationToken.None);
        var original = await first.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(CancellationToken.None);
        var transactionId = original.GetProperty("transactionId").GetGuid();
        using var replay = await client.SendAsync(Post(requestCorrelation), CancellationToken.None);
        using var get = new HttpRequestMessage(HttpMethod.Get, $"/transactions/{transactionId}");
        get.Headers.Add("X-Correlation-Id", requestCorrelation.ToString());
        using var found = await client.SendAsync(get, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var events = logs.Events.ToArray();
        Assert.Equal(3, events.Length);
        Assert.All(events, entry =>
        {
            Assert.Equal("TransactionApiHandled", entry["EventName"]);
            Assert.Equal(transactionId, entry["TransactionId"]);
            Assert.Equal(originalCorrelation, entry["CorrelationId"]);
        });
        Assert.Equal(originalCorrelation, events[0]["RequestCorrelationId"]);
        Assert.Equal(requestCorrelation, events[1]["RequestCorrelationId"]);
        Assert.Equal(requestCorrelation, events[2]["RequestCorrelationId"]);
        Assert.Equal(202, events[1]["HttpStatus"]);
        Assert.Equal(200, events[2]["HttpStatus"]);
        AssertNoPayload(logs);
    }

    [Fact]
    public async Task ValidationAndConflictAreLoggedWithoutRequestData()
    {
        using var logs = new RecordingProvider();
        using var factory = Factory(logs);
        using var client = factory.CreateClient();
        var correlation = Guid.NewGuid();
        using var created = await client.SendAsync(Post(correlation), CancellationToken.None);
        using var conflict = await client.SendAsync(Post(correlation, "999.99"), CancellationToken.None);
        using var invalid = await client.SendAsync(Post(correlation, "0.00"), CancellationToken.None);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(ExpectedCreateStatuses, logs.Events.Select(entry => (int)entry["HttpStatus"]!));
        AssertNoPayload(logs);
    }

    [Fact]
    public async Task InvalidAndMissingTransactionIdsHaveSafeLogs()
    {
        using var logs = new RecordingProvider();
        using var factory = Factory(logs);
        using var client = factory.CreateClient();
        using var invalid = await client.GetAsync("/transactions/private-invalid-id", CancellationToken.None);
        var missingId = Guid.NewGuid();
        using var missing = await client.GetAsync($"/transactions/{missingId}", CancellationToken.None);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var events = logs.Events.ToArray();
        Assert.Null(events[0]["TransactionId"]);
        Assert.Equal(missingId, events[1]["TransactionId"]);
        Assert.DoesNotContain("private-invalid-id", logs.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExceptionLogsKeepTypesAndCorrelationWithoutMessages(bool infrastructureFailure)
    {
        using var logs = new RecordingProvider();
        using var factory = Factory(logs, infrastructureFailure);
        using var client = factory.CreateClient();
        var correlation = Guid.NewGuid();
        using var request = Post(correlation);
        if (!infrastructureFailure)
        {
            request.Content = new StringContent("private-malformed-body", System.Text.Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, CancellationToken.None);
        Assert.Equal(infrastructureFailure ? HttpStatusCode.InternalServerError : HttpStatusCode.BadRequest,
            response.StatusCode);
        var entry = Assert.Single(logs.Events);
        Assert.Equal("TransactionApiFailed", entry["EventName"]);
        Assert.Equal(correlation, entry["RequestCorrelationId"]);
        Assert.NotNull(entry["ErrorType"]);
        if (infrastructureFailure)
        {
            Assert.Equal(nameof(TimeoutException), entry["CauseType"]);
        }
        Assert.DoesNotContain("private-", logs.Text);
        AssertNoPayload(logs);
    }

    private static WebApplicationFactory<Program> Factory(RecordingProvider logs, bool fail = false) =>
        new TransactionApiFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(logs);
            if (fail)
            {
                services.RemoveAll<ITransactionRepository>();
                services.AddSingleton<ITransactionRepository, FailingRepository>();
            }
        }));

    private static HttpRequestMessage Post(Guid correlation, string amount = "125.90")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(new
            {
                transactionType = "TRANSFER",
                amount = new { value = amount, currency = "BRL" },
                destination = new { accountId = "private-account" },
                externalReference = "private-reference"
            })
        };
        request.Headers.Add("X-Correlation-Id", correlation.ToString());
        request.Headers.Add("Idempotency-Key", "private-idempotency-key");
        return request;
    }

    private static void AssertNoPayload(RecordingProvider logs)
    {
        Assert.DoesNotContain("private-account", logs.Text);
        Assert.DoesNotContain("private-reference", logs.Text);
        Assert.DoesNotContain("private-idempotency-key", logs.Text);
        Assert.DoesNotContain("125.90", logs.Text);
        Assert.DoesNotContain("999.99", logs.Text);
    }

    private sealed class FailingRepository : ITransactionRepository
    {
        public Task<CreatePersistenceResult> CreateAsync(Transaction transaction, string requestHash,
            CancellationToken cancellationToken) => throw new InvalidOperationException(
                "private-provider-response", new TimeoutException("private-timeout"));

        public Task<Transaction?> GetAsync(Guid transactionId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("private-provider-response");
    }

    private sealed class RecordingProvider : ILoggerProvider
    {
        public ConcurrentQueue<IReadOnlyDictionary<string, object?>> Events { get; } = new();
        public string Text => string.Join('\n', Events.SelectMany(entry => entry.Select(pair => $"{pair.Key}={pair.Value}")));
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, Events);
        public void Dispose() { }
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<IReadOnlyDictionary<string, object?>> events) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (category == "TransactionApi" && state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                events.Enqueue(values.ToDictionary(pair => pair.Key, pair => pair.Value));
            }
        }
    }
}
