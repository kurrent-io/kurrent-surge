using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Client;

sealed class KurrentBuilder(IServiceCollection services) : IKurrentBuilder {
    public IServiceCollection Services { get; } = services;
}
