using System.Text.Json;

namespace MarcondesOliveira.AsyncTransactions.Api;

public sealed class UpperCaseJsonNamingPolicy : JsonNamingPolicy
{
    public static UpperCaseJsonNamingPolicy Instance { get; } = new();

    private UpperCaseJsonNamingPolicy()
    {
    }

    public override string ConvertName(string name) => name.ToUpperInvariant();
}
