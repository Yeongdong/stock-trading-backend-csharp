using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StockTrading.Application.DTOs.External.KoreaInvestment.Responses;
using StockTrading.Domain.Settings.ExternalServices;
using StockTrading.Infrastructure.ExternalServices.KoreaInvestment.RealTime;
using StockTrading.Infrastructure.ExternalServices.KoreaInvestment.RealTime.Converters;
using StockTrading.Infrastructure.ExternalServices.KoreaInvestment.RealTime.Parsers;

namespace StockTrading.Tests.Unit.ExternalServices.KoreaInvestment
{
    public class RealTimeDataProcessorTest
    {
        private readonly Mock<ILogger<RealTimeDataProcessor>> _mockLogger;
        private readonly Mock<ILoggerFactory> _mockLoggerFactory;
        private readonly Mock<StockDataConverter> _mockStockDataConverter;
        private readonly RealTimeDataProcessor _processor;

        public RealTimeDataProcessorTest()
        {
            _mockLogger = new Mock<ILogger<RealTimeDataProcessor>>();
            _mockLoggerFactory = new Mock<ILoggerFactory>();
            _mockStockDataConverter = new Mock<StockDataConverter>(
                Mock.Of<ILogger<StockDataConverter>>()
            );

            var messageTypes = new MessageTypes
            {
                StockExecution = "H0STASP0",
                TradeNotification = "H0STCNI0",
                TradeNotificationDemo = "H0STCNI9",
                StockAskBid = "H0ASKBID0",
                PingPong = "PINGPONG"
            };

            var options = Options.Create(new RealTimeDataSettings
            {
                MessageTypes = messageTypes
            });

            _mockLoggerFactory.Setup(x => x.CreateLogger<JsonMessageParser>())
                .Returns(Mock.Of<ILogger<JsonMessageParser>>());
            _mockLoggerFactory.Setup(x => x.CreateLogger<PipeDelimitedMessageParser>())
                .Returns(Mock.Of<ILogger<PipeDelimitedMessageParser>>());
            _mockLoggerFactory.Setup(x => x.CreateLogger<StockDataParser>())
                .Returns(Mock.Of<ILogger<StockDataParser>>());

            _processor = new RealTimeDataProcessor(
                _mockLogger.Object,
                _mockLoggerFactory.Object,
                options,
                _mockStockDataConverter.Object
            );
        }

        [Fact]
        public void ProcessMessage_StockPriceReceived_EventRaised_ForH0STASP0Message()
        {
            bool eventRaised = false;
            KisTransactionInfo receivedData = null;

            // 실시간 호가 메시지
            var hokaMessage = @"{
                ""header"": {
                    ""tr_id"": ""H0STASP0"",
                    ""msg_cd"": ""0"",
                    ""msg_tx"": """"
                },
                ""body"": {
                    ""mksc_shrn_iscd"": ""005930"",
                    ""stck_prpr"": ""76900"",
                    ""prdy_vrss"": ""1300"",
                    ""prdy_ctrt"": ""1.72"",
                    ""acml_vol"": ""7388353"",
                    ""ask_price1"": ""77000"",
                    ""ask_rsqn1"": ""1235"",
                    ""bid_price1"": ""76900"",
                    ""bid_rsqn1"": ""2470""
                }
            }";

            _processor.StockPriceReceived += (sender, data) =>
            {
                eventRaised = true;
                receivedData = data;
            };

            _processor.ProcessMessage(hokaMessage);

            Assert.True(eventRaised, "StockPriceReceived 이벤트가 발생하지 않았습니다.");
            Assert.NotNull(receivedData);
            Assert.Equal("005930", receivedData.Symbol);
            Assert.Equal(76900m, receivedData.Price);
            Assert.Equal(1300m, receivedData.PriceChange);
            Assert.Equal("상승", receivedData.ChangeType);
        }

        [Fact]
        public void ProcessMessage_TradeExecutionReceived_EventRaised_ForH0STCNI0Message()
        {
            bool eventRaised = false;
            object receivedData = null;

            // 실시간 체결통보 메시지
            var executionMessage = @"{
                ""header"": {
                    ""tr_id"": ""H0STCNI0"",
                    ""msg_cd"": ""0"",
                    ""msg_tx"": """"
                },
                ""body"": {
                    ""odno"": ""0000123456"",
                    ""prcs_stat_name"": ""체결"",
                    ""pdno"": ""005930"",
                    ""ord_qty"": ""10"",
                    ""cntg_qty"": ""10"",
                    ""cntg_pric"": ""76900"",
                    ""cntg_time"": ""151530"",
                    ""ord_unpr"": ""76900""
                }
            }";

            _processor.TradeExecutionReceived += (sender, data) =>
            {
                eventRaised = true;
                receivedData = data;
            };

            _processor.ProcessMessage(executionMessage);

            Assert.True(eventRaised, "TradeExecutionReceived 이벤트가 발생하지 않았습니다.");
            Assert.NotNull(receivedData);

            // 동적 객체 속성 접근
            var propertyInfo = receivedData.GetType().GetProperty("OrderId");
            Assert.NotNull(propertyInfo);
            Assert.Equal("0000123456", propertyInfo.GetValue(receivedData));

            propertyInfo = receivedData.GetType().GetProperty("StockCode");
            Assert.NotNull(propertyInfo);
            Assert.Equal("005930", propertyInfo.GetValue(receivedData));
        }
    }
}