using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Admin;
using Aspire.Hosting.KurrentDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

public static class KurrentDBCommandExtensions {
    extension(IResourceBuilder<KurrentDBResource> builder) {
        public IResourceBuilder<KurrentDBResource> WithAdminCommands() {
            var provider = builder.Resource.Parent;

            builder
                .WithStartScavengeCommand(provider)
                .WithStopScavengeCommand(provider)
                .WithScavengeStatusCommand(provider)
                .WithResignNodeCommand(provider)
                .WithStopNodeCommand(provider);

            return builder;
        }

        IResourceBuilder<KurrentDBResource> WithStartScavengeCommand(KurrentDBServerResource server) {
            builder.WithCommand(
                name: "start-scavenge",
                displayName: "Start Scavenge",
                executeCommand: async context => {
                    await using var client = await CreateClient(context, server);
                    var             result = await client.Admin.StartScavenge(context.CancellationToken);

                    var message = result.Status switch {
                        ScavengeResponseStatus.AlreadyRunning => $"Scavenge {result.ScavengeId} is already running.",
                        _                                     => $"Scavenge {result.ScavengeId} started."
                    };

                    return CommandResults.Success(message, result.ScavengeId);
                },
                commandOptions: new() {
                    Description         = "Starts a scavenge operation on this node",
                    IconName            = "ArrowClockwise",
                    IconVariant         = IconVariant.Filled,
                    ConfirmationMessage = "Start scavenge on this node?",
                    UpdateState         = HealthyOnly
                });

            return builder;
        }

        IResourceBuilder<KurrentDBResource> WithStopScavengeCommand(KurrentDBServerResource server) {
            builder.WithCommand(
                name: "stop-scavenge",
                displayName: "Stop Scavenge",
                executeCommand: async context => {
                    await using var client = await CreateClient(context, server);

                    try {
                        var result = await client.Admin.StopCurrentScavenge(context.CancellationToken);
                        return CommandResults.Success(
                            $"Stop requested for scavenge {result.ScavengeId}.",
                            result.ScavengeId);
                    }
                    catch (InvalidOperationException) {
                        return CommandResults.Success(
                            "No scavenge is currently running.",
                            "");
                    }
                },
                commandOptions: new() {
                    Description         = "Stops the currently running scavenge",
                    IconName            = "Stop",
                    IconVariant         = IconVariant.Filled,
                    ConfirmationMessage = "Stop the running scavenge?",
                    UpdateState         = HealthyOnly
                });

            return builder;
        }

        IResourceBuilder<KurrentDBResource> WithScavengeStatusCommand(KurrentDBServerResource server) {
            builder.WithCommand(
                name: "scavenge-status",
                displayName: "Scavenge Status",
                executeCommand: async context => {
                    await using var client = await CreateClient(context, server);
                    var             latest = await client.Admin.GetLatestScavenge(context.CancellationToken);

                    if (latest is null)
                        return CommandResults.Success("No scavenges found.", "");

                    var md = FormatScavengeSummary(latest);

                    return CommandResults.Success(
                        message: $"Scavenge {latest.ScavengeId}: {latest.State}",
                        value: new CommandResultData {
                            Value              = md,
                            Format             = CommandResultFormat.Markdown,
                            DisplayImmediately = true
                        });
                },
                commandOptions: new() {
                    Description = "Shows the latest scavenge status",
                    IconName    = "Info",
                    IconVariant = IconVariant.Regular,
                    UpdateState = HealthyOnly
                });

            return builder;
        
            static string FormatScavengeSummary(ScavengeSummary scavenge) {
                var spaceSaved = FormatBytes(scavenge.SpaceSaved);
                var completed  = scavenge.CompletedAt == default ? "—" : scavenge.CompletedAt.ToString("u");

                return $"""
                    ## Scavenge {scavenge.ScavengeId}

                    | Field | Value |
                    |-------|-------|
                    | **State** | {scavenge.State} |
                    | **Node** | {scavenge.NodeEndpoint} |
                    | **Started** | {scavenge.StartedAt:u} |
                    | **Completed** | {completed} |
                    | **Duration** | {scavenge.Duration} |
                    | **Space Saved** | {spaceSaved} |
                    """;
                
                static string FormatBytes(long bytes) => bytes switch {
                    >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
                    >= 1L << 20 => $"{bytes / (double)(1L << 20):F2} MB",
                    >= 1L << 10 => $"{bytes / (double)(1L << 10):F2} KB",
                    _           => $"{bytes} B"
                };
            }
        }

        IResourceBuilder<KurrentDBResource> WithResignNodeCommand(KurrentDBServerResource server) {
            builder.WithCommand(
                name: "resign-node",
                displayName: "Resign Node",
                executeCommand: async context => {
                    await using var client = await CreateClient(context, server);
                    await client.Admin.ResignNode(context.CancellationToken);
                    return CommandResults.Success("Node resigned from leader role.", "");
                },
                commandOptions: new() {
                    Description         = "Resigns this node from its leader role",
                    IconName            = "ArrowExit",
                    IconVariant         = IconVariant.Regular,
                    ConfirmationMessage = "Resign this node from leader role?",
                    UpdateState         = HealthyOnly
                });

            return builder;
        }

        IResourceBuilder<KurrentDBResource> WithStopNodeCommand(KurrentDBServerResource server) {
            builder.WithCommand(
                name: "stop-node",
                displayName: "Stop Node",
                executeCommand: async context => {
                    await using var client = await CreateClient(context, server);
                    await client.Admin.ShutdownServer(context.CancellationToken);
                    return CommandResults.Success("Node shutdown initiated.", "");
                },
                commandOptions: new() {
                    Description         = "Shuts down this KurrentDB node",
                    IconName            = "Power",
                    IconVariant         = IconVariant.Filled,
                    ConfirmationMessage = "This will shut down the KurrentDB node. Are you sure?",
                    UpdateState         = HealthyOnly
                });

            return builder;
        }
    }

    static async ValueTask<KurrentClient> CreateClient(ExecuteCommandContext context, KurrentDBServerResource server) {
        var subscriber       = context.ServiceProvider.GetRequiredService<ResourceConnectionProvider>();
        var loggerFactory    = context.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var connectionString = await subscriber.GetConnectionStringAsync(context.ResourceName);
        
        return KurrentClient.Create(connectionString, opts => {
            opts.ConnectionName = context.ResourceName;
            opts.Resilience     = KurrentClientResilienceOptions.NoResilience;
            opts.LoggerFactory  = loggerFactory;
        });
    }

    static ResourceCommandState HealthyOnly(UpdateCommandStateContext context) =>
        context.ResourceSnapshot.HealthStatus is HealthStatus.Healthy
            ? ResourceCommandState.Enabled
            : ResourceCommandState.Disabled;

}
