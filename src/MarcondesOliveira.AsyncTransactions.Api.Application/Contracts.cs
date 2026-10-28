namespace MarcondesOliveira.AsyncTransactions.Api.Application;

public sealed record CreateTransactionRequest(
    string? TransactionType,
    MoneyRequest? Amount,
    DestinationRequest? Destination,
    string? ExternalReference);

public sealed record MoneyRequest(string? Value, string? Currency);

public sealed record DestinationRequest(string? AccountId);

public sealed record MoneyResponse(string Value, string Currency);

public sealed record DestinationResponse(string AccountId);

public sealed record TransactionLinks(string Self);

public sealed record CreateTransactionResponse(
    Guid TransactionId,
    Guid CorrelationId,
    TransactionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    TransactionLinks Links);

public sealed record TransactionResultResponse(string Code, string Message);

public sealed record TransactionResponse(
    Guid TransactionId,
    Guid CorrelationId,
    string TransactionType,
    MoneyResponse Amount,
    DestinationResponse Destination,
    string? ExternalReference,
    TransactionStatus Status,
    Guid? ProcessorTransactionId,
    TransactionResultResponse? Result,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record ErrorDetail(string Field, string Message);

public sealed record ErrorResponse(
    string Code,
    string Message,
    Guid CorrelationId,
    IReadOnlyCollection<ErrorDetail>? Details = null);
