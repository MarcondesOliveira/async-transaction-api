using MarcondesOliveira.AsyncTransactions.Api.Application;

namespace MarcondesOliveira.AsyncTransactions.Api;

public static class TransactionEndpoints
{
    public static IEndpointRouteBuilder MapTransactionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/transactions", CreateTransactionAsync)
            .WithName("CreateTransaction")
            .WithSummary("Creates a transaction for asynchronous processing")
            .Produces<CreateTransactionResponse>(StatusCodes.Status202Accepted)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        endpoints.MapGet("/transactions/{transactionId}", GetTransactionAsync)
            .WithName("GetTransaction")
            .WithSummary("Gets the current transaction state")
            .Produces<TransactionResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> CreateTransactionAsync(
        CreateTransactionRequest request,
        HttpContext context,
        CreateTransactionUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (!CorrelationId.TryResolve(
                context.Request.Headers,
                out var correlationId))
        {
            var generatedCorrelationId = Guid.NewGuid();
            return Results.BadRequest(new ErrorResponse(
                "VALIDATION_ERROR",
                "The request contains invalid fields",
                generatedCorrelationId,
                [new ErrorDetail(
                    "X-Correlation-Id",
                    "Header must be a UUID when provided")]));
        }

        var outcome = await useCase.ExecuteAsync(
            request,
            context.Request.Headers["Idempotency-Key"].ToString(),
            correlationId,
            cancellationToken);

        return outcome.Kind switch
        {
            CreateTransactionOutcomeKind.Accepted => Accepted(context, outcome.Transaction!),
            CreateTransactionOutcomeKind.ValidationFailed => Results.BadRequest(
                new ErrorResponse(
                    "VALIDATION_ERROR",
                    "The request contains invalid fields",
                    correlationId,
                    outcome.Errors)),
            CreateTransactionOutcomeKind.IdempotencyConflict => Results.Conflict(
                new ErrorResponse(
                    "IDEMPOTENCY_KEY_REUSED",
                    "The idempotency key was already used with different content",
                    correlationId)),
            _ => throw new InvalidOperationException("Unsupported transaction outcome")
        };
    }

    private static async Task<IResult> GetTransactionAsync(
        string transactionId,
        HttpContext context,
        GetTransactionUseCase useCase,
        CancellationToken cancellationToken)
    {
        var correlationId = CorrelationId.ResolveOrCreate(context.Request.Headers);
        if (!Guid.TryParse(transactionId, out var parsedTransactionId))
        {
            return Results.BadRequest(new ErrorResponse(
                "VALIDATION_ERROR",
                "The request contains invalid fields",
                correlationId,
                [new ErrorDetail(
                    "transactionId",
                    "Value must be a UUID")]));
        }

        var transaction = await useCase.ExecuteAsync(
            parsedTransactionId,
            cancellationToken);

        return transaction is null
            ? Results.NotFound(new ErrorResponse(
                "TRANSACTION_NOT_FOUND",
                "The transaction was not found",
                correlationId))
            : Results.Ok(transaction);
    }

    private static IResult Accepted(
        HttpContext context,
        CreateTransactionResponse transaction)
    {
        context.Response.Headers.Location = transaction.Links.Self;
        return Results.Json(transaction, statusCode: StatusCodes.Status202Accepted);
    }
}
