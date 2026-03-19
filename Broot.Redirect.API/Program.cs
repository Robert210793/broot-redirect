using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Middleware;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Services;
using Broot.Redirect.Infrastructure.Extensions;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BrootRedirectOptions>(
    builder.Configuration.GetSection(BrootRedirectOptions.SectionName));

builder.Services.AddSmartRedirectInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IRuleMatchingService, RuleMatchingService>();
builder.Services.AddSingleton<IUrlTransformService, UrlTransformService>();
builder.Services.AddSingleton<ISmartSearchService, SmartSearchService>();

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.Cookie.Name = "admin_session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
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

// ---------------------------------------------------------------------------
// SPA fallback wrapper
// ---------------------------------------------------------------------------
// Placed BEFORE UseStaticFiles so it wraps the entire downstream pipeline.
// After await next() returns, every other middleware (static files, routing,
// controllers, RedirectMiddleware) has already had its chance.
//
// Conditions to serve index.html:
//   1. No middleware wrote a response body  (HasStarted == false)
//   2. No middleware changed the status code (still the default 200)
//      -- RedirectMiddleware sets 301/302, controllers set 4xx/5xx, etc.
//   3. The path is not under /api (safety net for controller 404s
//      that return StatusCodeResult without a body)
// ---------------------------------------------------------------------------
var indexPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");

app.Use(async (context, next) =>
{
    await next();

    if (!context.Response.HasStarted
        && context.Response.StatusCode == 200
        && !context.Request.Path.StartsWithSegments("/api"))
    {
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

app.UseSession();

app.UseMiddleware<AdminSessionMiddleware>();

app.MapControllers();

app.UseMiddleware<RedirectMiddleware>();

app.Run();