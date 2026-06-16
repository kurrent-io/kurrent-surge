using Kurrent.Client;
using Kurrent.Client.Schema;
using Kurrent.Schema.Catalogs;
using Transfermarkt.Domain.Events;

namespace Transfermarkt.Domain;

[CustomMessageCatalog]
sealed class TransfermarktMessageCatalog() : MessageCatalog(
    "transfermarkt", alwaysActive: true, catalog => catalog
        .Map<PlayerRegistered>("PlayerRegistered")
        .Map<PlayerTransferred>("PlayerTransferred")
        .Map<PlayerRetired>("PlayerRetired")
        .Map<LoanConvertedToPermanent>("LoanConvertedToPermanent"));
