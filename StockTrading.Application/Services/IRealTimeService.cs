using StockTrading.Application.DTOs.Users;

namespace StockTrading.Application.Services;

public interface IRealTimeService
{
    // 서비스 시작/중지
    Task StartAsync(UserInfo user);
    Task StopAsync();

    // 종목 구독 관리
    Task SubscribeSymbolAsync(string symbol);
    Task UnsubscribeSymbolAsync(string symbol);

    // 구독 중인 종목 목록 조회
    IReadOnlyCollection<string> GetSubscribedSymbols();
}