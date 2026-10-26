using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Abstractions;
using Qwader.Application.Auth;
using Qwader.Application.Notifications;
using Qwader.Application.Settings;
using Qwader.Application.Users;
using Qwader.Domain.Identity;
using Qwader.Infrastructure.Identity;
using Qwader.Infrastructure.Persistence;
using Qwader.Infrastructure.Services;

namespace Qwader.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString, sql =>
        {
            sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
        }));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.User.AllowedUserNameCharacters = string.Empty; // allow Arabic usernames; validated in FluentValidation
                options.Password.RequiredLength = PasswordRules.MinLength;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.AddSingleton<JwtTokenService>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IDocumentNumberGenerator, DocumentNumberGenerator>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<ISystemResetService, SystemResetService>();
        services.AddScoped<ISystemBackupService, SystemBackupService>();
        services.AddSingleton<PushQueue>();
        services.AddSingleton<VapidKeys>();
        services.AddSingleton<IPushNotifier, PushNotifier>();
        services.AddSingleton<IPushSender, WebPushSender>();
        services.AddScoped<IPushSubscriptionService, PushSubscriptionService>();
        services.AddHostedService<PushDispatcher>();
        services.AddSingleton<IExcelService, ExcelService>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();

        return services;
    }
}
