# BTCNutServer - Cashu plugin for BTCPay Server

> **Warning:** This plugin is in early beta and has not been audited. The author is not a cryptographer. Do not use with amounts you cannot afford to lose.

## Overview

BTCNutServer is a BTCPay Server plugin that lets merchants accept Bitcoin payments via [Cashu](https://cashu.space) ecash tokens. Received proofs are stored in a per-store wallet. The plugin handles double-spend protection, fee validation, and payment recovery automatically.

---

## Payment Modes

### Trusted Mints Only

Accepts tokens only from mints explicitly listed as trusted. Received proofs are held in the store wallet without melting. No Lightning backend required.

The merchant can later export the stored balance as a serialized Cashu token and redeem it elsewhere (e.g. [redeem.cashu.me](https://redeem.cashu.me)).

### Hold When Trusted

Requires a Lightning backend. Tokens from trusted mints are held as proofs. Tokens from untrusted mints are melted immediately to the Lightning wallet.

### Auto Convert

Requires a Lightning backend. All incoming tokens are melted to Lightning immediately, regardless of mint.

---

## Checkout

The invoice page displays a [NUT-18](https://github.com/cashubtc/nuts/blob/main/18.md) payment request with http transport as a QR code (`cashu://...`). Customers can pay in two ways:

- **Scan QR** - using a Cashu wallet that supports NUT-19. Requires a publicly reachable server (localhost will not work).
- **Paste token** - paste a serialized Cashu v4 token directly into the payment field.

---

## Configuration

| Option | Description |
|---|---|
| Payment Mode | `Trusted Mints Only`, `Hold When Trusted`, or `Auto Convert` |
| Max Lightning Fee | Maximum Lightning routing fee as a percentage of the transaction amount |
| Max Mint Fee | Maximum mint melt fee as a percentage of the transaction amount |
| Customer Fee Advance | Fixed amount in sats charged to the customer upfront to cover fees |
| Trusted Mints | List of trusted mint URLs (one per line, no trailing slash) |

---

## Wallet

Each store has an embedded wallet. Proofs are grouped by mint and unit. From the wallet view, merchants can export any balance as a Cashu v4 token.

**Onboarding:** Generate a 12-word mnemonic (or restore from an existing one)

**Restore:** Restoring from mnemonic runs as a background job. You can track progress and see which mints were unreachable. 

**With cashu, you can't share the same mnemonic across wallets**.

---

## Cashu Lightning Client (Experimental)

The plugin registers a custom `ILightningClient` that uses a Cashu mint as a Lightning backend. This enables BTCPay to receive payments by creating NUT-04 mint quotes and tracking confirmation via WebSocket subscriptions (NUT-17).

**Connection string format:**
```
type=cashu;mint-url=https://mint.example.com;store-id=<storeId>;secret=<guid>
```

The `secret` is a per-store GUID set in settings. It is required to authorize outbound lightning payments - without it the Lightning client is receive-only.

**Supported operations:**
- Create and retrieve invoices (mint quotes)
- Listen for payment confirmation (WebSocket-based, with missed-invoice recovery on reconnect)
- Pay outbound Lightning invoices by melting stored proofs (NUT-05)
- Report wallet balance from stored available proofs

**Not supported:** channel management, deposit addresses, invoice cancellation.

On connection, the plugin validates that the mint supports WebSockets, has bolt11 mint/melt enabled, and accepts the `sat` unit. If any check fails, BTCPay will show the mint as unavailable.

---

## Payment Recovery

Network failures during a swap or melt are tracked as failed transactions. Recovery runs automatically in the background every 2 minutes (up to 20 retries with exponential backoff).

If a customer reports their token as spent but the invoice is unconfirmed, the merchant can open the failed transactions screen and retry manually. If the payment went through, it will be marked as settled automatically.

---

## Greenfield API

The plugin exposes a REST API under `/api/v1/stores/{storeId}/cashu`:

- `GET/PUT /cashu` - read or update configuration
- `POST /cashu/wallet` - create a new wallet
- `POST /cashu/wallet/restore` - restore from mnemonic + mint list
- `GET /cashu/wallet/restore/{jobId}` - check restore job status
- `DELETE /cashu/wallet` - delete wallet and all stored proofs
- `GET /cashu/wallet/balances` - balances grouped by mint and unit
- `GET /cashu/tokens` - list exported tokens
- `GET /cashu/failed-transactions` - list failed transactions
- `POST /cashu/failed-transactions/{id}/retry` - manually retry a failed transaction

---

## Contributing

1. Fork the repository
2. Create a branch from `main`
3. Submit a pull request with a description of what changed and why

🥜󠅓󠅑󠅣󠅘󠅥󠄲󠅟󠄢󠄶󠅤󠅕󠄳󠄺󠅟󠅔󠄸󠅂󠅧󠅓󠅪󠅟󠅦󠄼󠄢󠄡󠅠󠅒󠅞󠅁󠅥󠅒󠅇󠅜󠅥󠅑󠅇󠄺󠅠󠅔󠄸󠄽󠅥󠅉󠄢󠄶󠅪󠅑󠄳󠄩󠄳󠅑󠅈󠅂󠅚󠅒󠄢󠅜󠅥󠅉󠅈󠅆󠅚󠅓󠄢󠄶󠄠󠅉󠅈󠅃󠄲󠅟󠅝󠄶󠅠󠅃󠄱󠄱󠅁󠅕󠅄󠅖󠅒󠄴󠄽󠅘󠅜󠅉󠅈󠄳󠄷󠅠󠄷󠄶󠅘󠄷󠅁󠅁󠄱󠅉󠅈󠄾󠄤󠅁󠄴󠅗󠄥󠄿󠅄󠄾󠅚󠅊󠅄󠄲󠅛󠅉󠅪󠅛󠅩󠅉󠅄󠅛󠄡󠅉󠅪󠄵󠄡󠄾󠅇󠄹󠄢󠄿󠄴󠅜󠅘󠅉󠅇󠅆󠅚󠄿󠄴󠄱󠅨󠄽󠅪󠅗󠄠󠅉󠄢󠄹󠄣󠅊󠅝󠄾󠅝󠅊󠅄󠅛󠄢󠅊󠄴󠅅󠅩󠄾󠅇󠅊󠅜󠄾󠅚󠅉󠄤󠄽󠅝󠅊󠅚󠄾󠄴󠄲󠅙󠄿󠄴󠄽󠄣󠄿󠄴󠅁󠄤󠅊󠅚󠅆󠅘󠅉󠄡󠅗󠅘󠄱󠄝󠅡󠅨󠅛󠅅󠄺󠄥󠅢󠅞󠅖󠅑󠅓󠄲󠅈󠄤󠅂󠅁󠄲󠅔󠄦󠅊󠄳󠅅󠄝󠅩󠅩󠅪󠄩󠄸󠄵󠅧󠅒󠄿󠅚󠅅󠅢󠄡󠅇󠅉󠅉󠄴󠄹󠄻󠅉󠅇󠅃󠅚󠅉󠅇󠅆󠅉󠄹󠄴󠄠󠄵󠅗󠅜󠅞󠅠󠄿󠄣󠅀󠅇󠅁󠅘󠄴󠅗󠅖󠄻󠄴󠄽󠄵󠅆󠄱󠅀󠄸󠄼󠅔󠅘󠅗󠅒󠅂󠄢󠅉󠄦󠅦󠅁󠄥󠄹󠄸󠅉󠄸󠅡󠄶󠅀󠅉󠅈󠄾󠅉󠄹󠄶󠅜󠄧󠅆󠅘󠄳󠄳󠄳󠄡󠄷󠄠󠄼󠄷󠅛󠅣󠅡󠅝󠅡󠄼󠄱󠄹󠄺󠄦󠅩󠄽󠄱󠅩󠄠󠅘󠄢󠄠󠄦󠅑󠅠󠄝󠅪󠅅󠄦󠅝󠅉󠄠󠅥󠅜󠅉󠅈󠄺󠅉󠄹󠄻󠅡󠄢󠅩󠄷󠄡󠅈󠄲󠄾󠄣󠅒󠅄󠅄󠄲󠄻󠄨󠄦󠅆󠅊󠅇󠅨󠄷󠅜󠄩󠅧󠅇󠄴󠄷󠄵󠅈󠅈󠄠󠅦󠄺󠅂󠅨󠄿󠄽󠄲󠅉󠄵󠅟󠅊󠅠󠄷󠄶󠅘󠄷󠅁󠄵󠄱󠅉󠅈󠄾󠄤󠅁󠄴󠄶󠅜󠄽󠅝󠄽󠅧󠄿󠄴󠄺󠅝󠄽󠅇󠅅󠄤󠅉󠄢󠄽󠄣󠄿󠄴󠅅󠄢󠄿󠅇󠄶󠅛󠄾󠄢󠅊󠅚󠅊󠅝󠄶󠅜󠄾󠄢󠄵󠅪󠄽󠅇󠅁󠄣󠄿󠄴󠅅󠄢󠄽󠅝󠅉󠄠󠄽󠅚󠅜󠅛󠅉󠅇󠄾󠅛󠄾󠄴󠅅󠄥󠄽󠅚󠄺󠅙󠄿󠅄󠅉󠄣󠄿󠅄󠅓󠅪󠅉󠅇󠅁󠄤󠅊󠅄󠅂󠅚󠅉󠅝󠅂󠅘󠅉󠄡󠅗󠅘󠄱󠅨󠄺󠅇󠅁󠄽󠅔󠄡󠅢󠅥󠄨󠅂󠅆󠅢󠅪󠄼󠄩󠄹󠄻󠅈󠅠󠅉󠅊󠅆󠄣󠅚󠄨󠄦󠅚󠅤󠅑󠅞󠅛󠄷󠅂󠅥󠄴󠄝󠄴󠄝󠅕󠄢󠅪󠅧󠅉󠅇󠅃󠅚󠅉󠅇󠅆󠅉󠄹󠅀󠅕󠅪󠅗󠄺󠄝󠅆󠄳󠄵󠄧󠅗󠅑󠄢󠅡󠄻󠅉󠄾󠅑󠅗󠅇󠅠󠄡󠅀󠄣󠄴󠅈󠅨󠅗󠄶󠅨󠅝󠄠󠅓󠄠󠅀󠅜󠅝󠄥󠅄󠅂󠅁󠄿󠄴󠅉󠅈󠄾󠅉󠄹󠄻󠅨󠅦󠅧󠄸󠅄󠄱󠄺󠄦󠅥󠄶󠅨󠄶󠄺󠅡󠅕󠅀󠄲󠄸󠅝󠅜󠅀󠅡󠄲󠅁󠄺󠅓󠄝󠅩󠄽󠄿󠅗󠅨󠅇󠄿󠄳󠄥󠅝󠅡󠅂󠅜󠅄󠅪󠅉󠅈󠄺󠅉󠄹󠄸󠅊󠅤󠅖󠄴󠅈󠄢󠄴󠅚󠄴󠅑󠄵󠅏󠅇󠅂󠄿󠄵󠅗󠄱󠅜󠅘󠅚󠅈󠄡󠄝󠄺󠄣󠅓󠅂󠅘󠄵󠄠󠅁󠅘󠄱󠅟󠅠󠄽󠄝󠄥󠅧󠅨󠅨󠅠󠄷󠄶󠅘󠄷󠄳󠄲󠅘󠅓󠄣󠅘󠄱󠄽󠅪󠄵󠄤󠄾󠄴󠅁󠅧󠅊󠄴󠅓󠅪󠅊󠄴󠅜󠅚󠄾󠅚󠅂󠅘󠅉󠅪󠄽󠅨󠄾󠅪󠅛󠄤󠄾󠅚󠅓󠅨󠄾󠄢󠅁󠅨󠄾󠅝󠄽󠄢󠅊󠅚󠅔󠅘󠅊󠄷󠄽󠅪󠄾󠄴󠄵󠅧󠄽󠄴󠄵󠄣󠄽󠅪󠅘󠅝󠅉󠅄󠄱󠅨󠅊󠅇󠄶󠅘󠄾󠅄󠅉󠅧󠄽󠄴󠅗󠅩󠄾󠅪󠄶󠅛󠄾󠄴󠄹󠄤󠄽󠅝󠄶󠅚󠅇󠄳󠄵󠄳󠅚󠅅󠅪󠅠󠅂󠄽󠄷󠄺󠅖󠄥󠅛󠄺󠅇󠅆󠄩󠄹󠄤󠄿󠅪󠅇󠅣󠅛󠅊󠄣󠄳󠄡󠅖󠄸󠄳󠅚󠅄󠄴󠅑󠄝󠄺󠄥󠅦󠅦󠄝󠅓󠄻󠄺󠅜󠅘󠅊󠄻󠄾󠅘󠅊󠅆󠅗󠅗󠅅󠄦󠅆󠄾󠄢󠅣󠄣󠄣󠄣󠄩󠅕󠅉󠅥󠅑󠄹󠅝󠅄󠅚󠄦󠄡󠄩󠅀󠄻󠅄󠄨󠅗󠄣󠅊󠅢󠄦󠄨󠄧󠄠󠄹󠄝󠄵󠄶󠅚󠄼󠄿󠄴󠄺󠅂󠅘󠅓󠄡󠅗󠅗󠅃󠄩󠄲󠅟󠄺󠅖󠄺󠅒󠄻󠄸󠄶󠅑󠅞󠅁󠅞󠅃󠄤󠄝󠅕󠄵󠅛󠅑󠄴󠄾󠅠󠄷󠄱󠅡󠄝󠄻󠄲󠄩󠄿󠄿󠄼󠅩󠅑󠄨󠅩󠄴󠅠󠄥󠄥󠅘󠅓󠅜󠅗󠅗󠅏󠅓󠄿󠄢󠅁󠅔󠄨󠅃󠅅󠅢󠅄󠄳󠄱󠅥󠄸󠄠󠅨󠅟󠄩󠅉󠅧󠄼󠅅󠅀󠅣󠅚󠅨󠅕󠅁󠄽󠅃󠅃󠄾󠅖󠅑󠅧󠅑󠄽󠄤󠄺󠅊󠅝󠅝󠅛󠅉󠅇󠄵󠅁󠅉󠅈󠄾󠄤󠅁󠄴󠅛󠄢󠅉󠄢󠄵󠄤󠄾󠅚󠅗󠄤󠅉󠅄󠅉󠄡󠄾󠅝󠄶󠅛󠄾󠅚󠅂󠅛󠄽󠄷󠅆󠅙󠄽󠅄󠅅󠄣󠄾󠄴󠅓󠄣󠄾󠅝󠄶󠅛󠄿󠄷󠄵󠄤󠄿󠄷󠄺󠅙󠄾󠅇󠄶󠅜󠅉󠅚󠅅󠄡󠄿󠄴󠅗󠄤󠅉󠅚󠄵󠅩󠄾󠅄󠄵󠄣󠄽󠅚󠄽󠅧󠄽󠅪󠅂󠅚󠄽󠄢󠅁󠄢󠄽󠅇󠄺󠅜󠄾󠅚󠄾󠅘󠅉󠄡󠅗󠅘󠄱󠅥󠅏󠅣󠅕󠅪󠅇󠅃󠅊󠄧󠅃󠅛󠄵󠅤󠄷󠅛󠅟󠅂󠄤󠄹󠅦󠅝󠅜󠅇󠅛󠄹󠄸󠅓󠅈󠄲󠅖󠅛󠅔󠄢󠅜󠅕󠅀󠅚󠅙󠅒󠅢󠅔󠄩󠅆󠅉󠅇󠅃󠅚󠅉󠅇󠅆󠅉󠄹󠄻󠅒󠄿󠅤󠄤󠅡󠅏󠄼󠄳󠅟󠅅󠅝󠅞󠅨󠅄󠅟󠅒󠅤󠄳󠄥󠄽󠄠󠅣󠅛󠅛󠄤󠄢󠄼󠅘󠅚󠅝󠅉󠄱󠄼󠄣󠅞󠅣󠅗󠅆󠅟󠄩󠅖󠅨󠅉󠅈󠄾󠅉󠄹󠄲󠅆󠅅󠄽󠅒󠄹󠅁󠅗󠄩󠄿󠄤󠅞󠄷󠅦󠄨󠄝󠄿󠅗󠄹󠅥󠄲󠄱󠅪󠄺󠄱󠅒󠅦󠄺󠅕󠄡󠅘󠅢󠄽󠅟󠄥󠅤󠄶󠅪󠄺󠅉󠄴󠄵󠅊󠅉󠅈󠄺󠅉󠄹󠄳󠄣󠅤󠅁󠄧󠅡󠅪󠅛󠅕󠄵󠄦󠅈󠅘󠅙󠅇󠅈󠄩󠄻󠄠󠅜󠅝󠅤󠄩󠅘󠄢󠅖󠅓󠅣󠄻󠄵󠅒󠅗󠄩󠄿󠄻󠄾󠅄󠅅󠅥󠅅󠄶󠄾󠅣󠅠󠄷󠄶󠅘󠄳󠄷󠄶󠅪󠅕󠄵󠄲󠅝󠄽󠅝󠄽󠄡󠄽󠅪󠄶󠅜󠅉󠅄󠅉󠅪󠄾󠄴󠄾󠅘󠄾󠅝󠄾󠅘󠅉󠅪󠅅󠄣󠄿󠅄󠄹󠅩󠄿󠄴󠄶󠅚󠄾󠅪󠅅󠄢󠄾󠅚󠄱󠅨󠄾󠅚󠅅󠄡󠄿󠄴󠅊󠅚󠄽󠅄󠅅󠅨󠅊󠄷󠄺󠅘󠄽󠅪󠄹󠅧󠄽󠄴󠅊󠅝󠄾󠅪󠅘󠅘󠅊󠅄󠅉󠄠󠄿󠅄󠄺󠅝󠅉󠅇󠅉󠅪󠄾󠅪󠄲󠅝󠅉󠅇󠄾󠅉󠄹󠅁󠄻󠅏󠄧󠄻󠄴󠅧󠅄󠅥󠅝󠄤󠅉󠅃󠄿󠅤󠅗󠅂󠄳󠄠󠅀󠅓󠄠󠅞󠅅󠄳󠅀󠅅󠄤󠄹󠄶󠅈󠄼󠄱󠅟󠅟󠄽󠅁󠄽󠄵󠄶󠄤󠄽󠅚󠅂󠄷󠄶󠅛󠅟󠄢󠄶󠅜󠅇󠄳󠄲󠅨󠅁󠄼󠅘󠅥󠅢󠅒󠅇󠅢󠅦󠄹󠅤󠅢󠄱󠅘󠅓󠅝󠄝󠄨󠅣󠄤󠄼󠅆󠅂󠅗󠅧󠄲󠄨󠄡󠄼󠄠󠄡󠅓󠄾󠅤󠅙󠄝󠅑󠄶󠅟󠅠󠅏󠄢󠄶󠅪󠅇󠄳󠄴󠄦󠅅󠅩󠅕󠅁󠅅󠅢󠅚󠅈󠅠󠅤󠄺󠅒󠅩󠅑󠄺󠅨󠅤󠅇󠄠󠄤󠄺󠄴󠄢󠅛󠅆󠅧󠅝󠄴󠄠󠄠󠅣󠅨󠅛󠅤󠅗󠄳󠄻󠅗󠄠󠄾󠅚󠅝󠄶󠅩󠅇󠄳󠄱󠄸󠅨󠄶󠅘󠅔󠅪󠄻󠄻󠅥󠄝󠅜󠅖󠄱󠄢󠄨󠅂󠄥󠅢󠅉󠄾󠄹󠄺󠄶󠅚󠄹󠅃󠅂󠅙󠅗󠄤󠄠󠅗󠄣󠅧󠅪󠅪󠄡󠅟󠄦󠅪󠅘󠄢󠅑󠅂󠅘󠅉󠅁󠄶󠅘󠅓󠄣󠅘󠄱󠄽󠅄󠅘󠅚󠄾󠄴󠄺󠅜󠅊󠅚󠅗󠄣󠄽󠅚󠅁󠄡󠄽󠅪󠄱󠄢󠅊󠅝󠅁󠄤󠄾󠅚󠄵󠄤󠅉󠅚󠅗󠄣󠄿󠅇󠄽󠄣󠅉󠅇󠅆󠅘󠄽󠅇󠅅󠄤󠅉󠅚󠅅󠅩󠅊󠅚󠅆󠅜󠄿󠄴󠅁󠄡󠅉󠅚󠅁󠄣󠅉󠅚󠅛󠄤󠄾󠅪󠅗󠅨󠄽󠅄󠄱󠅨󠅊󠅝󠄹󠅧󠅉󠅇󠄶󠅜󠄾󠅪󠅜󠅝󠄾󠄢󠄶󠅚󠅇󠄳󠄵󠄳󠄤󠄨󠄴󠄱󠄲󠅠󠅆󠅨󠄷󠅡󠅡󠄥󠄾󠅅󠅨󠅃󠅩󠄥󠄦󠅒󠄵󠅥󠄠󠅞󠅀󠅝󠄲󠅂󠄢󠄢󠄧󠅑󠅗󠅖󠅊󠅗󠅀󠅆󠄵󠅠󠅤󠅑󠄲󠅘󠅊󠄻󠄾󠅘󠅊󠅆󠅗󠅗󠅩󠄠󠅇󠅖󠅗󠄻󠄳󠅚󠅢󠅑󠄿󠄽󠅂󠄶󠅚󠅅󠄻󠅘󠅇󠄷󠄤󠅔󠅑󠄠󠅞󠅂󠅊󠅓󠅙󠅅󠄣󠄦󠅛󠄱󠅒󠅃󠅉󠄲󠄧󠄾󠅃󠄿󠄶󠅘󠅓󠄡󠅗󠅗󠅒󠄹󠅑󠅨󠄸󠅑󠄼󠅅󠅅󠄻󠄺󠅕󠄲󠅗󠅒󠅁󠄴󠅗󠅃󠅦󠅔󠅘󠄾󠅔󠅏󠅜󠄤󠅏󠅉󠄩󠅪󠄡󠄣󠅥󠄱󠅝󠅃󠅙󠅆󠅧󠅜󠅝󠅆󠅘󠅓󠅜󠅗󠅗󠅪󠄱󠄷󠅨󠄣󠄤󠄴󠅥󠅔󠄦󠅙󠅝󠄝󠄹󠅢󠅄󠅉󠅆󠅖󠅨󠅠󠄠󠅖󠄵󠅜󠄠󠄤󠄳󠄠󠄳󠄝󠅚󠅩󠄧󠅟󠅁󠅤󠅞󠅥󠄳󠅣󠅡󠄨