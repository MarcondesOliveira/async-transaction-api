namespace MarcondesOliveira.AsyncTransactions.Api.Application;

public enum TransactionStatus
{
    Pending,
    Processing,
    Succeeded,
    Rejected,
    Failed
}

public sealed record Transaction(
    Guid TransactionId,
    Guid CorrelationId,
    string TransactionType,
    string AmountValue,
    string AmountCurrency,
    string DestinationAccountId,
    string? ExternalReference,
    TransactionStatus Status,
    string RequestIdempotencyKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ProcessorTransactionId = null,
    string? ResultCode = null,
    string? ResultMessage = null,
    DateTimeOffset? CompletedAt = null);

public enum CreatePersistenceResultKind
{
    Created,
    Existing,
    Conflict
}

public sealed record CreatePersistenceResult(
    CreatePersistenceResultKind Kind,
    Transaction? Transaction = null);

public enum CreateTransactionOutcomeKind
{
    Accepted,
    ValidationFailed,
    IdempotencyConflict
}

public sealed record CreateTransactionOutcome(
    CreateTransactionOutcomeKind Kind,
    CreateTransactionResponse? Transaction = null,
    IReadOnlyCollection<ErrorDetail>? Errors = null);
