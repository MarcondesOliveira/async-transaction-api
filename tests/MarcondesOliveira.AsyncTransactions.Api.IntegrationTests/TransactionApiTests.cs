using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarcondesOliveira.AsyncTransactions.Api.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MarcondesOliveira.AsyncTransactions.Api.IntegrationTests;

public sealed class TransactionApiTests : IClassFixture<TransactionApiFactory>
{
    private readonly HttpClient _client;

    public TransactionApiTests(TransactionApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostThenGet_ReturnsPersistedPendingTransaction()
    {
        using var request = CreatePostRequest("demo-request-post-get");

        var createResponse = await _client.SendAsync(
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);

        using var createDocument = JsonDocument.Parse(
            await createResponse.Content.ReadAsStreamAsync(
                CancellationToken.None));
        var transactionId = createDocument.RootElement
            .GetProperty("transactionId")
            .GetGuid();
        Assert.Equal(
            "PENDING",
            createDocument.RootElement.GetProperty("status").GetString());

        var getResponse = await _client.GetAsync(
            $"/transactions/{transactionId}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var getDocument = JsonDocument.Parse(
            await getResponse.Content.ReadAsStreamAsync(
                CancellationToken.None));
        Assert.Equal(
            transactionId,
            getDocument.RootElement.GetProperty("transactionId").GetGuid());
        Assert.Equal(
            "125.90",
            getDocument.RootElement
                .GetProperty("amount")
                .GetProperty("value")
                .GetString());
    }

    [Fact]
    public async Task RepeatingSameIdempotencyKeyAndBody_ReturnsSameTransaction()
    {
        const string idempotencyKey = "demo-request-repeated";
        using var firstRequest = CreatePostRequest(idempotencyKey);
        using var secondRequest = CreatePostRequest(idempotencyKey);

        var firstResponse = await _client.SendAsync(
            firstRequest,
            CancellationToken.None);
        var secondResponse = await _client.SendAsync(
            secondRequest,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);

        using var firstJson = JsonDocument.Parse(
            await firstResponse.Content.ReadAsStreamAsync(
                CancellationToken.None));
        using var secondJson = JsonDocument.Parse(
            await secondResponse.Content.ReadAsStreamAsync(
                CancellationToken.None));
        Assert.Equal(
            firstJson.RootElement.GetProperty("transactionId").GetGuid(),
            secondJson.RootElement.GetProperty("transactionId").GetGuid());
    }

    [Fact]
    public async Task ReusingIdempotencyKeyWithDifferentBody_ReturnsConflict()
    {
        const string idempotencyKey = "demo-request-conflict";
        using var firstRequest = CreatePostRequest(idempotencyKey);
        using var secondRequest = CreatePostRequest(
            idempotencyKey,
            amount: "999.99");

        var firstResponse = await _client.SendAsync(
            firstRequest,
            CancellationToken.None);
        var secondResponse = await _client.SendAsync(
            secondRequest,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        using var document = JsonDocument.Parse(
            await secondResponse.Content.ReadAsStreamAsync(
                CancellationToken.None));
        Assert.Equal(
            "IDEMPOTENCY_KEY_REUSED",
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvalidRequest_ReturnsContractError()
    {
        using var request = CreatePostRequest(
            "invalid-request-key",
            amount: "0.00");

        var response = await _client.SendAsync(
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(
                CancellationToken.None));
        Assert.Equal(
            "VALIDATION_ERROR",
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnknownJsonField_ReturnsContractError()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(new
            {
                transactionType = "TRANSFER",
                amount = new { value = "125.90", currency = "BRL" },
                destination = new { accountId = "portfolio-account-001" },
                unexpected = true
            })
        };
        request.Headers.Add("Idempotency-Key", "unknown-field-request");

        var response = await _client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(CancellationToken.None));
        Assert.Equal(
            "VALIDATION_ERROR",
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvalidTransactionId_ReturnsBadRequest()
    {
        var response = await _client.GetAsync(
            "/transactions/not-a-guid",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(CancellationToken.None));
        Assert.Equal(
            "VALIDATION_ERROR",
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task OpenApiDocument_IsAvailable()
    {
        var response = await _client.GetAsync(
            "/openapi/v1.json",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(CancellationToken.None));
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .TryGetProperty("/transactions", out _));
    }

    [Fact]
    public async Task SwaggerUi_IsAvailable()
    {
        var response = await _client.GetAsync(
            "/swagger/index.html",
            CancellationToken.None);
        var html = await response.Content.ReadAsStringAsync(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Swagger UI", html, StringComparison.Ordinal);
    }

    private static HttpRequestMessage CreatePostRequest(
        string idempotencyKey,
        string amount = "125.90")
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(new
            {
                transactionType = "TRANSFER",
                amount = new { value = amount, currency = "BRL" },
                destination = new { accountId = "portfolio-account-001" },
                externalReference = "order-2026-000042"
            })
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        message.Headers.Add(
            "X-Correlation-Id",
            "f452ea72-e88d-48e9-875f-3f5e7f2736e6");
        return message;
    }
}

public sealed class TransactionApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITransactionRepository>();
            services.AddSingleton<ITransactionRepository, InMemoryTransactionRepository>();
        });
    }
}

internal sealed class InMemoryTransactionRepository : ITransactionRepository
{
    private readonly ConcurrentDictionary<string, StoredTransaction> _byIdempotency = new();
    private readonly ConcurrentDictionary<Guid, Transaction> _byId = new();

    public Task<CreatePersistenceResult> CreateAsync(
        Transaction transaction,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var stored = _byIdempotency.GetOrAdd(
            transaction.RequestIdempotencyKey,
            _ =>
            {
                _byId[transaction.TransactionId] = transaction;
                return new StoredTransaction(requestHash, transaction);
            });

        return Task.FromResult(string.Equals(
                stored.RequestHash,
                requestHash,
                StringComparison.Ordinal)
            ? new CreatePersistenceResult(
                stored.Transaction.TransactionId == transaction.TransactionId
                    ? CreatePersistenceResultKind.Created
                    : CreatePersistenceResultKind.Existing,
                stored.Transaction)
            : new CreatePersistenceResult(CreatePersistenceResultKind.Conflict));
    }

    public Task<Transaction?> GetAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        _byId.TryGetValue(transactionId, out var transaction);
        return Task.FromResult(transaction);
    }

    private sealed record StoredTransaction(
        string RequestHash,
        Transaction Transaction);
}
