using System.Net.Mime;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSqlServer<ApplicationDbContext>(builder.Configuration.GetConnectionString("SqlConnection")!);

builder.Services.AddHealthChecks()
    .AddCheck<SampleHealthCheck>("Sample")
    .AddTypeActivatedCheck<DatabaseHealthCheck>("Database", builder.Configuration.GetConnectionString("SqlConnection")!)
    .AddDbContextCheck<ApplicationDbContext>("Database");

// Sample for readiness and liveness checks
builder.Services.AddSingleton<StartupHealthCheck>();
builder.Services.AddHostedService<StartupBackgroundService>();

builder.Services.AddHealthChecks()
    .AddCheck<StartupHealthCheck>("Startup", tags: ["ready"]);

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseHttpsRedirection();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    _ = app.UseSwagger();
    _ = app.UseSwaggerUI();
}

//app.MapGet("/api/ping", () =>
//{
//    return TypedResults.NoContent();
//})
//.WithOpenApi();

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
            _ = await command.ExecuteScalarAsync(cancellationToken);

            return new HealthCheckResult(HealthStatus.Healthy);
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(HealthStatus.Unhealthy, "Unable to connect to database", ex);
        }
    }
}

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options);

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

