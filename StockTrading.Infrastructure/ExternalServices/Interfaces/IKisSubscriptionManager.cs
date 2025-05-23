namespace StockTrading.Infrastructure.ExternalServices.Interfaces;

public interface IKisSubscriptionManager
{
    Task SubscribeSymbolAsync(string symbol);
    Task UnsubscribeSymbolAsync(string symbol);
    Task UnsubscribeAllAsync();
    IReadOnlyCollection<string> GetSubscribedSymbols();
}