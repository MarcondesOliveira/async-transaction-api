using System.Globalization;
using MarcondesOliveira.AsyncTransactions.Api.Application;

namespace MarcondesOliveira.AsyncTransactions.Api.UnitTests;

public sealed class CreateTransactionUseCaseTests
{
    private static readonly Guid TransactionId =
        Guid.Parse("06f26a61-3ae3-4f80-a4e9-061a3a73ad6d");

    private static readonly Guid CorrelationId =
        Guid.Parse("f452ea72-e88d-48e9-875f-3f5e7f2736e6");

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse(
            "2026-10-28T18:45:30.123Z",
            CultureInfo.InvariantCulture);

    [Fact]
    public async Task ExecuteAsync_WithValidRequest_CreatesPendingTransaction()
    {
        var repository = new StubRepository();
        var useCase = CreateUseCase(repository);

        var outcome = await useCase.ExecuteAsync(
            ValidRequest(),
            "demo-request-000042",
            CorrelationId,
            CancellationToken.None);

        Assert.Equal(CreateTransactionOutcomeKind.Accepted, outcome.Kind);
        Assert.NotNull(outcome.Transaction);
        Assert.Equal(TransactionId, outcome.Transaction.TransactionId);
        Assert.Equal(CorrelationId, outcome.Transaction.CorrelationId);
        Assert.Equal(TransactionStatus.Pending, outcome.Transaction.Status);
        Assert.Equal(Now, outcome.Transaction.CreatedAt);
        Assert.Equal($"/transactions/{TransactionId}", outcome.Transaction.Links.Self);
        Assert.NotNull(repository.CapturedTransaction);
        Assert.Equal(64, repository.CapturedHash?.Length);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidRequest_DoesNotPersist()
    {
        var repository = new StubRepository();
        var useCase = CreateUseCase(repository);
        var request = new CreateTransactionRequest(
            "PAYMENT",
            new MoneyRequest("0.00", "USD"),
            new DestinationRequest(""),
            null);

        var outcome = await useCase.ExecuteAsync(
            request,
            "short",
            CorrelationId,
            CancellationToken.None);

        Assert.Equal(CreateTransactionOutcomeKind.ValidationFailed, outcome.Kind);
        Assert.NotNull(outcome.Errors);
        Assert.Equal(5, outcome.Errors.Count);
        Assert.Null(repository.CapturedTransaction);
    }

    [Fact]
    public async Task ExecuteAsync_WithReusedKeyAndDifferentBody_ReturnsConflict()
    {
        var repository = new StubRepository(CreatePersistenceResultKind.Conflict);
        var useCase = CreateUseCase(repository);

        var outcome = await useCase.ExecuteAsync(
            ValidRequest(),
            "demo-request-000042",
            CorrelationId,
            CancellationToken.None);

        Assert.Equal(CreateTransactionOutcomeKind.IdempotencyConflict, outcome.Kind);
        Assert.Null(outcome.Transaction);
    }

    [Theory]
    [InlineData("1.00", true)]
    [InlineData("999999999999.99", true)]
    [InlineData("0.00", false)]
    [InlineData("1", false)]
    [InlineData("1.999", false)]
    [InlineData("1000000000000.00", false)]
    public void Validate_AmountFormat_FollowsContract(
        string value,
        bool expectedValid)
    {
        var request = ValidRequest() with
        {
            Amount = new MoneyRequest(value, "BRL")
        };

        var errors = TransactionValidation.Validate(
            request,
            "demo-request-000042");

        Assert.Equal(
            expectedValid,
            errors.All(error => error.Field != "amount.value"));
    }

    private static CreateTransactionUseCase CreateUseCase(
        ITransactionRepository repository) =>
        new(
            repository,
            new FixedIdGenerator(TransactionId),
            new FixedTimeProvider(Now));

    private static CreateTransactionRequest ValidRequest() =>
        new(
            "TRANSFER",
            new MoneyRequest("125.90", "BRL"),
            new DestinationRequest("portfolio-account-001"),
            "order-2026-000042");

    private sealed class StubRepository(
        CreatePersistenceResultKind resultKind = CreatePersistenceResultKind.Created)
        : ITransactionRepository
    {
        public Transaction? CapturedTransaction { get; private set; }

        public string? CapturedHash { get; private set; }

        public Task<CreatePersistenceResult> CreateAsync(
            Transaction transaction,
            string requestHash,
            CancellationToken cancellationToken)
        {
            CapturedTransaction = transaction;
            CapturedHash = requestHash;
            return Task.FromResult(new CreatePersistenceResult(
                resultKind,
                resultKind == CreatePersistenceResultKind.Conflict
                    ? null
                    : transaction));
        }

        public Task<Transaction?> GetAsync(
            Guid transactionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Transaction?>(null);
    }

    private sealed class FixedIdGenerator(Guid value) : IIdGenerator
    {
        public Guid NewId() => value;
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
