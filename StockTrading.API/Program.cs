using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StockTrading.DataAccess.Repositories;
using StockTrading.DataAccess.Services.Interfaces;
using StockTrading.Infrastructure.ExternalServices.Interfaces;
using StockTrading.Infrastructure.ExternalServices.KoreaInvestment;
using StockTrading.Infrastructure.Implementations;
using StockTrading.Infrastructure.Interfaces;
using StockTrading.Infrastructure.Repositories;
using StockTrading.Infrastructure.Security.Encryption;
using StockTrading.Infrastructure.Security.Options;
using StockTradingBackend.DataAccess.Settings;
using stock_trading_backend.Validator.Implementations;
using stock_trading_backend.Validator.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// 로깅 설정 개선
ConfigureLogging(builder);

// 1. 기본 서비스 등록
ConfigureBasicServices(builder.Services);

// 2. 데이터베이스 설정
ConfigureDatabase(builder.Services, builder.Configuration);

// 3. 보안 서비스 설정
ConfigureSecurity(builder.Services, builder.Configuration);

// 4. 인증 설정
ConfigureAuthentication(builder.Services, builder.Configuration);

// 5. 외부 API 클라이언트 설정
ConfigureHttpClients(builder.Services, builder.Configuration);

// 6. 비즈니스 서비스 등록
ConfigureBusinessServices(builder.Services);

// 7. 실시간 서비스 등록
ConfigureRealTimeServices(builder.Services);

// 8. CORS 설정
ConfigureCors(builder.Services, builder.Configuration);

// 9. CSRF 보호 설정 (환경별 분기)
ConfigureAntiforgery(builder.Services, builder.Environment);

// 10. 헬스체크 추가
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>();

var app = builder.Build();

// 미들웨어 파이프라인 구성
ConfigureMiddleware(app);

app.Run();

#region 서비스 설정 메서드들

static void ConfigureLogging(WebApplicationBuilder builder)
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();

    if (builder.Environment.IsDevelopment())
    {
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
    }
    else
    {
        builder.Logging.SetMinimumLevel(LogLevel.Information);
    }
}

static void ConfigureBasicServices(IServiceCollection services)
{
    services.AddEndpointsApiExplorer();
    services.AddSwaggerGen();
    services.AddControllers();
    services.AddHttpContextAccessor();
    services.AddSignalR();
    services.AddMemoryCache();
}

static void ConfigureDatabase(IServiceCollection services, IConfiguration configuration)
{
    services.AddDbContext<ApplicationDbContext>(options =>
    {
        options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));

        // 개발 환경에서만 민감한 데이터 로깅 활성화
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
        {
            options.EnableSensitiveDataLogging();
        }
    });

    services.AddScoped<ApplicationDbContext>(provider =>
    {
        var options = provider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        var encryptionService = provider.GetRequiredService<IEncryptionService>();
        return new ApplicationDbContext(options, encryptionService);
    });
}

static void ConfigureSecurity(IServiceCollection services, IConfiguration configuration)
{
    // JWT 설정 등록
    services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));

    // 암호화 서비스 등록
    services.Configure<EncryptionOptions>(configuration.GetSection("Encryption"));
    services.AddSingleton<IEncryptionService, AesEncryptionService>();
}

static void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration)
{
    var jwtSettings = configuration.GetSection("JwtSettings");
    var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]);

    services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings["Issuer"],
                ValidAudience = jwtSettings["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // 쿠키에서 토큰 읽기
                    context.Token = context.Request.Cookies["auth_token"];
                    return Task.CompletedTask;
                },
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogWarning("JWT 인증 실패: {Error}", context.Exception.Message);
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    var email = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
                    logger.LogDebug("JWT 토큰 검증 성공: {Email}", email);
                    return Task.CompletedTask;
                },
                OnChallenge = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogWarning("JWT 인증 요구됨: {Path}", context.Request.Path);
                    return Task.CompletedTask;
                }
            };
        })
        .AddGoogle("Google", options =>
        {
            options.ClientId = configuration["Authentication:Google:ClientId"];
            options.ClientSecret = configuration["Authentication:Google:ClientSecret"];
            options.CallbackPath = "/api/auth/oauth2/callback/google";
        });
}

