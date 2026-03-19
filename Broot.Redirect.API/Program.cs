using Broot.Redirect.API.Configuration;
using Broot.Redirect.API.Middleware;
using Broot.Redirect.API.Services;
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
builder.Services.AddSingleton<BruteForceProtectionService>();

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

// Rate limiting: reject floods before doing any real work
app.UseMiddleware<RateLimitMiddleware>();

// CSRF: reject cross-origin state-changing requests before session processing
app.UseMiddleware<CsrfProtectionMiddleware>();

app.UseSession();

app.UseMiddleware<AdminSessionMiddleware>();

app.MapControllers();

app.UseMiddleware<RedirectMiddleware>();

app.Run();