using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockTrading.Application.DTOs.External.KoreaInvestment.Requests;
using StockTrading.Application.DTOs.External.KoreaInvestment.Responses;
using StockTrading.Application.ExternalServices;
using StockTrading.Application.Features.Trading.DTOs.Inquiry;
using StockTrading.Application.Features.Trading.DTOs.Portfolio;
using StockTrading.Application.Features.Users.DTOs;
using StockTrading.Domain.Settings.ExternalServices;
using StockTrading.Infrastructure.ExternalServices.KoreaInvestment.Common;
using StockTrading.Infrastructure.ExternalServices.KoreaInvestment.Common.Helpers;
using StockTrading.Infrastructure.ExternalServices.KoreaInvestment.RealTime.Converters;

namespace StockTrading.Infrastructure.ExternalServices.KoreaInvestment.Trading;

public class KisBalanceApiClient : KisApiClientBase, IKisBalanceApiClient
{
    private readonly StockDataConverter _stockDataConverter;

    public KisBalanceApiClient(HttpClient httpClient, StockDataConverter stockDataConverter,
        IOptions<KoreaInvestmentSettings> settings, ILogger<KisBalanceApiClient> logger) : base(httpClient, settings,
        logger)
    {
        _stockDataConverter = stockDataConverter;
    }

    #region 국내 주식

    public async Task<AccountBalance> GetStockBalanceAsync(UserInfo user)
    {
        var queryParams = CreateBalanceQueryParams(user);
        var httpRequest = CreateBalanceHttpRequest(queryParams, user);

        var response = await _httpClient.SendAsync(httpRequest);
        var kisResponse = await response.Content.ReadFromJsonAsync<KisBalanceResponse>();

        ValidateBalanceResponse(kisResponse);

        return CreateAccountBalance(kisResponse);
    }

    public async Task<BuyableInquiryResponse> GetBuyableInquiryAsync(BuyableInquiryRequest request, UserInfo user)
    {
        var queryParams = CreateBuyableInquiryQueryParams(request, user);
        var httpRequest = CreateBuyableInquiryHttpRequest(queryParams, user);

        var response = await _httpClient.SendAsync(httpRequest);
        var kisResponse = await response.Content.ReadFromJsonAsync<KisBuyableInquiryResponse>();

        ValidateBuyableInquiryResponse(kisResponse);

        return _stockDataConverter.ConvertToBuyableInquiryResponse(kisResponse.Output, request.OrderPrice,
            request.StockCode);
    }

    #endregion

    #region 해외 주식

    public async Task<OverseasAccountBalance> GetOverseasStockBalanceAsync(UserInfo user)
    {
        var allPositions = new List<KisOverseasBalanceData>();
        OverseasDepositInfo? depositInfo = null;

        var exchangeCurrencyPairs = new[]
        {
            ("NASD", "USD"), // 나스닥
            ("NYSE", "USD"), // 뉴욕증권거래소  
            ("AMEX", "USD"), // 아멕스
            ("SEHK", "HKD"), // 홍콩
            ("TKSE", "JPY"), // 일본
        };

        foreach (var (exchangeCode, currencyCode) in exchangeCurrencyPairs)
        {
            var queryParams = CreateOverseasBalanceQueryParams(user, exchangeCode, currencyCode);
            var httpRequest = CreateOverseasBalanceHttpRequest(queryParams, user);

            var response = await _httpClient.SendAsync(httpRequest);
            var kisResponse = await response.Content.ReadFromJsonAsync<KisOverseasBalanceResponse>();

            if (kisResponse?.IsSuccess != true) continue;

            if (kisResponse.HasPositions)
            {
                var validPositions = kisResponse.Positions
                    .Where(p => !string.IsNullOrEmpty(p.StockCode) && !string.IsNullOrEmpty(p.StockName))
                    .ToList();

                allPositions.AddRange(validPositions);
            }

            // 예수금 정보는 첫 번째 성공한 응답에서만 수집 
            depositInfo ??= ConvertToDepositInfo(kisResponse.DepositData, currencyCode);
        }

        return new OverseasAccountBalance
        {
            Positions = allPositions,
            DepositInfo = depositInfo ?? new OverseasDepositInfo()
        };
    }

