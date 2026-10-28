namespace MarcondesOliveira.AsyncTransactions.Api.Infrastructure;

public sealed class DynamoDbOptions
{
    public const string SectionName = "DynamoDb";

    public string TableName { get; init; } = "AsyncTransactions";
}
