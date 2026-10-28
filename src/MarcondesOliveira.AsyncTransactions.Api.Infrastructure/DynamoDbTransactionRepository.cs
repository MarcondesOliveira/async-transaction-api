using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using MarcondesOliveira.AsyncTransactions.Api.Application;

namespace MarcondesOliveira.AsyncTransactions.Api.Infrastructure;

public sealed class DynamoDbTransactionRepository(
    IAmazonDynamoDB dynamoDb,
    DynamoDbOptions options) : ITransactionRepository
{
    public async Task<CreatePersistenceResult> CreateAsync(
        Transaction transaction,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var existing = await ResolveIdempotencyAsync(
            transaction.RequestIdempotencyKey,
            requestHash,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var transactionItem = ToItem(transaction);
        var idempotencyItem = ToIdempotencyItem(transaction, requestHash);
        const string notExists = "attribute_not_exists(PK)";

        var request = new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Put = new Put
                    {
                        TableName = options.TableName,
                        Item = transactionItem,
                        ConditionExpression = notExists
                    }
                },
                new TransactWriteItem
                {
                    Put = new Put
                    {
                        TableName = options.TableName,
                        Item = idempotencyItem,
                        ConditionExpression = notExists
                    }
                }
            ]
        };

        try
        {
            await dynamoDb.TransactWriteItemsAsync(request, cancellationToken);
            return new CreatePersistenceResult(
                CreatePersistenceResultKind.Created,
                transaction);
        }
        catch (TransactionCanceledException)
        {
            var racedResult = await ResolveIdempotencyAsync(
                transaction.RequestIdempotencyKey,
                requestHash,
                cancellationToken);

            if (racedResult is not null)
            {
                return racedResult;
            }

            throw;
        }
    }

    public async Task<Transaction?> GetAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = options.TableName,
                ConsistentRead = true,
                Key = Key($"TRANSACTION#{transactionId}")
            },
            cancellationToken);

        return response.IsItemSet ? FromItem(response.Item) : null;
    }

    private async Task<CreatePersistenceResult?> ResolveIdempotencyAsync(
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = options.TableName,
                ConsistentRead = true,
                Key = Key($"IDEMPOTENCY#{idempotencyKey}")
            },
            cancellationToken);

        if (!response.IsItemSet)
        {
            return null;
        }

        if (!string.Equals(
                response.Item["requestHash"].S,
                requestHash,
                StringComparison.Ordinal))
        {
            return new CreatePersistenceResult(CreatePersistenceResultKind.Conflict);
        }

        var transactionId = Guid.Parse(response.Item["transactionId"].S);
        var transaction = await GetAsync(transactionId, cancellationToken);

        return transaction is null
            ? throw new InvalidOperationException(
                $"Idempotency record points to missing transaction {transactionId}")
            : new CreatePersistenceResult(
                CreatePersistenceResultKind.Existing,
                transaction);
    }

    private static Dictionary<string, AttributeValue> ToItem(Transaction transaction)
    {
        var key = $"TRANSACTION#{transaction.TransactionId}";
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = Text(key),
            ["SK"] = Text(key),
            ["entityType"] = Text("TRANSACTION"),
            ["schemaVersion"] = Number(1),
            ["transactionId"] = Text(transaction.TransactionId.ToString()),
            ["correlationId"] = Text(transaction.CorrelationId.ToString()),
            ["transactionType"] = Text(transaction.TransactionType),
            ["amountValue"] = Text(transaction.AmountValue),
            ["amountCurrency"] = Text(transaction.AmountCurrency),
            ["destinationAccountId"] = Text(transaction.DestinationAccountId),
            ["status"] = Text(ToStoredStatus(transaction.Status)),
            ["requestIdempotencyKey"] = Text(transaction.RequestIdempotencyKey),
            ["createdAt"] = Text(FormatDate(transaction.CreatedAt)),
            ["updatedAt"] = Text(FormatDate(transaction.UpdatedAt))
        };

        AddOptional(item, "externalReference", transaction.ExternalReference);
        AddOptional(item, "processorTransactionId", transaction.ProcessorTransactionId?.ToString());
        AddOptional(item, "resultCode", transaction.ResultCode);
        AddOptional(item, "resultMessage", transaction.ResultMessage);
        AddOptional(item, "completedAt", transaction.CompletedAt is null
            ? null
            : FormatDate(transaction.CompletedAt.Value));

        return item;
    }

    private static Dictionary<string, AttributeValue> ToIdempotencyItem(
        Transaction transaction,
        string requestHash)
    {
        var key = $"IDEMPOTENCY#{transaction.RequestIdempotencyKey}";
        return new Dictionary<string, AttributeValue>
        {
            ["PK"] = Text(key),
            ["SK"] = Text(key),
            ["entityType"] = Text("IDEMPOTENCY"),
            ["idempotencyKey"] = Text(transaction.RequestIdempotencyKey),
            ["requestHash"] = Text(requestHash),
            ["transactionId"] = Text(transaction.TransactionId.ToString()),
            ["createdAt"] = Text(FormatDate(transaction.CreatedAt)),
            ["expiresAt"] = Number(transaction.CreatedAt.AddHours(24).ToUnixTimeSeconds())
        };
    }

    private static Transaction FromItem(Dictionary<string, AttributeValue> item) =>
        new(
            Guid.Parse(item["transactionId"].S),
            Guid.Parse(item["correlationId"].S),
            item["transactionType"].S,
            item["amountValue"].S,
            item["amountCurrency"].S,
            item["destinationAccountId"].S,
            GetOptional(item, "externalReference"),
            Enum.Parse<TransactionStatus>(item["status"].S, ignoreCase: true),
            item["requestIdempotencyKey"].S,
            ParseDate(item["createdAt"].S),
            ParseDate(item["updatedAt"].S),
            ParseOptionalGuid(item, "processorTransactionId"),
            GetOptional(item, "resultCode"),
            GetOptional(item, "resultMessage"),
            ParseOptionalDate(item, "completedAt"));

    private static Dictionary<string, AttributeValue> Key(string value) =>
        new()
        {
            ["PK"] = Text(value),
            ["SK"] = Text(value)
        };

    private static AttributeValue Text(string value) => new() { S = value };

    private static AttributeValue Number(long value) =>
        new() { N = value.ToString(CultureInfo.InvariantCulture) };

    private static string ToStoredStatus(TransactionStatus status) =>
        status.ToString().ToUpperInvariant();

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static void AddOptional(
        Dictionary<string, AttributeValue> item,
        string name,
        string? value)
    {
        if (value is not null)
        {
            item[name] = Text(value);
        }
    }

    private static string? GetOptional(
        Dictionary<string, AttributeValue> item,
        string name) =>
        item.TryGetValue(name, out var value) ? value.S : null;

    private static Guid? ParseOptionalGuid(
        Dictionary<string, AttributeValue> item,
        string name) =>
        Guid.TryParse(GetOptional(item, name), out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseOptionalDate(
        Dictionary<string, AttributeValue> item,
        string name) =>
        GetOptional(item, name) is { } value ? ParseDate(value) : null;
}
