using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StockTrading.Application.Features.Auth.DTOs;
using StockTrading.Domain.Entities;
using StockTrading.Domain.Enums;
using StockTrading.Infrastructure.Persistence.Contexts;
using StockTrading.Infrastructure.Persistence.Repositories;
using StockTrading.Infrastructure.Security.Encryption;

namespace StockTrading.Tests.Unit.Repositories
{
    public class TokenRepositoryTests
    {
        private readonly Mock<ILogger<TokenRepository>> _loggerMock;
        private readonly Mock<IEncryptionService> _mockEncryptionService;

        public TokenRepositoryTests()
        {
            _loggerMock = new Mock<ILogger<TokenRepository>>();
            _mockEncryptionService = new Mock<IEncryptionService>();
        }

        private ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options, _mockEncryptionService.Object);
        }

        [Fact]
        public async Task SaveKisToken_WithNoExistingToken_ShouldCreateNewToken()
        {
            using var context = CreateContext();
            var repository = new TokenRepository(context, _loggerMock.Object);

            var user = new User
            {
                Email = "test@example.com",
                Name = "Test User",
                GoogleId = "google123",
                Role = UserRole.User,
                CreatedAt = DateTime.UtcNow,
                KisToken = new KisToken
                {
                    AccessToken = "test_access_token",
                    ExpiresIn = DateTime.UtcNow.AddMinutes(5),
                    TokenType = "Bearer"
                }
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var userId = user.Id;
            var tokenResponse = new TokenInfo
            {
                AccessToken = "new_access_token",
                ExpiresIn = 86400,
                TokenType = "Bearer"
            };

            await repository.SaveKisTokenAsync(userId, tokenResponse);

            var savedToken = await context.KisTokens.FirstOrDefaultAsync(t => t.UserId == userId);
            Assert.NotNull(savedToken);
            Assert.Equal(tokenResponse.AccessToken, savedToken.AccessToken);
            Assert.Equal(tokenResponse.TokenType, savedToken.TokenType);
        }

        [Fact]
        public async Task SaveKisToken_WithExistingToken_ShouldUpdateToken()
        {
            using var context = CreateContext();
            var repository = new TokenRepository(context, _loggerMock.Object);

            var user = new User
            {
                Email = "test@example.com",
                Name = "Test User",
                GoogleId = "google123",
                Role = UserRole.User,
                CreatedAt = DateTime.UtcNow,
                KisToken = new KisToken
                {
                    AccessToken = "test_access_token",
                    ExpiresIn = DateTime.UtcNow.AddMinutes(5),
                    TokenType = "Bearer"
                }
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var userId = user.Id;

            // 기존 토큰 추가
            var existingToken = new KisToken
            {
                UserId = userId,
                AccessToken = "old_access_token",
                ExpiresIn = DateTime.UtcNow.AddDays(-1), // 만료된 토큰
                TokenType = "Bearer"
            };
            context.KisTokens.Add(existingToken);
            await context.SaveChangesAsync();

            // 새 토큰 응답 준비
            var tokenResponse = new TokenInfo
            {
                AccessToken = "updated_access_token",
                ExpiresIn = 86400, // 1일
                TokenType = "Bearer"
            };

            await repository.SaveKisTokenAsync(userId, tokenResponse);

            var updatedToken = await context.KisTokens.FirstOrDefaultAsync(t => t.UserId == userId);
            Assert.NotNull(updatedToken);
            Assert.Equal(tokenResponse.AccessToken, updatedToken.AccessToken);
            Assert.Equal(tokenResponse.TokenType, updatedToken.TokenType);

            // 토큰이 하나만 존재하는지 확인 (중복 생성 방지)
            var tokenCount = await context.KisTokens.CountAsync(t => t.UserId == userId);
            Assert.Equal(1, tokenCount);
        }
    }
}