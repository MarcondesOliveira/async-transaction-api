namespace MarcondesOliveira.AsyncTransactions.Api.Application;

public sealed class GetTransactionUseCase(ITransactionRepository repository)
{
    public async Task<TransactionResponse?> ExecuteAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetAsync(transactionId, cancellationToken);
        return transaction is null ? null : ToResponse(transaction);
    }

    private static TransactionResponse ToResponse(Transaction transaction) =>
        new(
            transaction.TransactionId,
            transaction.CorrelationId,
            transaction.TransactionType,
            new MoneyResponse(transaction.AmountValue, transaction.AmountCurrency),
            new DestinationResponse(transaction.DestinationAccountId),
            transaction.ExternalReference,
            transaction.Status,
            transaction.ProcessorTransactionId,
            transaction.ResultCode is null || transaction.ResultMessage is null
                ? null
                : new TransactionResultResponse(
                    transaction.ResultCode,
                    transaction.ResultMessage),
            transaction.CreatedAt,
            transaction.UpdatedAt,
            transaction.CompletedAt);
}
