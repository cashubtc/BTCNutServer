using System;
using System.Collections.Generic;
using BTCPayServer.Plugins.Cashu.Data.enums;
using DotNut;

namespace BTCPayServer.Plugins.Cashu.Services;

public class PollResult
{
    public bool Success => State == CashuPaymentState.Success;
    public CashuPaymentState State { get; set; }
    public List<Proof>? ResultProofs { get; set; }
    public Exception? Error { get; set; }
}