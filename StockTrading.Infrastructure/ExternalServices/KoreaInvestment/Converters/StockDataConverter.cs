using Microsoft.Extensions.Logging;
using StockTrading.Application.DTOs.External.KoreaInvestment.Responses;
using StockTrading.Infrastructure.ExternalServices.KoreaInvestment.Constants;

namespace StockTrading.Infrastructure.ExternalServices.KoreaInvestment.Converters;

/// <summary>
/// 주식 데이터 변환기
/// </summary>
public class StockDataConverter
{
    private readonly ILogger<StockDataConverter> _logger;

    public StockDataConverter(ILogger<StockDataConverter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 필드 배열을 KisTransactionInfo로 변환
    /// </summary>
    public KisTransactionInfo? ConvertToTransactionInfo(string[] fields, int recordIndex)
    {
        var stockCode = fields[KisRealTimeConstants.FieldIndices.StockCode];

        if (!IsValidStockCode(stockCode))
        {
            _logger.LogDebug("레코드 {Index}: 유효하지 않은 종목코드: {StockCode}", recordIndex, stockCode);
            return null;
        }

        var priceData = CreateTransactionInfo(fields, stockCode);

        _logger.LogDebug("레코드 {Index}: 변환 성공 - 종목: {Symbol}, 현재가: {Price}원",
            recordIndex, stockCode, priceData.Price);

        return priceData;
    }

    private KisTransactionInfo CreateTransactionInfo(string[] fields, string stockCode)
    {
        var tradeTime = fields[KisRealTimeConstants.FieldIndices.TradeTime];
        var currentPrice = ParseDecimalSafely(fields[KisRealTimeConstants.FieldIndices.CurrentPrice]);
        var changeSign = fields[KisRealTimeConstants.FieldIndices.ChangeSign];
        var priceChange = ParseDecimalSafely(fields[KisRealTimeConstants.FieldIndices.PriceChange]);
        var changeRate = ParseDecimalSafely(fields[KisRealTimeConstants.FieldIndices.ChangeRate]);
        var openPrice = ParseDecimalSafely(fields[KisRealTimeConstants.FieldIndices.OpenPrice]);
        var highPrice = ParseDecimalSafely(fields[KisRealTimeConstants.FieldIndices.HighPrice]);
        var lowPrice = ParseDecimalSafely(fields[KisRealTimeConstants.FieldIndices.LowPrice]);
        var volume = ParseLongSafely(fields[KisRealTimeConstants.FieldIndices.Volume]);
        var totalVolume = ParseLongSafely(fields[KisRealTimeConstants.FieldIndices.TotalVolume]);

        return new KisTransactionInfo
        {
            Symbol = stockCode,
            Price = currentPrice,
            PriceChange = priceChange,
            ChangeType = ConvertChangeType(changeSign),
            TransactionTime = ParseTradeTime(tradeTime),
            ChangeRate = changeRate,
            Volume = (int)Math.Min(volume, int.MaxValue),
            TotalVolume = totalVolume,
            OpenPrice = openPrice,
            HighPrice = highPrice,
            LowPrice = lowPrice
        };
    }

    private static string ConvertChangeType(string changeSign)
    {
        if (KisRealTimeConstants.ChangeTypes.RiseCodes.Contains(changeSign))
            return KisRealTimeConstants.ChangeTypes.Rise;

        return KisRealTimeConstants.ChangeTypes.FallCodes.Contains(changeSign)
            ? KisRealTimeConstants.ChangeTypes.Fall
            : KisRealTimeConstants.ChangeTypes.Unchanged;
    }

    private static DateTime ParseTradeTime(string tradeTime)
    {
        if (tradeTime.Length != KisRealTimeConstants.Parsing.TradeTimeLength) return DateTime.Now;

        var hour = int.Parse(tradeTime[..2]);
        var minute = int.Parse(tradeTime.Substring(2, 2));
        var second = int.Parse(tradeTime.Substring(4, 2));

        return DateTime.Today
            .AddHours(hour)
            .AddMinutes(minute)
            .AddSeconds(second);
    }

    private static long ParseLongSafely(string value)
    {
        if (decimal.TryParse(value, out var decimalResult))
            return (long)decimalResult;

        return 0L;
    }

    public static int ParseIntSafely(string value)
    {
        return int.TryParse(value, out var result) ? result : 0;
    }

    public static decimal ParseDecimalSafely(string value)
    {
        return decimal.TryParse(value, out var result) ? result : 0m;
    }

    private static bool IsValidStockCode(string stockCode)
    {
        return !string.IsNullOrWhiteSpace(stockCode) &&
               stockCode.Length == KisRealTimeConstants.Parsing.StockCodeLength &&
               stockCode.All(char.IsDigit);
    }
}