    public async Task<KisOverseasStockSearchResponse> SearchOverseasStocksAsync(KisOverseasStockSearchRequest request,
        UserInfo user)
    {
        KisValidationHelper.ValidateUserForKisApi(user);

        var queryParams = new Dictionary<string, string>
        {
            ["AUTH"] = request.AUTH,
            ["EXCD"] = request.EXCD,
            ["KEYB"] = request.KEYB,
            // 모든 조건 파라미터를 빈 문자열로 설정 (전체 조회)
            ["CO_YN_PRICECUR"] = "",
            ["CO_ST_PRICECUR"] = "",
            ["CO_EN_PRICECUR"] = "",
            ["CO_YN_RATE"] = "",
            ["CO_ST_RATE"] = "",
            ["CO_EN_RATE"] = "",
            ["CO_YN_VALX"] = "",
            ["CO_ST_VALX"] = "",
            ["CO_EN_VALX"] = "",
            ["CO_YN_SHAR"] = "",
            ["CO_ST_SHAR"] = "",
            ["CO_EN_SHAR"] = "",
            ["CO_YN_VOLUME"] = "",
            ["CO_ST_VOLUME"] = "",
            ["CO_EN_VOLUME"] = "",
            ["CO_YN_AMT"] = "",
            ["CO_ST_AMT"] = "",
            ["CO_EN_AMT"] = "",
            ["CO_YN_EPS"] = "",
            ["CO_ST_EPS"] = "",
            ["CO_EN_EPS"] = "",
            ["CO_YN_PER"] = "",
            ["CO_ST_PER"] = "",
            ["CO_EN_PER"] = ""
        };

        var url = BuildGetUrl(_settings.Endpoints.OverseasStockSearchPath, queryParams);
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);

        SetStandardHeaders(httpRequest, _settings.DefaultValues.OverseasStockSearchTransactionId, user);

        _logger.LogInformation("해외주식 조건검색 API 호출: 거래소={ExchangeCode}", request.EXCD);

        var response = await _httpClient.SendAsync(httpRequest);
        var responseContent = await ValidateAndReadResponse(response);

        var result = JsonSerializer.Deserialize<KisOverseasStockSearchResponse>(responseContent);

        if (result == null)
            throw new Exception("해외주식 조건검색 응답을 파싱할 수 없습니다.");

        if (result.rt_cd != "0")
        {
            _logger.LogWarning("해외주식 조건검색 실패: {ErrorCode} - {ErrorMessage}", result.msg_cd, result.msg1);
            throw new Exception($"해외주식 조건검색 실패: {result.msg1}");
        }

