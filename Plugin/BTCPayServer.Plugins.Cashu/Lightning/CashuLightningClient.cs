// using System;
// using System.Threading;
// using System.Threading.Tasks;
// using BTCPayServer.Lightning;
// using BTCPayServer.Plugins.Cashu.Data;
// using DotNut.Abstractions;
// using Microsoft.EntityFrameworkCore.Internal;
// using Microsoft.Extensions.Logging;
// using System.Linq;
// using BTCPayServer.Plugins.Cashu.Data.Models;
// using DotNut;
// using NBitcoin;
//
// namespace BTCPayServer.Plugins.Cashu;
//
// public class CashuLightningClient: ILightningClient
// {
//     private Uri _mintUrl;
//     private string _storeId;
//     private ILogger _logger;
//     private DbContextFactory<CashuDbContext> _dbContextFactory;
//     private IWalletBuilder _wallet;
//     public CashuLightningClient(Uri mintUrl, string storeId, DbContextFactory<CashuDbContext> dbContextFactory, ILogger logger)
//     {
//         this._mintUrl = mintUrl;
//         this._storeId = storeId;
//         this._logger = logger;
//         this._dbContextFactory = dbContextFactory;
//         
//         this._wallet = Wallet.Create().WithMint(_mintUrl);
//     }
//
//     public override string ToString()
//     {
//         return $"type=cashu;mint-url={_mintUrl};store-id={_storeId};";
//     }
//     
//     //todo add database
//     public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = new CancellationToken())
//     {
//         using var db = _dbContextFactory.CreateDbContext();
//         // var invoice = db.CashuWalletConfig
//     }
//     
//     public async  Task<LightningInvoice> GetInvoice(uint256 paymentHash, CancellationToken cancellation = new CancellationToken())
//     {
//         return await GetInvoice(paymentHash.ToString(), cancellation);
//     }
//     public Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = new CancellationToken())
//     {
//         return ListInvoices(new ListInvoicesParams(), cancellation);
//     }
//
//     public Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = new CancellationToken())
//     {
//         throw new NotImplementedException();
//     }
//     //todo add database
//
//     public Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = new CancellationToken())
//     {
//         throw new NotImplementedException();
//     }
//
//     public async Task<LightningPayment[]> ListPayments(CancellationToken cancellation = new CancellationToken())
//     {
//         return await ListPayments(new ListPaymentsParams(), cancellation);
//     }
//     //todo add database
//
//     public async Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = new CancellationToken())
//     {
//         throw new NotImplementedException();
//     }
//
//     public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
//         CancellationToken cancellation = new CancellationToken())
//     {
//         //we can't do expiry, so it's ignored
//         
//         using var wallet = Wallet
//             .Create()
//             .WithMint(_mintUrl);
//         var mintHAndler = await wallet.CreateMintQuote()
//             .WithAmount((ulong)Math.Round((decimal)(amount.MilliSatoshi/1000)))
//             .WithUnit("sat")
//             .WithDescription(description)
//             .ProcessAsyncBolt11(cancellation);
//         var quote = mintHAndler.GetQuote();
//         
//         await using var db = await _dbContextFactory.CreateDbContextAsync(cancellation);
//         
//         var bolt11 = BOLT11PaymentRequest.Parse(quote.Request, Network.Main);
//         db.LightningClientQuotes.Add(new LightningClientQuote()
//         {
//             
//         })
//         //todo add to db
//         return new LightningInvoice
//         {
//             Id = bolt11.PaymentHash.ToString(),
//             BOLT11 = quote.Request,
//             Amount = quote.Amount is not null ? LightMoney.MilliSatoshis(quote.Amount.Value) : 0,
//             ExpiresAt = quote.Expiry is not null ? DateTimeOffset.FromUnixTimeSeconds(quote.Expiry.Value): bolt11.ExpiryDate,
//             
//         };
//     }
//
//     public async Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest, CancellationToken cancellation = new CancellationToken())
//     {
//         return await CreateInvoice(createInvoiceRequest.Amount, createInvoiceRequest.Description, createInvoiceRequest.Expiry, cancellation);
//     }
//
//     public Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = new CancellationToken())
//     {
//         throw new NotImplementedException();
//     }
//
//     public Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = new CancellationToken())
//     {
//         throw new NotSupportedException();
//     }
//
//     public async Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = new CancellationToken())
//     {
//         await using var db = _dbContextFactory.CreateDbContext();
//         var keysets = await _wallet.GetKeysets(false, cancellation);
//         var keysetIds = keysets.Select(k => k.Id);
//         var amount = db.Proofs
//             .Where(p => p.StoreId == this._storeId && keysetIds.Contains(p.Id))
//             .Select(p => p.Amount).AsEnumerable()
//             .Sum();
//         
//         return new LightningNodeBalance()
//         {
//             OffchainBalance = new OffchainBalance()
//             {
//                 Local = LightMoney.Satoshis(amount)
//             }
//         };
//
//     }
//
//     public async Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation = new CancellationToken())
//     {
//         return await Pay(null, new PayInvoiceParams(), cancellation);
//     }
//
//     public async Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = new CancellationToken())
//     {
//         throw new NotImplementedException();
//     }
//
//     public Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = new CancellationToken())
//     {
//         return await Pay(bolt11, new PayInvoiceParams(), cancellation);
//     }
//     
//     /*
//      * ============= *
//      * Not supported *
//      * ============= *
//      */
//
//     public Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation = new CancellationToken())
//     {
//         throw new NotImplementedException();
//     }
//
//     public Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = new CancellationToken())
//     {
//         throw new NotImplementedException();
//     }
//
//     public Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation = new CancellationToken())
//     {
//         throw new NotImplementedException();
//     }
//
//     public Task CancelInvoice(string invoiceId, CancellationToken cancellation = new CancellationToken())
//     {
//         throw new NotImplementedException();
//     }
//
//     public Task<LightningChannel[]> ListChannels(CancellationToken cancellation = new CancellationToken())
//     {
//         throw new NotImplementedException();
//     }
// }