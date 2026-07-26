using FamilyPortal.Application.Interfaces;
using FamilyPortal.Infrastructure.Calendar;
using FamilyPortal.Infrastructure.Hubs;
using FamilyPortal.Infrastructure.Media;
using FamilyPortal.Infrastructure.MongoDB;
using FamilyPortal.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyPortal.Infrastructure;

/// <summary>
/// Infrastructure 層 DI 註冊擴充方法
/// 將所有基礎設施服務集中註冊，保持 Program.cs 簡潔
/// </summary>
public static class DependencyInjection
{
    /// <summary>註冊資料存取、行事曆與安全性基礎設施服務。</summary>
    /// <param name="services">服務集合。</param>
    /// <param name="configuration">應用程式設定。</param>
    /// <returns>相同的服務集合，供連鎖註冊使用。</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // MongoDB 設定與連線
        services.Configure<MongoDbSettings>(
            configuration.GetSection("MongoDbSettings"));
        services.AddSingleton<MongoDbContext>();
        services.AddSingleton<MongoDbIndexInitializer>();

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IItineraryRepository, ItineraryRepository>();
        services.AddScoped<IMediaRepository, MediaRepository>();
        services.AddSingleton<IImageInspector, SkiaImageInspector>();
        services.AddScoped<ICalendarNotifier, SignalRCalendarNotifier>();
        services.AddScoped<ILunarRecurrenceService, LunarRecurrenceService>();

        // Security Services
        services.Configure<JwtSettings>(
            configuration.GetSection("JwtSettings"));
        services.AddScoped<IPasswordHasher, Argon2PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();

        return services;
    }
}
