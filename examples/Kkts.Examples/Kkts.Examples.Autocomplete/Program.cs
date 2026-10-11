using Kkts.Expressions;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json.Serialization;

if (args.Contains("--performance"))
    return Performance.Run();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 65536);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.AllowDuplicateProperties = false;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
var metadata = new SampleMetadata();

app.MapPost("/api/completion", Results<Ok<CompletionResponse>, ProblemHttpResult> (
    CompletionRequest request, CancellationToken cancellationToken) =>
{
    cancellationToken.ThrowIfCancellationRequested();
    if (request.Text is null || request.Offset is null || request.Snapshot is null || request.Snapshot < 0)
        return TypedResults.Problem(statusCode: 400, title: "Text, offset, and a nonnegative snapshot are required.");
    var offset = request.Offset.Value;
    if (offset < 0 || offset > request.Text.Length ||
        offset > 0 && offset < request.Text.Length &&
            char.IsHighSurrogate(request.Text[offset - 1]) && char.IsLowSurrogate(request.Text[offset]))
        return TypedResults.Problem(statusCode: 400, title: "Offset must be a valid UTF-16 boundary in text.");
    var completion = metadata.Context.CompleteExpression(
        request.Text, offset, metadata.Variables, metadata.Hints);
    var diagnostics = completion.Status == ExpressionCompletionStatus.LimitExceeded
        ? completion.Diagnostics
        : metadata.Context.AnalyzeExpression(request.Text, metadata.Variables).Diagnostics
            .Concat(completion.Diagnostics).ToArray();
    var visibleDiagnostics = diagnostics.GroupBy(item => (item.Code, item.Start, item.Length))
        .Select(group => group.First()).OrderBy(item => item.Start).ToArray();
    var output = visibleDiagnostics.Take(32).Select(item =>
        new DiagnosticResponse(item.Code, item.Message, item.Start, item.Length, item.Kind.ToString())).ToList();
    if (visibleDiagnostics.Length > 32)
        output[31] = new DiagnosticResponse("query-policy-diagnostics-truncated",
            "Additional diagnostics were omitted.", output[31].Start, 0, "Syntax");
    return TypedResults.Ok(new CompletionResponse(request.Snapshot.Value, request.Text, offset,
        completion.Status.ToString(), completion.IsIncomplete,
        completion.Items.Select(item => new ItemResponse(
            item.Kind.ToString(), item.Label, item.InsertionText, item.Start, item.Length, item.Description,
            item.TypeInfo is null ? null : new PublicTypeResponse(item.TypeInfo.ClrType?.Name,
                item.TypeInfo.Nullability.ToString(), item.TypeInfo.ElementType?.Name))).ToArray(), output));
})
    .WithName("CompleteExpression")
    .WithSummary("Complete an exact editor snapshot using server-owned metadata.")
    .WithDescription("Returns advisory completions and same-snapshot diagnostics, not executable predicates.");

app.Run();
return 0;

/// <summary>Exact editor source, UTF-16 cursor, and client snapshot identity.</summary>
public sealed record CompletionRequest(string? Text, int? Offset, long? Snapshot);
/// <summary>Public scalar or collection type summary, never a CLR Type object.</summary>
public sealed record PublicTypeResponse(string? Name, string Nullability, string? ElementType);
/// <summary>One exact source replacement and its public presentation metadata.</summary>
public sealed record ItemResponse(string Kind, string Label, string InsertionText, int Start, int Length,
    string Description, PublicTypeResponse? Type);
/// <summary>A diagnostic range in the requested source snapshot.</summary>
public sealed record DiagnosticResponse(string Code, string Message, int Start, int Length, string Kind);
/// <summary>Same-snapshot suggestions and diagnostics from server-owned query metadata.</summary>
public sealed record CompletionResponse(long Snapshot, string Text, int Offset, string Status, bool IsIncomplete,
    IReadOnlyList<ItemResponse> Items, IReadOnlyList<DiagnosticResponse> Diagnostics);
