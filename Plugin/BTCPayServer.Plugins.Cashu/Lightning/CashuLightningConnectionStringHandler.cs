// using System;
// using System.Diagnostics.CodeAnalysis;
// using System.Linq;
// using System.Net.Http;
// using BTCPayServer.Lightning;
// using Microsoft.Extensions.Logging;
// using NBitcoin;
//
// namespace BTCPayServer.Plugins.Cashu;
//
// public class CashuLightningConnectionStringHandler: ILightningConnectionStringHandler
// {
//     private readonly IHttpClientFactory _httpClientFactory;
//     private readonly ILoggerFactory _loggerFactory;
//     
//     public CashuLightningConnectionStringHandler(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
//     {
//         _httpClientFactory = httpClientFactory;
//         _loggerFactory = loggerFactory;
//     }
//     
//     public ILightningClient? Create(string connectionString, Network network, out string? error)
//     {
//         var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
//         if (type != "cashu")
//         {
//             error = null;
//             return null;
//         }
//
//         if (!kv.TryGetValue("mint-url", out var url))
//         {
//             error = "Mint url expected";
//             return null;
//         }
//         
//         if (!kv.TryGetValue("store-id", out var storeId))
//         {
//             error = "Store Id expected";
//             return null;
//         }
//
//         Uri uri = new Uri(url);
//         
//         bool allowInsecure = false;
//         if (kv.TryGetValue("allowinsecure", out var allowinsecureStr))
//         {
//             var allowedValues = new[] {"true", "false"};
//             if (!allowedValues.Any(v => v.Equals(allowinsecureStr, StringComparison.OrdinalIgnoreCase)))
//             {
//                 error = "The key 'allowinsecure' should be true or false";
//                 return null;
//             }
//
//             allowInsecure = allowinsecureStr.Equals("true", StringComparison.OrdinalIgnoreCase);
//         }
//
//         if (!LightningConnectionStringHelper.VerifySecureEndpoint(uri, allowInsecure))
//         {
//             error = "The key 'allowinsecure' is false, but server's Uri is not using https";
//             return null;
//         }
//
//         error = null;
//         
//
//         kv.TryGetValue("wallet-id", out var walletId);
//         var cashuLnClient = new CashuLightningClient(uri, storeId, _loggerFactory.CreateLogger(nameof(CashuLightningClient));
//         
//         try
//         {
//             
//         }
//         catch
//         {
//             error = "Mint unreachable";
//         }
//
//         return cashuLnClient;
//     }
// }