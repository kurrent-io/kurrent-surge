using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Processors.Hosting;

/// <summary>
/// <see cref="IServiceCollection"/> helpers that return a <see cref="bool"/> indicating
/// whether a registration actually occurred — the signal missing from stock
/// <c>TryAddSingleton</c>/<c>TryAddScoped</c>/<c>TryAddTransient</c> overloads.
/// </summary>
/// <remarks>
/// <para>
/// All <c>TryRegister*</c> helpers default to <see cref="ServiceLifetime.Singleton"/>.
/// The dedup rule mirrors <see cref="ServiceCollectionDescriptorExtensions.TryAdd(IServiceCollection, ServiceDescriptor)"/>:
/// a single O(n) pass comparing <c>(ServiceType, ServiceKey)</c>. Keyed and non-keyed
/// registrations are therefore distinct entries.
/// </para>
/// <para>
/// Activated variants piggyback on
/// <c>Microsoft.Extensions.DependencyInjection.AutoActivation</c> to force instantiation
/// at host startup via <c>AutoActivationHostedService</c>. Only the Singleton lifetime
/// is supported for activation; the stock package exposes no activation path for
/// <see cref="ServiceLifetime.Scoped"/> or <see cref="ServiceLifetime.Transient"/>.
/// </para>
/// </remarks>
static class ServiceCollectionExtensions {
    extension(IServiceCollection services) {
        #region ->> Registration Checks <<-

        public bool IsRegistered<T>() =>
            services.IsRegistered(typeof(T), serviceKey: null);

        public bool IsRegistered<T>(object? serviceKey) =>
            services.IsRegistered(typeof(T), serviceKey);

        public bool IsRegistered(Type serviceType, object? serviceKey = null) {
            int count = services.Count;
            for (int i = 0; i < count; i++) {
                var d = services[i];
                if (d.ServiceType == serviceType && Equals(d.ServiceKey, serviceKey))
                    return true;
            }
            return false;
        }

        public bool IsRegistered(ServiceDescriptor descriptor) =>
            services.IsRegistered(descriptor.ServiceType, descriptor.ServiceKey);

        #endregion

        #region ->> TryRegister (non-keyed) <<-

        /// <summary>
        /// Registers <typeparamref name="T"/> as its own service type if no non-keyed
        /// registration of <typeparamref name="T"/> already exists.
        /// </summary>
        public bool TryRegister<T>(
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        ) where T : class =>
            services.TryAdd(ServiceDescriptor.Describe(typeof(T), typeof(T), lifetime));

