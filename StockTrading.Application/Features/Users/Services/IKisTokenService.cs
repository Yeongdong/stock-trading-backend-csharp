using StockTrading.Application.Features.Auth.DTOs;

namespace StockTrading.Application.Features.Users.Services;

public interface IKisTokenService
{
    Task<TokenInfo> GetKisAccessTokenAsync(int userId, string appKey, string appSecret, string accountNumber);
    Task<string> GetKisWebSocketTokenAsync(int userId, string appKey, string appSecret);
    Task<TokenInfo> UpdateKisCredentialsAndTokensAsync(int userId, string appKey, string appSecret,
        string accountNumber);
}