        _logger.LogInformation("해외주식 조건검색 성공: {Count}개 종목 조회", result.output2?.Count ?? 0);
        return result;
    }

    #endregion

    #region 국내 주식 Private Methods

    private Dictionary<string, string> CreateBalanceQueryParams(UserInfo user)
    {
        var defaults = _settings.DefaultValues;
        return new Dictionary<string, string>
        {
            ["CANO"] = user.AccountNumber,
            ["ACNT_PRDT_CD"] = defaults.AccountProductCode,
            ["AFHR_FLPR_YN"] = defaults.AfterHoursForeignPrice,
            ["OFL_YN"] = defaults.OfflineYn,
            ["INQR_DVSN"] = defaults.InquiryDivision,
            ["UNPR_DVSN"] = defaults.UnitPriceDivision,
            ["FUND_STTL_ICLD_YN"] = defaults.FundSettlementInclude,
            ["FNCG_AMT_AUTO_RDPT_YN"] = defaults.FinancingAmountAutoRedemption,
            ["PRCS_DVSN"] = defaults.ProcessDivision,
            ["CTX_AREA_FK100"] = "",
            ["CTX_AREA_NK100"] = ""
        };
    }

    private HttpRequestMessage CreateBalanceHttpRequest(Dictionary<string, string> queryParams, UserInfo user)
    {
        var url = BuildGetUrl(_settings.Endpoints.DomesticBalancePath, queryParams);
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);

        SetStandardHeaders(httpRequest, _settings.DefaultValues.DomesticBalanceTransactionId, user);
        return httpRequest;
    }

    private static void ValidateBalanceResponse(KisBalanceResponse? kisResponse)
    {
        if (!kisResponse.IsSuccess)
            throw new Exception($"잔고조회 실패: {kisResponse.Message}");

        if (!kisResponse.HasData)
            throw new Exception("잔고조회 데이터가 없습니다.");
    }

    private static AccountBalance CreateAccountBalance(KisBalanceResponse? kisResponse)
    {
        return new AccountBalance
        {
            Positions = kisResponse.Positions,
            Summary = kisResponse.Summary.FirstOrDefault() ?? new KisAccountSummaryResponse()
        };
    }

    private Dictionary<string, string> CreateBuyableInquiryQueryParams(BuyableInquiryRequest request, UserInfo user)
    {
        var defaults = _settings.DefaultValues;
        return new Dictionary<string, string>
        {
            ["CANO"] = user.AccountNumber,
            ["ACNT_PRDT_CD"] = defaults.AccountProductCode,
            ["PDNO"] = request.StockCode,
            ["ORD_UNPR"] = request.OrderPrice.ToString("F0"),
            ["ORD_DVSN"] = request.OrderType,
            ["CMA_EVLU_AMT_ICLD_YN"] = "Y",
            ["OVRS_ICLD_YN"] = "N"
        };
    }

    private HttpRequestMessage CreateBuyableInquiryHttpRequest(Dictionary<string, string> queryParams, UserInfo user)
    {
        var url = BuildGetUrl(_settings.Endpoints.DomesticBuyableInquiryPath, queryParams);
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);

        SetStandardHeaders(httpRequest, _settings.DefaultValues.DomesticBuyableInquiryTransactionId, user);
        return httpRequest;
    }

    private void ValidateBuyableInquiryResponse(KisBuyableInquiryResponse? kisResponse)
    {
        if (!kisResponse.IsSuccess)
            throw new Exception($"매수가능조회 실패: {kisResponse.Message}");

        if (!kisResponse.HasData)
            throw new Exception("매수가능조회 데이터가 없습니다.");
    }

    #endregion

    #region 해외 주식 Private Methods

    private Dictionary<string, string> CreateOverseasBalanceQueryParams(UserInfo user, string exchangeCode,
        string currencyCode)
    {
        var defaults = _settings.DefaultValues;
        return new Dictionary<string, string>
        {
            ["CANO"] = user.AccountNumber,
            ["ACNT_PRDT_CD"] = defaults.AccountProductCode,
            ["OVRS_EXCG_CD"] = exchangeCode, // 해외거래소코드 (필수)
            ["TR_CRCY_CD"] = currencyCode, // 거래통화코드 (필수)
            ["CTX_AREA_FK200"] = "", // 연속조회검색조건200 (첫 조회시 공백)
            ["CTX_AREA_NK200"] = "" // 연속조회키200 (첫 조회시 공백)
        };
    }

    private HttpRequestMessage CreateOverseasBalanceHttpRequest(Dictionary<string, string> queryParams, UserInfo user)
    {
        var url = BuildGetUrl(_settings.Endpoints.OverseasBalancePath, queryParams);
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);

        SetStandardHeaders(httpRequest, _settings.DefaultValues.OverseasBalanceTransactionId, user);
        return httpRequest;
    }

    private static OverseasDepositInfo ConvertToDepositInfo(KisOverseasDepositData? data, string currencyCode)
    {
        if (data == null) return new OverseasDepositInfo { CurrencyCode = currencyCode };

        return new OverseasDepositInfo
        {
            TotalDepositAmount = decimal.TryParse(data.DepositAmount, out var deposit) ? deposit : 0,
            OrderableAmount = decimal.TryParse(data.OrderableAmount, out var orderable) ? orderable : 0,
            CurrencyCode = currencyCode,
            ExchangeRate = decimal.TryParse(data.ExchangeRate, out var rate) ? rate : 0,
            InquiryTime = DateTime.Now
        };
    }

    #endregion
}