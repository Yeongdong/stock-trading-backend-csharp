using StackExchange.Redis;
using StockTrading.Infrastructure.Configuration;
using StockTrading.Infrastructure.Configuration.ServiceConfiguration;
using StockTrading.API.Services;
using StockTrading.API.Validator.Implementations;
using StockTrading.API.Validator.Interfaces;
using StockTrading.Application.Common.Interfaces;
using StockTrading.Application.Features.Auth.Services;
using StockTrading.Application.Features.Market.Repositories;
using StockTrading.Application.Features.Market.Services;
using StockTrading.Application.Features.Trading.Repositories;
using StockTrading.Application.Features.Trading.Services;
using StockTrading.Application.Features.Users.Repositories;
using StockTrading.Application.Features.Users.Services;
using StockTrading.Domain.Settings.Infrastructure;
using StockTrading.Infrastructure.Cache;
using StockTrading.Infrastructure.ExternalServices.KoreaInvestment;
using StockTrading.Infrastructure.ExternalServices.KoreaInvestment.Auth;
using StockTrading.Infrastructure.ExternalServices.KoreaInvestment.Market.Converters;
using StockTrading.Infrastructure.Persistence.Repositories;
using StockTrading.Infrastructure.Services.Auth;
using StockTrading.Infrastructure.Services.Trading;
using StockTrading.Infrastructure.Services.Market;
using StockTrading.Infrastructure.Services.Common;
using StockTrading.Infrastructure.ExternalServices.KoreaInvestment.RealTime;
using StockTrading.Infrastructure.ExternalServices.KoreaInvestment.RealTime.Converters;
using StockTrading.Infrastructure.ExternalServices.KoreaInvestment.Trading.Converters;

namespace StockTrading.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        // 기본 서비스들
        services.AddBasicServices();

        // 설정 등록
        services.AddApplicationSettings(configuration);

        // Infrastructure 서비스들
        services.AddDatabaseServices(configuration);
        services.AddAuthenticationServices(configuration);
        services.AddCacheServices(configuration);
        services.AddExternalServices(configuration);

        // Business 서비스들
        services.AddBusinessServices();
        services.AddRealTimeServices();

        // CORS 및 기타
        services.AddCorsServices(configuration);
        services.AddHealthCheckServices();

        return services;
    }

    private static IServiceCollection AddBasicServices(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.AddControllers();
        services.AddHttpContextAccessor();
        services.AddSignalR();
        services.AddMemoryCache();

        return services;
    }

    private static IServiceCollection AddBusinessServices(this IServiceCollection services)
    {
        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ITokenRepository, TokenRepository>();
        services.AddScoped<IUserKisInfoRepository, UserKisInfoRepository>();
        services.AddScoped<IStockRepository, StockRepository>();

        // Application Services
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IBalanceService, BalanceService>();
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<IKisTokenService, KisTokenService>();
        services.AddScoped<IOrderExecutionInquiryService, OrderExecutionInquiryService>();
        services.AddScoped<IBuyableInquiryService, BuyableInquiryService>();
        services.AddScoped<ICurrentPriceService, CurrentPriceService>();
        services.AddScoped<IPeriodPriceService, PeriodPriceService>();
        services.AddScoped<IStockCacheService, StockCacheService>();
        services.AddScoped<IKisTokenRefreshService, KisTokenRefreshService>();
        services.AddScoped<ICookieService, CookieService>();

        // Infrastructure Services
        services.AddScoped<IDbContextWrapper, DbContextWrapper>();

        // API Services
        services.AddScoped<IUserContextService, UserContextService>();

        // Validators
        services.AddScoped<IGoogleAuthValidator, GoogleAuthValidator>();

        // Converters
        services.AddSingleton<StockDataConverter>();
        services.AddSingleton<PriceDataConverter>();
        services.AddSingleton<OrderDataConverter>();

        return services;
    }

    private static IServiceCollection AddRealTimeServices(this IServiceCollection services)
    {
        // WebSocket 클라이언트
        services.AddSingleton<WebSocketClient>();
        services.AddSingleton<IWebSocketClient>(provider =>
            provider.GetRequiredService<WebSocketClient>());

        // 실시간 데이터 처리
        services.AddSingleton<RealTimeDataProcessor>();
        services.AddSingleton<IRealTimeDataProcessor>(provider =>
            provider.GetRequiredService<RealTimeDataProcessor>());

        // 구독 관리
        services.AddSingleton<SubscriptionManager>();
        services.AddSingleton<ISubscriptionManager>(provider =>
            provider.GetRequiredService<SubscriptionManager>());

        // 데이터 브로드캐스터
        services.AddSingleton<RealTimeDataBroadcaster>();
        services.AddSingleton<IRealTimeDataBroadcaster>(provider =>
            provider.GetRequiredService<RealTimeDataBroadcaster>());

        // 실시간 서비스
        services.AddSingleton<IRealTimeService, RealTimeService>();

        return services;
    }


    private static IServiceCollection AddCorsServices(this IServiceCollection services, IConfiguration configuration)
    {
        var frontendUrl = configuration["Application:Frontend:Url"] ?? "http://localhost:3000";

        services.AddCors(options =>
        {
            options.AddPolicy("AllowReactApp", builder =>
            {
                builder.WithOrigins(frontendUrl)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });

            options.AddPolicy("Development", builder =>
            {
                builder.WithOrigins(frontendUrl, "http://localhost:3000", "https://localhost:3000")
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials()
                    .SetIsOriginAllowed(_ => true);
            });
        });

        return services;
    }

    private static IServiceCollection AddCacheServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CacheSettings>(configuration.GetSection(CacheSettings.SectionName));
        services.Configure<RedisSettings>(configuration.GetSection(RedisSettings.SectionName));

        var redisSettings = configuration.GetSection(RedisSettings.SectionName).Get<RedisSettings>();

        // Redis 설정이 활성화되어 있으면 Redis 사용, 아니면 메모리 캐시
        if (redisSettings?.Enabled == true && !string.IsNullOrEmpty(redisSettings.ConnectionString))
        {
            // Redis ConnectionMultiplexer 등록
            services.AddSingleton<IConnectionMultiplexer>(provider =>
            {
                var config = ConfigurationOptions.Parse(redisSettings.ConnectionString);
                config.ConnectTimeout = redisSettings.ConnectTimeoutSeconds * 1000;
                config.SyncTimeout = redisSettings.SyncTimeoutMs;
                config.ConnectRetry = redisSettings.RetryCount;
                config.AbortOnConnectFail = false;

                return ConnectionMultiplexer.Connect(config);
            });

            // Redis 분산 캐시 등록
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisSettings.ConnectionString;
                options.InstanceName = redisSettings.InstanceName;
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
            services.AddSingleton<IConnectionMultiplexer?>(provider => null);
        }

        services.AddSingleton<CacheTtl>();
        services.AddSingleton<CacheMetrics>();
        services.AddScoped<IStockCacheService, StockCacheService>();

        return services;
    }

    private static IServiceCollection AddHealthCheckServices(this IServiceCollection services)
    {
        services.AddHealthChecks();
        return services;
    }
}