using FamilyPortal.Application.Interfaces;
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

        // Security Services
        services.Configure<JwtSettings>(
            configuration.GetSection("JwtSettings"));
        services.AddScoped<IPasswordHasher, Argon2PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();

        return services;
    }
}
