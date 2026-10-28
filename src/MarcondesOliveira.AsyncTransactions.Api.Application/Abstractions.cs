namespace MarcondesOliveira.AsyncTransactions.Api.Application;

public interface ITransactionRepository
{
    Task<CreatePersistenceResult> CreateAsync(
        Transaction transaction,
        string requestHash,
        CancellationToken cancellationToken);

    Task<Transaction?> GetAsync(Guid transactionId, CancellationToken cancellationToken);
}

public interface IIdGenerator
{
    Guid NewId();
}

public sealed class GuidIdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.NewGuid();
}
