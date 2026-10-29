using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Filters;
using Broot.Redirect.API.Middleware;
using Broot.Redirect.API.Services;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Services;
using Broot.Redirect.Infrastructure.Extensions;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

var appInsightsConnectionString = builder.Configuration
    .GetValue<string>("APPLICATIONINSIGHTS__CONNECTIONSTRING")
    ?? builder.Configuration
    .GetValue<string>("ApplicationInsights:ConnectionString");

if (!string.IsNullOrEmpty(appInsightsConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry(options =>
    {
        options.ConnectionString = appInsightsConnectionString;
    });

    Console.WriteLine("Application Insights telemetry enabled.");
}
else
{
    Console.WriteLine("Application Insights not configured. Running without telemetry.");
}

builder.Services.Configure<BrootRedirectOptions>(
    builder.Configuration.GetSection(BrootRedirectOptions.SectionName));

builder.Services.AddSmartRedirectInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IRuleMatchingService, RuleMatchingService>();
builder.Services.AddSingleton<IUrlTransformService, UrlTransformService>();
builder.Services.AddSingleton<ISmartSearchService, SmartSearchService>();
builder.Services.AddSingleton<BruteForceProtectionService>();
builder.Services.AddSingleton<RuleValidationService>();
builder.Services.AddSingleton<IntegrationWriteLock>();
builder.Services.AddSingleton<QmProRuleService>();
builder.Services.AddSingleton<AreaRuleSyncService>();
builder.Services.AddSingleton<QmProApiKeyFilter>();

builder.Services.AddDistributedMemoryCache();

var allowTeamsEmbedding = builder.Configuration
    .GetSection(BrootRedirectOptions.SectionName)
    .GetValue<bool>("AllowTeamsEmbedding");

builder.Services.AddSession(options =>
{
    options.Cookie.Name = "admin_session";
    options.Cookie.HttpOnly = true;
    TeamsEmbedding.ConfigureSessionCookie(options.Cookie, allowTeamsEmbedding, builder.Environment.IsProduction());
    options.Cookie.Path = "/";
    options.Cookie.IsEssential = true;

    var sessionTimeoutDays = builder.Configuration
        .GetSection(BrootRedirectOptions.SectionName)
        .GetValue<int?>("SessionTimeoutDays") ?? 7;

    options.IdleTimeout = TimeSpan.FromDays(sessionTimeoutDays);
});


builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var adminPassword = builder.Configuration
    .GetSection(BrootRedirectOptions.SectionName)
    .GetValue<string>("AdminPassword") ?? "Password1";

if (adminPassword == "Password1")
{
    Console.WriteLine("WARNING: Using default password 'Password1'. Set SmartRedirect__AdminPassword environment variable.");
}

var qmProApiKey = builder.Configuration
    .GetSection(BrootRedirectOptions.SectionName)
    .GetValue<string>("QmProApiKey");

if (string.IsNullOrWhiteSpace(qmProApiKey))
{
    Console.WriteLine("WARNING: No QM Pro API key configured. All requests to /api/qm-pro are rejected. Set SmartRedirect__QmProApiKey environment variable.");
}

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

if (allowTeamsEmbedding)
{
    Console.WriteLine("Teams embedding enabled: session cookie is SameSite=None; Secure; Partitioned and framing is limited to Microsoft Teams.");

    app.Use(async (context, next) =>
    {
        TeamsEmbedding.ApplyFrameAncestors(context.Response.Headers);

        await next();
    });
}

var indexPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");

app.Use(async (context, next) =>
{
    await next();

    if (!context.Response.HasStarted
        && context.Response.StatusCode is 200 or 404
        && !context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/html";

        await context.Response.SendFileAsync(indexPath);
    }
});

app.UseStaticFiles();

app.UseRouting();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
    }

    await next();
});

app.UseMiddleware<RateLimitMiddleware>();

app.UseMiddleware<CsrfProtectionMiddleware>();

app.UseSession();

app.UseMiddleware<AdminSessionMiddleware>();

app.MapControllers();

app.UseMiddleware<RedirectMiddleware>();

app.Run();