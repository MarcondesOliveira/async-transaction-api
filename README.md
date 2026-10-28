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
