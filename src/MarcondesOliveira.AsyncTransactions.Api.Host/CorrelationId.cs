namespace MarcondesOliveira.AsyncTransactions.Api;

public static class CorrelationId
{
    private const string HeaderName = "X-Correlation-Id";

    public static bool TryResolve(
        IHeaderDictionary headers,
        out Guid correlationId)
    {
        var value = headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            correlationId = Guid.NewGuid();
            return true;
        }

        return Guid.TryParse(value, out correlationId);
    }

    public static Guid ResolveOrCreate(IHeaderDictionary headers) =>
        TryResolve(headers, out var correlationId)
            ? correlationId
            : Guid.NewGuid();
}
