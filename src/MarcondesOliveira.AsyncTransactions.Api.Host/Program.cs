using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.DynamoDBv2;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using MarcondesOliveira.AsyncTransactions.Api;
using MarcondesOliveira.AsyncTransactions.Api.Application;
using MarcondesOliveira.AsyncTransactions.Api.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
// The custom failure event omits exception messages, which can contain request data.
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(UpperCaseJsonNamingPolicy.Instance));
});

builder.Services.AddOpenApi();
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
builder.Services.Configure<RouteHandlerOptions>(options =>
    options.ThrowOnBadRequest = true);

var dynamoDbOptions = builder.Configuration
    .GetSection(DynamoDbOptions.SectionName)
    .Get<DynamoDbOptions>() ?? new DynamoDbOptions();

builder.Services.AddSingleton(dynamoDbOptions);
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddSingleton<ITransactionRepository, DynamoDbTransactionRepository>();
builder.Services.AddSingleton<IIdGenerator, GuidIdGenerator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CreateTransactionUseCase>();
builder.Services.AddScoped<GetTransactionUseCase>();

var app = builder.Build();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var correlationId = TransactionApiLog.GetRequestCorrelation(context);
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var isBadRequest = exception is BadHttpRequestException or JsonException;
        context.Response.StatusCode = isBadRequest
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status500InternalServerError;
        TransactionApiLog.Failed(context, context.Response.StatusCode, correlationId, exception);
        await context.Response.WriteAsJsonAsync(
            new ErrorResponse(
                isBadRequest ? "VALIDATION_ERROR" : "INTERNAL_ERROR",
                isBadRequest
                    ? "The request contains invalid fields"
                    : "An unexpected error occurred",
                correlationId),
            context.RequestAborted);
    });
});

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";
    options.SwaggerEndpoint(
        "/openapi/v1.json",
        "Async Transaction API v1");
});
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .ExcludeFromDescription();
app.MapTransactionEndpoints();

app.Run();

public partial class Program;
