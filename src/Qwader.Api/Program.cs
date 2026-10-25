using System.IdentityModel.Tokens.Jwt;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Qwader.Api.Authorization;
using Qwader.Api.Infrastructure;
using Qwader.Application;
using Qwader.Application.Abstractions;
using Qwader.Infrastructure;
using Qwader.Infrastructure.Identity;
using Qwader.Infrastructure.Persistence.Seed;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(e => string.IsNullOrEmpty(e.Key) ? "body" : e.Key.TrimStart('$', '.'),
                _ => new[] { "القيمة المدخلة غير صالحة" });
        return new ObjectResult(new ProblemDetails { Status = 400, Title = "البيانات المرسلة غير صالحة", Extensions = { ["errors"] = errors } })
        {
            StatusCode = 400,
            ContentTypes = { "application/problem+json" },
        };
    });

builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
// Configured lazily so the signing key is read from the final configuration (user-secrets, env, test overrides).
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<Microsoft.Extensions.Options.IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = jwt.GetKey(),
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.UniqueName,
            RoleClaimType = "role",
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await ProblemWriter.WriteAsync(context.HttpContext, 401, "انتهت الجلسة أو لم يتم تسجيل الدخول");
            },
            OnForbidden = context => ProblemWriter.WriteAsync(context.HttpContext, 403, "ليست لديك صلاحية لتنفيذ هذه العملية"),
        };
    });

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, PermissionPolicyProvider>();

var loginsPerMinute = builder.Configuration.GetValue("RateLimiting:LoginPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.OnRejected = (context, _) =>
        new ValueTask(ProblemWriter.WriteAsync(context.HttpContext, 429, "محاولات كثيرة، انتظر دقيقة ثم حاول مجددًا"));
    options.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Qwader API — معرض قويدر", Version = "v1" });
    options.CustomSchemaIds(t => t.FullName!.Replace("+", "."));
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSerilogRequestLogging(); // outermost, so it records the final status written by the exception handler
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "Qwader API");
}

app.UseDefaultFiles();
var contentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = ctx =>
    {
        // Hashed build assets never change; the service worker, manifest and index must always be revalidated
        // so an installed app picks up new versions.
        var path = ctx.Context.Request.Path.Value ?? "";
        ctx.Context.Response.Headers.CacheControl = path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    },
});

app.UseAuthentication();
app.UseRateLimiter();

// Until a user replaces a temporary password, only the auth endpoints are reachable.
app.Use(async (context, next) =>
{
    if (context.User.HasClaim(JwtTokenService.MustChangePasswordClaim, "true")
        && context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/api/auth"))
    {
        await ProblemWriter.WriteAsync(context, 403, "يجب تغيير كلمة المرور قبل متابعة استخدام النظام");
        return;
    }
    await next();
});

app.UseAuthorization();
app.MapControllers();

// The built React app is served from wwwroot; unknown non-API routes fall back to it.
app.MapFallbackToFile("index.html").AllowAnonymous();

if (!app.Configuration.GetValue<bool>("Database:SkipInitialization"))
    await DbInitializer.InitializeAsync(app.Services);

app.Run();

public partial class Program;
