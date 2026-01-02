#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.Errors;
using BTCPayServer.Plugins.Cashu.PaymentHandlers;
using DotNut;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Cashu.Controllers;

[EnableCors(CorsPolicies.All)]
public class CashuPaymentController : Controller
{
    public CashuPaymentController(CashuPaymentService cashuPaymentService)
    {
        _cashuPaymentService = cashuPaymentService;
    }

    private readonly CashuPaymentService _cashuPaymentService;

    /// <summary>
    /// Api endpoint for Paying Invoice via Post Request, by sending all the data exclusively.
    /// </summary>
    /// <param name="token">V4 encoded Cashu Token</param>
    /// <param name="invoiceId"></param>
    /// <returns></returns>
    /// <exception cref="CashuPaymentException"></exception>
    [AllowAnonymous]
    [HttpPost("~/cashu/pay-invoice")]
    public async Task<IActionResult> PayByToken(string token, string invoiceId)
    {
        try
        {
            if (!CashuUtils.TryDecodeToken(token, out var decodedToken))
            {
                throw new CashuPaymentException("Invalid token");
            }
            await _cashuPaymentService.ProcessPaymentAsync(decodedToken, invoiceId);
        }
        catch (CashuPaymentException cex)
        {
            return BadRequest($"Payment Error: {cex.Message} ");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

        return Redirect(Url.ActionAbsolute(this.Request, nameof(UIInvoiceController.Checkout), "UIInvoice", new { invoiceId = invoiceId }).AbsoluteUri);
    }

    /// <summary>
    /// Api endpoint for Paying Invoice via Post Request, by sending nut19 payment payload.
    /// </summary>
    /// <param name="payload"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    [AllowAnonymous]
    [HttpPost("~/cashu/pay-invoice-pr")]
    public async Task<ActionResult> PayByPaymentRequest([FromBody] JObject payload)
    {
        try
        {
            if (payload["mint"] == null || payload["id"] == null || payload["unit"] == null || payload["proofs"] == null)
            {
                throw new ArgumentException("Required fields are missing in the payload.");
            }

            //todo idk why i didn't do it that way, but well i've fucked up
            //it's a workaround - it should be deserialized by JSONSerializer
            var parsedPayload = new PaymentRequestPayload
            {
                Mint = payload["mint"].Value<string>(),
                PaymentId = payload["id"].Value<string>(),
                Memo = payload["memo"]?.Value<string>(),
                Unit = payload["unit"].Value<string>(),
                Proofs = payload["proofs"].Value<JArray>().Select(p => new Proof
                {
                    Amount = p["amount"]!.Value<ulong>(),
                    Id = new KeysetId(p["id"]!.Value<string>()),
                    Secret = new StringSecret(p["secret"]!.Value<string>()),
                    C = new PubKey(p["C"]!.Value<string>()),
                }).ToArray()
            };

            // var parsedPayload = JsonSerializer.Deserialize<PaymentRequestPayload>(paymentPayload);
            //   "id": str <optional>, will correspond to invoiceId
            //   "memo": str <optional>, idc about this
            //   "mint": str, //if trusted mint - save to db, if not - melt 🔥
            //   "unit": <str_enum>, should always be in sat, since there aren't any standardisation for unit denomination
            //   "proofs": Array<Proof>  yeah proofs

            var token = new CashuToken
            {
                Tokens =
                [
                    new CashuToken.Token
                    {
                        Mint = parsedPayload.Mint,
                        Proofs = parsedPayload.Proofs.ToList()
                    }
                ],
                Memo = parsedPayload.Memo,
                Unit = parsedPayload.Unit
            };

            await _cashuPaymentService.ProcessPaymentAsync(token, parsedPayload.PaymentId);
            return Ok("Payment sent!");
        }
        catch (CashuPaymentException cex)
        {
            return BadRequest($"Payment Error: {cex.Message}");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
    
    // this method is useless
    // check for `GetInvoicePaymentMethods` in `GreenfieldInvoiceController`
    // if you look `ToPaymentMethodModels`, you can see that you can add fields in the `AdditionalData` field
    // it may already be exposed if you already implemented `ParsePaymentPromptDetails`
    // `api/v1/stores/{storeId}/invoices/{invoiceId}/payment-methods`
    // API to fetch payment request for invoice
    [HttpGet("~/cashu/get-payment-request")]
    public Task<IActionResult> GetPaymentRequest()
    {
        throw new NotImplementedException();
    }
}
