using System.Globalization;

namespace MarcondesOliveira.AsyncTransactions.Api.Application;

public static class TransactionValidation
{
    public static IReadOnlyCollection<ErrorDetail> Validate(
        CreateTransactionRequest request,
        string? idempotencyKey)
    {
        var errors = new List<ErrorDetail>();

        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Length is < 8 or > 128 ||
            idempotencyKey.Any(character => character is < '!' or > '~'))
        {
            errors.Add(new ErrorDetail(
                "Idempotency-Key",
                "Header must contain 8 to 128 printable ASCII characters without spaces"));
        }

        if (!string.Equals(request.TransactionType, "TRANSFER", StringComparison.Ordinal))
        {
            errors.Add(new ErrorDetail(
                "transactionType",
                "Value must be TRANSFER"));
        }

        ValidateAmount(request.Amount, errors);

        if (string.IsNullOrWhiteSpace(request.Destination?.AccountId) ||
            request.Destination.AccountId.Length > 64)
        {
            errors.Add(new ErrorDetail(
                "destination.accountId",
                "Value must contain 1 to 64 characters"));
        }

        if (request.ExternalReference?.Length > 100)
        {
            errors.Add(new ErrorDetail(
                "externalReference",
                "Value must contain at most 100 characters"));
        }

        return errors;
    }

    private static void ValidateAmount(
        MoneyRequest? amount,
        List<ErrorDetail> errors)
    {
        if (amount is null || !HasValidMonetaryFormat(amount.Value) ||
            !decimal.TryParse(
                amount.Value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsedValue) ||
            parsedValue <= 0)
        {
            errors.Add(new ErrorDetail(
                "amount.value",
                "Value must be greater than zero, contain up to 12 integer digits and exactly two decimal places"));
        }

        if (!string.Equals(amount?.Currency, "BRL", StringComparison.Ordinal))
        {
            errors.Add(new ErrorDetail(
                "amount.currency",
                "Value must be BRL"));
        }
    }

    private static bool HasValidMonetaryFormat(string? value)
    {
        if (value is null)
        {
            return false;
        }

        var separatorIndex = value.IndexOf('.', StringComparison.Ordinal);
        if (separatorIndex is < 1 or > 12 || separatorIndex != value.Length - 3)
        {
            return false;
        }

        return value[..separatorIndex].All(char.IsAsciiDigit) &&
               value[(separatorIndex + 1)..].All(char.IsAsciiDigit);
    }
}
