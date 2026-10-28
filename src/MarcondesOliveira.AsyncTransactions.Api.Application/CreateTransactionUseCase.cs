using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MarcondesOliveira.AsyncTransactions.Api.Application;

public sealed class CreateTransactionUseCase(
    ITransactionRepository repository,
    IIdGenerator idGenerator,
    TimeProvider timeProvider)
{
    public async Task<CreateTransactionOutcome> ExecuteAsync(
        CreateTransactionRequest request,
        string? idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var errors = TransactionValidation.Validate(request, idempotencyKey);
        if (errors.Count > 0)
        {
            return new CreateTransactionOutcome(
                CreateTransactionOutcomeKind.ValidationFailed,
                Errors: errors);
        }

        var now = timeProvider.GetUtcNow();
        var transaction = new Transaction(
            idGenerator.NewId(),
            correlationId,
            request.TransactionType!,
            request.Amount!.Value!,
            request.Amount.Currency!,
            request.Destination!.AccountId!,
            request.ExternalReference,
            TransactionStatus.Pending,
            idempotencyKey!,
            now,
            now);

        var persistenceResult = await repository.CreateAsync(
            transaction,
            ComputeRequestHash(request),
            cancellationToken);

        return persistenceResult.Kind switch
        {
            CreatePersistenceResultKind.Created or CreatePersistenceResultKind.Existing =>
                new CreateTransactionOutcome(
                    CreateTransactionOutcomeKind.Accepted,
                    ToResponse(persistenceResult.Transaction!)),
            CreatePersistenceResultKind.Conflict =>
                new CreateTransactionOutcome(CreateTransactionOutcomeKind.IdempotencyConflict),
            _ => throw new InvalidOperationException("Unsupported persistence result")
        };
    }

    private static string ComputeRequestHash(CreateTransactionRequest request)
    {
        var json = JsonSerializer.Serialize(request, JsonSerializerOptions.Web);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(hash);
    }

    private static CreateTransactionResponse ToResponse(Transaction transaction) =>
        new(
            transaction.TransactionId,
            transaction.CorrelationId,
            transaction.Status,
            transaction.CreatedAt,
            transaction.UpdatedAt,
            new TransactionLinks($"/transactions/{transaction.TransactionId}"));
}