        /// <summary>
        /// Registers <typeparamref name="TImplementation"/> as <typeparamref name="TService"/>
        /// if no non-keyed registration of <typeparamref name="TService"/> already exists.
        /// </summary>
        public bool TryRegister<TService, TImplementation>(
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
            where TService : class
            where TImplementation : class, TService =>
            services.TryAdd(ServiceDescriptor.Describe(typeof(TService), typeof(TImplementation), lifetime));

        /// <summary>
        /// Registers a factory-backed <typeparamref name="T"/> if no non-keyed registration
        /// of <typeparamref name="T"/> already exists.
        /// </summary>
        public bool TryRegister<T>(
            Func<IServiceProvider, T> implementationFactory,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        ) where T : class =>
            services.TryAdd(ServiceDescriptor.Describe(typeof(T), implementationFactory, lifetime));

        /// <summary>
        /// Registers a pre-built instance as a singleton if no non-keyed registration of
        /// <typeparamref name="T"/> already exists. Instance-backed registrations only make
        /// sense for <see cref="ServiceLifetime.Singleton"/>.
        /// </summary>
        public bool TryRegisterInstance<T>(T instance)
            where T : class =>
            services.TryAdd(new ServiceDescriptor(typeof(T), instance));

        #endregion

        #region ->> TryRegisterKeyed <<-

        /// <summary>
        /// Registers <typeparamref name="T"/> as its own service type under <paramref name="serviceKey"/>
        /// if no registration for that (type, key) pair already exists.
        /// </summary>
        public bool TryRegisterKeyed<T>(
            object? serviceKey,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        ) where T : class =>
            services.TryAdd(ServiceDescriptor.DescribeKeyed(typeof(T), serviceKey, typeof(T), lifetime));

        /// <summary>
        /// Registers <typeparamref name="TImplementation"/> as <typeparamref name="TService"/> under
        /// <paramref name="serviceKey"/> if no registration for that (type, key) pair already exists.
        /// </summary>
        public bool TryRegisterKeyed<TService, TImplementation>(
            object? serviceKey,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
            where TService : class
            where TImplementation : class, TService =>
            services.TryAdd(ServiceDescriptor.DescribeKeyed(typeof(TService), serviceKey, typeof(TImplementation), lifetime));

        /// <summary>
        /// Registers a factory-backed <typeparamref name="T"/> under <paramref name="serviceKey"/>
        /// if no registration for that (type, key) pair already exists.
        /// </summary>
        public bool TryRegisterKeyed<T>(
            object? serviceKey,
            Func<IServiceProvider, object?, T> implementationFactory,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        ) where T : class =>
            services.TryAdd(ServiceDescriptor.DescribeKeyed(typeof(T), serviceKey, implementationFactory, lifetime));

        /// <summary>
        /// Registers a pre-built instance as a keyed singleton if no registration for
        /// <typeparamref name="T"/> under <paramref name="serviceKey"/> already exists.
        /// </summary>
        public bool TryRegisterKeyedInstance<T>(object? serviceKey, T instance)
            where T : class =>
            services.TryAdd(new ServiceDescriptor(typeof(T), serviceKey, instance));

        #endregion

        #region ->> TryRegisterActivated (non-keyed) <<-

        /// <summary>
        /// Registers <typeparamref name="T"/> as a singleton and marks it for auto-activation
        /// at host startup. Only Singleton lifetime is supported.
        /// </summary>
        public bool TryRegisterActivated<T>() where T : class {
            if (!services.TryRegister<T>())
                return false;

            services.ActivateSingleton<T>();
            return true;
        }

        public bool TryRegisterActivated<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService {
            if (!services.TryRegister<TService, TImplementation>())
                return false;

            services.ActivateSingleton<TService>();
            return true;
        }

        public bool TryRegisterActivated<T>(
            Func<IServiceProvider, T> implementationFactory
        ) where T : class {
            if (!services.TryRegister(implementationFactory))
                return false;

            services.ActivateSingleton<T>();
            return true;
        }

        #endregion

        #region ->> TryRegisterKeyedActivated <<-

        public bool TryRegisterKeyedActivated<T>(object? serviceKey)
            where T : class {
            if (!services.TryRegisterKeyed<T>(serviceKey))
                return false;

            services.ActivateKeyedSingleton<T>(serviceKey);
            return true;
        }

        public bool TryRegisterKeyedActivated<TService, TImplementation>(
            object? serviceKey
        )
            where TService : class
            where TImplementation : class, TService {
            if (!services.TryRegisterKeyed<TService, TImplementation>(serviceKey))
                return false;

            services.ActivateKeyedSingleton<TService>(serviceKey);
            return true;
        }

        public bool TryRegisterKeyedActivated<T>(
            object? serviceKey,
            Func<IServiceProvider, object?, T> implementationFactory
        ) where T : class {
            if (!services.TryRegisterKeyed(serviceKey, implementationFactory))
                return false;

            services.ActivateKeyedSingleton<T>(serviceKey);
            return true;
        }

        #endregion

        #region ->> TryRegisterHostedService <<-

        /// <summary>
        /// Registers <typeparamref name="THostedService"/> as an <see cref="IHostedService"/>,
        /// returning <c>true</c> when this call performed the registration. Unlike
        /// <see cref="ServiceCollectionHostedServiceExtensions.AddHostedService{THostedService}(IServiceCollection)"/>,
        /// this does not go through <c>TryAddEnumerable</c>'s <c>(ServiceType, ImplementationType)</c> dedup;
        /// the concrete hosted-service type is the dedup key.
        /// </summary>
        public bool TryRegisterHostedService<THostedService>()
            where THostedService : class, IHostedService {
            if (services.IsRegistered<THostedService>())
                return false;

            services.AddSingleton<THostedService>();
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<THostedService>());
            return true;
        }

        public bool TryRegisterHostedService<THostedService>(
            Func<IServiceProvider, THostedService> factory
        ) where THostedService : class, IHostedService {
            if (services.IsRegistered<THostedService>())
                return false;

            services.AddSingleton(factory);
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<THostedService>());
            return true;
        }

        #endregion

        #region ->> Core primitive <<-

        /// <summary>
        /// Dedups on <c>(ServiceType, ServiceKey)</c> via <see cref="IsRegistered(ServiceDescriptor)"/>,
        /// adds otherwise. Returns <c>true</c> iff the descriptor was added by this call.
        /// </summary>
        bool TryAdd(ServiceDescriptor descriptor) {
            if (services.IsRegistered(descriptor))
                return false;

            services.Add(descriptor);
            return true;
        }

        #endregion
    }
}
