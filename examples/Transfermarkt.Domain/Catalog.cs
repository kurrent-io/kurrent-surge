using Kurrent.Client;
using Kurrent.Client.Schema;
using Transfermarkt.Domain.Events;

namespace Transfermarkt.Domain;

[CustomMessageCatalog]
sealed class TransfermarktMessageCatalog() : MessageCatalog(
    "transfermarkt", alwaysActive: true, catalog => catalog
        .Map<PlayerRegistered>("PlayerRegistered")
        .Map<PlayerTransferred>("PlayerTransferred")
        .Map<PlayerRetired>("PlayerRetired")
        .Map<LoanConvertedToPermanent>("LoanConvertedToPermanent"));
