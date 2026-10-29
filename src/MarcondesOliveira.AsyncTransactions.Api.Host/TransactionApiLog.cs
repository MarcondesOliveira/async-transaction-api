namespace MarcondesOliveira.AsyncTransactions.Api;

internal static partial class TransactionApiLog
{
    private static readonly object CorrelationKey = new();

    public static void SetRequestCorrelation(HttpContext context, Guid correlationId) =>
        context.Items[CorrelationKey] = correlationId;

    public static Guid GetRequestCorrelation(HttpContext context) =>
        context.Items[CorrelationKey] is Guid value
            ? value
            : CorrelationId.ResolveOrCreate(context.Request.Headers);

    public static void Handled(HttpContext context, string operation, int statusCode,
        Guid requestCorrelationId, Guid? transactionId = null, Guid? correlationId = null)
    {
        var logger = Logger(context);
        if (logger.IsEnabled(LogLevel.Information))
        {
            WriteHandled(logger, "TransactionApiHandled", operation, statusCode,
                transactionId, correlationId, requestCorrelationId);
        }
    }

    public static void Failed(HttpContext context, int statusCode, Guid correlationId, Exception? exception)
    {
        var logger = Logger(context);
        var level = statusCode >= 500 ? LogLevel.Error : LogLevel.Warning;
        if (logger.IsEnabled(level))
        {
            var errorType = exception?.GetType().Name;
            var causeType = exception?.GetBaseException().GetType().Name;
            var stackTrace = exception?.StackTrace;
            WriteFailure(logger, level, "TransactionApiFailed", statusCode, correlationId,
                errorType, causeType, stackTrace);
        }
    }

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TransactionApi");

    [LoggerMessage(EventId = 101, Level = LogLevel.Information,
        Message = "{EventName} operation={Operation} httpStatus={HttpStatus} transactionId={TransactionId} correlationId={CorrelationId} requestCorrelationId={RequestCorrelationId}")]
    private static partial void WriteHandled(ILogger logger, string eventName, string operation,
        int httpStatus, Guid? transactionId, Guid? correlationId, Guid requestCorrelationId);

    [LoggerMessage(EventId = 102,
        Message = "{EventName} httpStatus={HttpStatus} requestCorrelationId={RequestCorrelationId} errorType={ErrorType} causeType={CauseType} stackTrace={StackTrace}")]
    private static partial void WriteFailure(ILogger logger, LogLevel level, string eventName,
        int httpStatus, Guid requestCorrelationId, string? errorType, string? causeType, string? stackTrace);
}
