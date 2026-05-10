// // ReSharper disable ClassNeverInstantiated.Global
//
// using Kurrent.Processors;
// using Transfermarkt.Domain;
//
// namespace Processor.AspNetMinimal;
//
// public class TransferProcessorV2 : MessageProcessor {
//     public TransferProcessorV2() {
//         Process<PlayerTransferred>((transfer, ctx) => ctx.Logger.LogInformation(
//             "Transfer: {PlayerName} from {FromClub} to {ToClub} for {Fee:C}",
//             transfer.PlayerName.ToString(),
//             transfer.FromClubId.ToString(),
//             transfer.ToClubId.ToString(),
//             transfer.Fee.Amount));
//
//         Process<PlayerRegistered>((registered, ctx) => ctx.Logger.LogInformation(
//             "Registration: {PlayerName} ({Nationality}, {Position})",
//             registered.Name.ToString(),
//             registered.Nationality.ToString(),
//             registered.MainPosition.ToString()));
//     }
// }
//
// public class TransferProcessor : IRecordProcessor {
//     public ValueTask ProcessRecord(ProcessRecordContext context) {
//         switch (context.Record.Value) {
//             case PlayerTransferred transfer:
//                 context.Logger.LogInformation(
//                     "Transfer: {PlayerName} from {FromClub} to {ToClub} for {Fee:C}",
//                     transfer.PlayerName.ToString(),
//                     transfer.FromClubId.ToString(),
//                     transfer.ToClubId.ToString(),
//                     transfer.Fee.Amount
//                 );
//                 break;
//
//             case PlayerRegistered registered:
//                 context.Logger.LogInformation(
//                     "Registration: {PlayerName} ({Nationality}, {Position})",
//                     registered.Name.ToString(),
//                     registered.Nationality.ToString(),
//                     registered.MainPosition.ToString()
//                 );
//                 break;
//         }
//
//         return ValueTask.CompletedTask;
//     }
// }
//
// public class TransferProcessorV3 : IRecordBatchProcessor {
//     public int      MaxBatchSize => 1000;
//     
//     public TimeSpan BatchWindow  => TimeSpan.FromSeconds(1);
//
//     public ValueTask ProcessBatch(BatchContext context) {
//        
//         // 1000
//         
//     }
// }
//