static void ConfigureHttpClients(IServiceCollection services, IConfiguration configuration)
{
    var kisBaseUrl = configuration["KoreaInvestment:BaseUrl"];

    // 기본 HttpClient 추가
    services.AddHttpClient();

    // KIS API 클라이언트
    services.AddHttpClient(nameof(KisApiClient), client =>
    {
        client.BaseAddress = new Uri(kisBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("User-Agent", "StockTradingApp/1.0");
    });

    services.AddHttpClient(nameof(KisTokenService), client =>
    {
        client.BaseAddress = new Uri(kisBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("User-Agent", "StockTradingApp/1.0");
    });

    // 타입 기반 등록 유지
    services.AddHttpClient<KisApiClient>();
    services.AddHttpClient<KisService>();
}

static void ConfigureBusinessServices(IServiceCollection services)
{
    // Repository 계층
    services.AddScoped<IUserRepository, UserRepository>();
    services.AddScoped<IOrderRepository, OrderRepository>();
    services.AddScoped<IKisTokenRepository, KisTokenRepository>();
    services.AddScoped<IUserKisInfoRepository, UserKisInfoRepository>();

    // Application 서비스 계층
    services.AddScoped<IJwtService, JwtService>();
    services.AddScoped<IUserService, UserService>();
    services.AddScoped<IGoogleAuthProvider, GoogleAuthProvider>();
    services.AddScoped<IKisService, KisService>();
    services.AddScoped<IKisTokenService, KisTokenService>();

    // Infrastructure 계층
    services.AddScoped<IKisApiClient, KisApiClient>();
    services.AddScoped<IDbContextWrapper, DbContextWrapper>();

    // Validator 계층
    services.AddScoped<IGoogleAuthValidator, GoogleAuthValidator>();
}

static void ConfigureRealTimeServices(IServiceCollection services)
{
    // 실시간 서비스
    services.AddSingleton<KisWebSocketClient>();
    services.AddSingleton<KisRealTimeDataProcessor>();
    services.AddSingleton<RealTimeDataBroadcaster>();
    services.AddSingleton<KisSubscriptionManager>();

    // 실시간 서비스는 사용자별 격리를 위해 Scoped
    services.AddScoped<IKisRealTimeService, KisRealTimeService>();
}

static void ConfigureCors(IServiceCollection services, IConfiguration configuration)
{
    var frontendUrl = configuration["Frontend:Url"] ?? "http://localhost:3000";

    services.AddCors(options =>
    {
        options.AddPolicy("AllowReactApp", builder =>
        {
            builder.WithOrigins(frontendUrl)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials()
                .SetIsOriginAllowedToAllowWildcardSubdomains();
        });

        // 개발 환경용 정책
        options.AddPolicy("Development", builder =>
        {
            builder.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        });
    });
}

static void ConfigureAntiforgery(IServiceCollection services, IWebHostEnvironment environment)
{
    // 개발 환경에서는 CSRF 보호 완화
    if (environment.IsDevelopment())
    {
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "XSRF-TOKEN";
            options.Cookie.HttpOnly = false;
            options.Cookie.SecurePolicy = CookieSecurePolicy.None; // 개발 환경
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.SameSite = SameSiteMode.Lax;
        });
    }
    else
    {
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "XSRF-TOKEN";
            options.Cookie.HttpOnly = false;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // 운영 환경
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
    }

    // 컨트롤러에 자동 검증 필터
    services.AddMvc(options => { options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()); });
}

static void ConfigureMiddleware(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    // 1. 전역 예외 처리 (가장 먼저)
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                var feature = context.Features.Get<IExceptionHandlerFeature>();

                if (feature?.Error != null)
                {
                    logger.LogError(feature.Error, "전역 예외 발생: {Message}", feature.Error.Message);

                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "application/json";

                    var response = new { error = "내부 서버 오류가 발생했습니다." };
                    await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
                }
            });
        });
    }

    // 2. 개발 환경별 설정
    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Stock Trading API V1");
            c.RoutePrefix = "swagger";
        });

        // 개발 환경 요청 로깅
        app.Use(async (context, next) =>
        {
            var startTime = DateTime.UtcNow;
            await next();
            var duration = DateTime.UtcNow - startTime;

            logger.LogDebug("HTTP {Method} {Path} - {StatusCode} ({Duration}ms)",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                duration.TotalMilliseconds);
        });

        logger.LogInformation("개발 환경: Swagger UI 및 상세 로깅 활성화됨");
    }
    else
    {
        // 운영 환경 보안 설정
        app.UseHsts();
        app.Use(async (context, next) =>
        {
            // 보안 헤더 추가
            context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Add("X-Frame-Options", "DENY");
            context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
            context.Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
            context.Response.Headers.Add("Content-Security-Policy", "default-src 'self'");

            await next();
        });
        logger.LogInformation("운영 환경: 보안 헤더 적용됨");
    }

    // 3. CORS (인증 전에 위치해야 함)
    if (app.Environment.IsDevelopment())
    {
        app.UseCors("Development");
    }
    else
    {
        app.UseCors("AllowReactApp");
    }

    // 4. HTTPS 리다이렉션
    app.UseHttpsRedirection();

    // 5. 라우팅
    app.UseRouting();

    // 6. 인증 (Authorization 전에 위치)
    app.UseAuthentication();

    // 7. 인가
    app.UseAuthorization();

    // 8. 엔드포인트 매핑
    app.MapControllers();
    app.MapHub<StockHub>("/stockhub");

    // 9. 헬스체크 엔드포인트
    app.MapHealthChecks("/health");

    // 10. 기본 상태 확인 엔드포인트
    app.MapGet("/", () => Results.Ok(new
    {
        message = "Stock Trading API is running",
        version = "1.0.0",
        timestamp = DateTime.UtcNow,
        environment = app.Environment.EnvironmentName
    }));

    logger.LogInformation("미들웨어 파이프라인 구성 완료");
}

#endregion

public partial class Program
{
}