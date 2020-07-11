using System;
using fxcore2;

namespace TraderTools.Brokers.FXCM
{
    public class MonitorLivePrices : IDisposable
    {
        private readonly FxcmBroker _fxcm;
        private readonly Action<(string Instrument, double Bid, double Ask, DateTime Time)> _processUpdateAction;
        private ResponseListener _responseListener;

        public MonitorLivePrices(
            FxcmBroker fxcm,
            Action<(string Instrument, double Bid, double Ask, DateTime Time)> processUpdateAction)
        {
            _fxcm = fxcm;
            _processUpdateAction = processUpdateAction;

            _responseListener = new ResponseListener(_fxcm.Session, ProcessResponse);
            _fxcm.Session.subscribeResponse(_responseListener);
        }

        private void ProcessResponse(O2GResponse resp)
        {
            if (resp.Type == O2GResponseType.TablesUpdates)
            {
                O2GResponseReaderFactory readerFactory = _fxcm.Session.getResponseReaderFactory();
                if (readerFactory == null)
                {
                    throw new Exception("Cannot create response reader factory");
                }
                O2GOffersTableResponseReader responseReader = readerFactory.createOffersTableReader(resp);
                for (int i = 0; i < responseReader.Count; i++)
                {
                    O2GOfferRow offerRow = responseReader.getRow(i);
                    if (offerRow.isTimeValid && offerRow.isBidValid && offerRow.isAskValid)
                    {
                        _processUpdateAction((
                            offerRow.Instrument,
                            offerRow.Bid,
                            offerRow.Ask,
                            offerRow.Time));
                    }
                }
            }
        }

        public void Dispose()
        {
            _fxcm.Session.unsubscribeResponse(_responseListener);
        }
    }
}