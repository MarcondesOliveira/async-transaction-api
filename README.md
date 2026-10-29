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
token. The validation job neither publishes deployment packages nor changes AWS resources.
Tests use controlled substitutes, so no manual HTTP test or deployed table is
required for this CI-only change. A passing workflow is not branch protection;
required checks must be configured separately if desired.

### Versioned Lambda artifact

After validation succeeds on `main` (push or manual dispatch), a separate job
publishes the Host in Release for `linux-arm64`, framework-dependent, without
ReadyToRun or an apphost. Pull requests do not generate deployment artifacts.
The job stores the following bundle in GitHub Actions for seven days:

- `async-transaction-api.zip`: Lambda package with assemblies at its root;
- `async-transaction-api.zip.sha256`: SHA-256 verification file;
- `build-manifest.json`: full source commit, SDK, runtime, architecture, package
  hash in hexadecimal/Base64, and the future S3 key `<service>/<commit>/<service>.zip`.

The Actions artifact name includes the commit, run ID and attempt, so reruns do
not overwrite earlier bundles. This records build identity, not a guarantee of
byte-for-byte reproducible ZIPs across builds. The Base64 `sourceCodeHash` is for
the inner Lambda ZIP, not GitHub's outer download archive/digest.

Download the Actions artifact and extract its outer archive before using the
Lambda ZIP. From that extracted directory, run `sha256sum --check
async-transaction-api.zip.sha256`. `.github/scripts/package.sh` contains the same
packaging procedure and refuses to overwrite an existing output directory.

No AWS credentials/OIDC are requested by this job. S3 publication and Lambda
deployment run in the downstream `Deliver API` workflow; a green package job
alone does not change the running API. Manual HTTP checks belong after delivery.

References: [checkout](https://github.com/actions/checkout),
[setup-dotnet](https://github.com/actions/setup-dotnet),
[NuGet audit command](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-package-list).

### Automated delivery through OIDC

`Deliver API` (`.github/workflows/publish.yml`) runs after a successful `CI`
triggered by a push to this repository's `main`. PRs, forks, failed/cancelled CI
and manual CI runs do not automatically deploy. The existing CI bundle is
verified and published without rebuilding. The workflow then dispatches the
central Terraform pipeline and waits for its exact run to finish successfully,
including Lambda package verification and an end-to-end synthetic transaction.
A successful S3 upload alone is not reported as successful delivery.

`INFRA_WORKFLOW_TOKEN` is a short-lived fine-grained Actions read/write token
restricted to the infrastructure repository, supplied only to the dispatch/wait
step. It is not an AWS key and is never stored in source or workflow inputs.
Missing/expired tokens, rejected dispatches and unsuccessful central runs fail
delivery visibly. The dispatch API returns the exact run ID; no latest-run lookup
is used. See [GitHub workflow dispatch API](https://docs.github.com/en/rest/actions/workflows#create-a-workflow-dispatch-event).

Deliveries queue rather than replace pending jobs. The central workflow serializes
Terraform changes per stack and preserves other services' deployed packages.
The wait is bounded to 40 minutes; timeout does not cancel a possibly running
central deployment. Inspect the linked run before retrying. No automatic rollback
or duplicate dispatch is performed.

Repository variables hold deployment metadata outside source code:
`AWS_REGION`, `AWS_ACCOUNT_ID`, `ARTIFACT_BUCKET` and `AWS_ARTIFACT_ROLE_ARN`.
They are not AWS access keys. The existing IAM trust must match this repository's
verified immutable OIDC subject, audience STS and `main` branch. No environment
name is configured on this job because that would change its subject format.

1. Wait for the chosen `CI` run on `main` to succeed, including packaging.
2. Normally the push starts delivery automatically. For an explicit manual
   delivery, open **Actions → Deliver API → Run workflow**, select `main`, supply
   the numeric `ci_run_id` and type `PUBLISH` in `confirmation`.
3. The job checks the run's workflow path, repository, event, branch and conclusion,
   downloads its exact commit/run/attempt artifact, and verifies the fixed package
   name, target, ZIP layout, manifest and both checksum representations.
4. Only then it assumes the existing artifact-publisher role via OIDC, checks the
   AWS account and uploads the ZIP plus manifest/checksum to the commit prefix.
   Conditional `If-None-Match: *` writes refuse existing keys. The final ZIP
   checksum, encryption and source-commit metadata are checked against S3.

The publication job receives `id-token: write`; CI jobs remain without AWS credentials.
It requests a 15-minute role session, masks deployment identifiers in logs and
uses actions pinned by SHA. The role remains shared with other application
repositories: per-repository IAM isolation is not introduced here. Conditional
writes protect this workflow, not every possible client of the bucket.

An existing key produces a failure rather than replacement, including on rerun.
The three object writes are not atomic: a partial failure can leave an uploaded
ZIP without its companion files. Inspect the error, object/version and checksums
with the owner before any retry or cleanup; there is no automatic delete/rollback.
An expired GitHub artifact cannot be published with this workflow. Do not rebuild
the same commit and overwrite its existing S3 package to work around either case.

After publication, the central workflow applies the matching artifact using
Terraform and verifies the Lambda and synthetic transaction. A central failure,
cancellation or timeout fails this delivery rather than hiding it behind the S3
upload. The run URL is printed and written in the workflow summary.
After successful delivery, a manual Bruno/Swagger POST `/transactions` followed
by GET `/transactions/{transactionId}` can confirm the updated live flow; use
synthetic data and a new `Idempotency-Key`. Expect eventual `SUCCEEDED/APPROVED`.
The first automatic push-to-deploy execution still needs validation after this
change is pushed; only the central deploy has already been exercised.

Orchestration safeguards can be tested locally without GitHub or AWS requests:
`python3 -m unittest discover -s tests/delivery -v`. CI also runs these tests.

References: [AWS OIDC action](https://github.com/aws-actions/configure-aws-credentials),
[S3 conditional writes](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-writes.html).

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
