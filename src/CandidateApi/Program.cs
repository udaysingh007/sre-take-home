using CandidateApi.Configuration;
using CandidateApi.Contracts;
using CandidateApi.Services;
using Microsoft.Extensions.Options;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole();

builder.Services
    .AddOptions<CandidateApiOptions>()
    .Bind(builder.Configuration.GetSection(CandidateApiOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<ReadinessEvaluator>();

var app = builder.Build();

app.UseHttpMetrics();

// -----------------------------------------------------------------------
// Chaos / fault-injection (for incident-response demo)
// Uncomment the two lines below to enable.  After CHAOS_DELAY_MINUTES
// (default 5) the /api/work-items endpoint will start returning 500s,
// which burns the availability error budget and fires SLO alerts.
// -----------------------------------------------------------------------
var chaosStart = DateTime.UtcNow.AddMinutes(
    int.TryParse(Environment.GetEnvironmentVariable("CHAOS_DELAY_MINUTES"), out var d) ? d : 5);
var chaosEnabled = true;
// var chaosStart = DateTime.MaxValue;
// var chaosEnabled = false;

app.MapGet("/", (IOptions<CandidateApiOptions> options, IWebHostEnvironment environment) =>
{
    var response = new ServiceMetadataResponse(
        options.Value.ServiceName,
        environment.EnvironmentName,
        options.Value.Region,
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
        DateTimeOffset.UtcNow,
        "Stable release — all systems operational");

    return Results.Ok(response);
});

app.MapGet("/health/live", () => Results.Ok(new { status = "Alive" }));

app.MapGet(
    "/health/ready",
    (IOptions<CandidateApiOptions> options, ReadinessEvaluator readinessEvaluator) =>
    {
        var report = readinessEvaluator.Evaluate(options.Value.Dependencies);
        return report.Status == "Healthy"
            ? Results.Ok(report)
            : Results.Json(report, statusCode: StatusCodes.Status503ServiceUnavailable);
    });

app.MapGet("/api/work-items", (IOptions<CandidateApiOptions> options) =>
{
    // Chaos: return 500 after the delay elapses (when enabled)
    if (chaosEnabled && DateTime.UtcNow >= chaosStart)
        return Results.StatusCode(500);

    var items = options.Value.WorkItems
        .Select(item => new WorkItemResponse(
            item.Id,
            item.Title,
            item.Team,
            item.Priority,
            item.Status))
        .ToArray();

    return Results.Ok(items);
});

app.MapMetrics();

app.Run();
