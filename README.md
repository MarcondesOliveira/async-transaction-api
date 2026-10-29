# Async Transaction API

Serverless .NET API that creates and queries transactions in the Async Transaction Processing portfolio project.

The API accepts a transaction synchronously, stores it as `PENDING` in DynamoDB and returns `202 Accepted`. Downstream processing is intentionally outside this repository and starts from the DynamoDB Stream.

## Responsibilities

- `POST /transactions`
- `GET /transactions/{transactionId}`
- request validation
- idempotent transaction creation
- transactional DynamoDB persistence
- interactive Swagger UI at `/swagger`
- OpenAPI document at `/openapi/v1.json`

This repository does not consume DynamoDB Streams or SQS, call the external processor, process callbacks or provision AWS resources.

## Technology

- .NET 10 LTS
- ASP.NET Core Minimal API
- AWS Lambda managed `dotnet10` runtime
- API Gateway HTTP API event model
- Amazon DynamoDB
- xUnit

## Project structure

```text
src/
├── MarcondesOliveira.AsyncTransactions.Api.Host
├── MarcondesOliveira.AsyncTransactions.Api.Application
└── MarcondesOliveira.AsyncTransactions.Api.Infrastructure
tests/
├── MarcondesOliveira.AsyncTransactions.Api.UnitTests
├── MarcondesOliveira.AsyncTransactions.Api.IntegrationTests
└── MarcondesOliveira.AsyncTransactions.Api.ContractTests
```

The Application project owns use cases, contracts and ports. Infrastructure implements DynamoDB persistence. Host owns HTTP and Lambda composition.

## Requirements

- .NET SDK 10.0
- AWS credentials and a compatible DynamoDB table only when running the production adapter manually

Unit, contract and HTTP integration tests do not require AWS resources.

## Swagger

Run the Host project and open the interactive documentation:

```bash
dotnet run --project src/MarcondesOliveira.AsyncTransactions.Api.Host
```

```text
http://localhost:5276/swagger
```

Use the HTTP or HTTPS address printed by `dotnet run` if the local profile selects a different port. The raw OpenAPI document is available at `/openapi/v1.json`.

## Build and test

```bash
dotnet restore MarcondesOliveira.AsyncTransactions.Api.slnx --disable-parallel
dotnet build MarcondesOliveira.AsyncTransactions.Api.slnx --no-restore --configuration Release
dotnet test MarcondesOliveira.AsyncTransactions.Api.slnx --no-build --configuration Release
```

## Continuous integration

The `CI` workflow runs on pull requests targeting `main`, pushes to `main`, and
manual dispatch. It restores dependencies, verifies formatting, builds Release
with the configured .NET analyzers/warnings as errors, runs all three test projects,
and audits direct/transitive NuGet dependencies. The audit fails on reported
vulnerabilities; its JSON report is printed in the job log for investigation.

Actions are pinned to commit SHAs. The job uses a read-only repository token,
does not persist checkout credentials, and requests no AWS credentials or OIDC
token. It neither publishes deployment packages nor changes AWS resources.
Tests use controlled substitutes, so no manual HTTP test or deployed table is
required for this CI-only change. A passing workflow is not branch protection;
required checks must be configured separately if desired.

References: [checkout](https://github.com/actions/checkout),
[setup-dotnet](https://github.com/actions/setup-dotnet),
[NuGet audit command](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-package-list).

## Operational logs

Application events use the `TransactionApi` logger category with source-generated
`LoggerMessage` methods and the ASP.NET JSON console formatter:

- `TransactionApiHandled`: POST acceptance, validation failure or idempotency
  conflict; GET success, invalid UUID or missing transaction. Includes operation,
  HTTP status and `RequestCorrelationId`. `TransactionId` and `CorrelationId`
  identify the persisted transaction when available; a missing-transaction GET
  includes the valid requested transaction UUID without a stored correlation.
- `TransactionApiFailed`: JSON/binding failures and unexpected exceptions handled
  by the HTTP error handler. Includes HTTP status, request correlation, exception
  and root-cause types and stack trace, without exception messages or raw bodies.

On POST replay, `CorrelationId` remains the original transaction's correlation,
while `RequestCorrelationId` identifies the current HTTP call. A GET similarly
keeps lookup-request and stored transaction correlations separate. Correlation
created before persistence is retained for a subsequent exception response/log.

The custom events do not log amounts, accounts, external references or idempotency
keys. The default exception-handler middleware category is suppressed to avoid
duplicating exceptions with potentially sensitive messages; the custom failure
event replaces it. Tests inspect structured log state, not rendered text.

JSON properties are emitted in the ASP.NET console format's `State` object.
Keep Lambda's runtime log format as `Text` for this function to avoid wrapping
application JSON again; the SQS handlers use a different native `ILambdaLogger`
format. These changes require a reviewed package deployment before appearing in AWS.

After deployment, use Bruno or Swagger to POST a new transaction and GET its ID.
Inspect the transaction-api log group for the handled events and matching
identifiers, then follow the transaction through the Request and Callback Handler
groups. A repeated identical POST can verify that a new request correlation does
not replace the stored transaction correlation.

## Configuration

| Setting | Default | Purpose |
|---|---|---|
| `DynamoDb__TableName` | `AsyncTransactions` | DynamoDB table containing transaction and idempotency items. |
| `AWS_REGION` | AWS SDK resolution chain | Region used by the DynamoDB client. |

The AWS SDK default credential chain is used. Credentials and account identifiers must never be stored in this repository.

## Create transaction

```http
POST /transactions HTTP/1.1
Content-Type: application/json
Idempotency-Key: demo-request-000042
X-Correlation-Id: f452ea72-e88d-48e9-875f-3f5e7f2736e6

{
  "transactionType": "TRANSFER",
  "amount": {
    "value": "125.90",
    "currency": "BRL"
  },
  "destination": {
    "accountId": "portfolio-account-001"
  },
  "externalReference": "order-2026-000042"
}
```

The first valid request creates a transaction and returns `202`. Repeating the same idempotency key and body returns the original transaction. Reusing the key with different content returns `409`.

## Query transaction

```http
GET /transactions/06f26a61-3ae3-4f80-a4e9-061a3a73ad6d HTTP/1.1
```

The endpoint returns the current transaction state or `404` when it does not exist.

See [docs/api-contract.md](docs/api-contract.md) for validation and response details.

## DynamoDB writes

Creation uses `TransactWriteItems` to atomically insert:

- `TRANSACTION#{transactionId}` — the transaction with status `PENDING`;
- `IDEMPOTENCY#{idempotencyKey}` — the request hash and transaction reference.

Both items use `PK` and `SK`. The idempotency item expires after 24 hours through DynamoDB TTL. The table and all permissions are owned by the infrastructure repository.

## Lambda packaging

The deployment artifact is a framework-dependent ZIP for the managed `dotnet10` runtime. Its Lambda handler is the Host assembly name:

```text
MarcondesOliveira.AsyncTransactions.Api
```

Packaging and upload will be added with the CI/CD stage. No application repository creates AWS resources.

## Current scope

The API has been deployed and smoke-tested on AWS through API Gateway, Lambda
and DynamoDB. The asynchronous request pipeline and GitHub Actions CI/CD belong
to later project stages.
