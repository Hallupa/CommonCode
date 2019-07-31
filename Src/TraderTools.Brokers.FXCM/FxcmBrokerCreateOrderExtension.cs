using System;
using System.Reflection;
using fxcore2;
using log4net;
using TraderTools.Basics;

namespace TraderTools.Brokers.FXCM
{
    public static class FxcmBrokerCreateOrderExtension
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static bool CreateOrder(this FxcmBroker fxcm, string market, double rate, DateTime? expiryDateUtc, decimal amount,
            TradeDirection direction, IBrokersCandlesService candlesService, IMarketDetailsService marketDetailsService)
        {
            var requestFactory = fxcm.Session.getRequestFactory();

            // Get price
            var offer = GetOffer(fxcm.Session, market);
            if (offer == null)
            {
                Log.Error($"Unable to get offer price  - {requestFactory.getLastError()}");
                return false;
            }

            // Get account
            var account = GetAccount(fxcm.Session);
            var loginRules = fxcm.Session.getLoginRules();
            var tradingSettingsProvider = loginRules.getTradingSettingsProvider();

            var iCondDistEntryLimit = tradingSettingsProvider.getCondDistEntryLimit(market);
            var iCondDistEntryStop = tradingSettingsProvider.getCondDistEntryStop(market);

            // Get order type
            var buySell = direction == TradeDirection.Long ? "B" : "S";
            var sOrderType = GetEntryOrderType(offer.Bid, offer.Ask, rate, buySell, offer.PointSize, iCondDistEntryLimit, iCondDistEntryStop);


            var valuemap = requestFactory.createValueMap();
            var sOfferID = Guid.NewGuid().ToString();
            valuemap.setString(O2GRequestParamsEnum.Command, Constants.Commands.CreateOrder);
            valuemap.setString(O2GRequestParamsEnum.OrderType, sOrderType);
            valuemap.setString(O2GRequestParamsEnum.AccountID, account.AccountID);
            valuemap.setString(O2GRequestParamsEnum.OfferID, sOfferID);
            valuemap.setString(O2GRequestParamsEnum.BuySell, buySell);
            valuemap.setInt(O2GRequestParamsEnum.Amount, (int)amount);
            valuemap.setDouble(O2GRequestParamsEnum.Rate, rate);
            valuemap.setString(O2GRequestParamsEnum.CustomID, "EntryOrder");

            // Set expiry
            if (expiryDateUtc != null)
            {
                var expiryStr = expiryDateUtc.Value.ToString("yyyyMMdd-HH:mm:ss.SSS");
                valuemap.setString(O2GRequestParamsEnum.TimeInForce, Constants.TIF.GTD);
                valuemap.setString(O2GRequestParamsEnum.ExpireDateTime, expiryStr); // UTCTimestamp format: "yyyyMMdd-HH:mm:ss.SSS" (milliseconds are optional)
            }

            // Create order
            var request = requestFactory.createOrderRequest(valuemap);
            if (request == null)
            {
                Log.Error($"Unable to create FXCM order - {requestFactory.getLastError()}");
                return false;
            }

            return true;
        }

        private static O2GOfferRow GetOffer(O2GSession session, string market)
        {
            O2GOfferRow offer = null;
            bool bHasOffer = false;
            O2GResponseReaderFactory readerFactory = session.getResponseReaderFactory();
            if (readerFactory == null)
            {
                throw new Exception("Cannot create response reader factory");
            }
            var loginRules = session.getLoginRules();
            var response = loginRules.getTableRefreshResponse(O2GTableType.Offers);
            var offersResponseReader = readerFactory.createOffersTableReader(response);
            for (int i = 0; i < offersResponseReader.Count; i++)
            {
                offer = offersResponseReader.getRow(i);
                if (offer.Instrument.Equals(market))
                {
                    if (offer.SubscriptionStatus.Equals("T"))
                    {
                        bHasOffer = true;
                        break;
                    }
                }
            }
            if (!bHasOffer)
            {
                return null;
            }
            else
            {
                return offer;
            }
        }

        private static string GetEntryOrderType(double dBid, double dAsk, double dRate, string sBuySell, double dPointSize, int iCondDistLimit, int iCondDistStop)
        {
            double dAbsoluteDifference = 0.0D;
            if (sBuySell.Equals(Constants.Buy))
            {
                dAbsoluteDifference = dRate - dAsk;
            }
            else
            {
                dAbsoluteDifference = dBid - dRate;
            }
            int iDifferenceInPips = (int)Math.Round(dAbsoluteDifference / dPointSize);

            if (iDifferenceInPips >= 0)
            {
                if (iDifferenceInPips <= iCondDistStop)
                {
                    throw new Exception("Price is too close to market.");
                }
                return Constants.Orders.StopEntry;
            }
            else
            {
                if (-iDifferenceInPips <= iCondDistLimit)
                {
                    throw new Exception("Price is too close to market.");
                }
                return Constants.Orders.LimitEntry;
            }
        }

        public static O2GAccountRow GetAccount(this O2GSession session)
        {
            O2GResponseReaderFactory readerFactory = session.getResponseReaderFactory();
            if (readerFactory == null)
            {
                throw new Exception("Cannot create response reader factory");
            }
            var loginRules = session.getLoginRules();
            var response = loginRules.getTableRefreshResponse(O2GTableType.Accounts);
            var accountsResponseReader = readerFactory.createAccountsTableReader(response);

            for (int i = 0; i < accountsResponseReader.Count; i++)
            {
                var account = accountsResponseReader.getRow(i);
                string sAccountKind = account.AccountKind;
                if (sAccountKind.Equals("32") || sAccountKind.Equals("36"))
                {
                    if (account.MarginCallFlag.Equals("N"))
                    {
                        return account;
                    }
                }
            }

            return null;
        }
    }
}
