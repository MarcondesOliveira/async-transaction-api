using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarcondesOliveira.AsyncTransactions.Api.Application;

namespace MarcondesOliveira.AsyncTransactions.Api.ContractTests;

public sealed class TransactionContractTests
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    [Fact]
    public void CreateRequest_DocumentationExample_Deserializes()
    {
        const string json = """
            {
              "transactionType": "TRANSFER",
              "amount": {
                "value": "125.90",
                "currency": "BRL"
              },
              "destination": {
                "accountId": "portfolio-account-001"
              },
              "externalReference": "order-2026-000042"
            }
            """;

        var request = JsonSerializer.Deserialize<CreateTransactionRequest>(
            json,
            Options);

        Assert.NotNull(request);
        Assert.Equal("TRANSFER", request.TransactionType);
        Assert.Equal("125.90", request.Amount?.Value);
        Assert.Equal("BRL", request.Amount?.Currency);
        Assert.Equal("portfolio-account-001", request.Destination?.AccountId);
        Assert.Equal("order-2026-000042", request.ExternalReference);
    }

    [Fact]
    public void CreateResponse_UsesCamelCaseAndUpperCaseStatus()
    {
        var response = new CreateTransactionResponse(
            Guid.Parse("06f26a61-3ae3-4f80-a4e9-061a3a73ad6d"),
            Guid.Parse("f452ea72-e88d-48e9-875f-3f5e7f2736e6"),
            TransactionStatus.Pending,
            DateTimeOffset.Parse(
                "2026-10-28T18:45:30.123Z",
                CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(
                "2026-10-28T18:45:30.123Z",
                CultureInfo.InvariantCulture),
            new TransactionLinks(
                "/transactions/06f26a61-3ae3-4f80-a4e9-061a3a73ad6d"));

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(response, Options));
        var root = document.RootElement;

        Assert.Equal("PENDING", root.GetProperty("status").GetString());
        Assert.Equal(
            "06f26a61-3ae3-4f80-a4e9-061a3a73ad6d",
            root.GetProperty("transactionId").GetString());
        Assert.False(root.TryGetProperty("TransactionId", out _));
    }

    [Fact]
    public void CreateRequest_WithUnknownField_IsRejected()
    {
        const string json = """
            {
              "transactionType": "TRANSFER",
              "amount": { "value": "125.90", "currency": "BRL" },
              "destination": { "accountId": "portfolio-account-001" },
              "unexpected": true
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CreateTransactionRequest>(json, Options));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(
            new JsonStringEnumConverter(new UpperCaseNamingPolicy()));
        return options;
    }

    private sealed class UpperCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name) => name.ToUpperInvariant();
    }
}
