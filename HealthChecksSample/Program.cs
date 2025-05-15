using System.Net.Mime;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSqlServer<ApplicationDbContext>(builder.Configuration.GetConnectionString("SqlConnection")!);

builder.Services.AddHealthChecks()
    .AddCheck<SampleHealthCheck>("Sample", tags: ["publish"])
    .AddTypeActivatedCheck<DatabaseHealthCheck>("SqlDatabase", builder.Configuration.GetConnectionString("SqlConnection")!)
    .AddDbContextCheck<ApplicationDbContext>("EntityFramworkCore");

// Configure the health check publisher
builder.Services.Configure<HealthCheckPublisherOptions>(options =>
{
    options.Delay = TimeSpan.FromSeconds(2);
    options.Timeout = TimeSpan.FromSeconds(30);
    options.Predicate = healthCheck => healthCheck.Tags.Contains("publish");
});

builder.Services.AddSingleton<IHealthCheckPublisher, SampleHealthCheckPublisher>();

// Sample for readiness and liveness checks
builder.Services.AddSingleton<StartupHealthCheck>();
builder.Services.AddHostedService<StartupBackgroundService>();

builder.Services.AddHealthChecks()
    .AddCheck<StartupHealthCheck>("Startup", tags: ["ready"]);

builder.Services.AddOpenApi();

var app = builder.Build();
app.UseHttpsRedirection();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", app.Environment.ApplicationName);
    });
}

app.MapGet("/api/ping", () =>
{
    return TypedResults.NoContent();
});

app.MapHealthChecks("/status", new()
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    },
    ResponseWriter = async (context, report) =>
    {
        var result = JsonSerializer.Serialize(
            new
            {
                status = report.Status.ToString(),
                duration = report.TotalDuration.TotalMilliseconds,
                details = report.Entries.Select(entry => new
                {
                    service = entry.Key,
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    exception = entry.Value.Exception?.Message,
                })
            });

        context.Response.ContentType = MediaTypeNames.Application.Json;
        await context.Response.WriteAsync(result);
    }
});

app.MapHealthChecks("/health/ready", new()
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new()
{
    Predicate = _ => false
});

app.Run();

public class SampleHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var isHealthy = true;

        if (isHealthy)
        {
            return Task.FromResult(HealthCheckResult.Healthy("OK"));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("Error"));
    }
}

public class DatabaseHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";

            await connection.OpenAsync(cancellationToken);
            await command.ExecuteScalarAsync(cancellationToken);

            return new HealthCheckResult(HealthStatus.Healthy);
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(HealthStatus.Unhealthy, "Unable to connect to database", ex);
        }
    }
}

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options);

public class SampleHealthCheckPublisher : IHealthCheckPublisher
{
    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        if (report.Status == HealthStatus.Healthy)
        {
            // ...
        }
        else
        {
            // ...
        }

        return Task.CompletedTask;
    }
}

public class StartupHealthCheck : IHealthCheck
{
    public bool StartupCompleted { get; set; }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (StartupCompleted)
        {
            return Task.FromResult(HealthCheckResult.Healthy("OK"));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("Startup is not completed"));
    }
}

public class StartupBackgroundService(StartupHealthCheck healthCheck) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        healthCheck.StartupCompleted = true;
    }
}

