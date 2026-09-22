using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Toolkit;

/// <summary>
/// <see cref="IServiceCollection"/> helpers that return whether a registration actually
/// occurred — the signal missing from stock <c>TryAdd*</c> overloads.
/// </summary>
/// <remarks>
/// Dedup matches <c>TryAdd</c>: one pass comparing <c>(ServiceType, ServiceKey)</c>, so keyed
/// and non-keyed registrations are distinct. Defaults to <see cref="ServiceLifetime.Singleton"/>.
/// Activated variants use <c>Microsoft.Extensions.DependencyInjection.AutoActivation</c> to force
/// instantiation at host startup — Singleton only.
/// </remarks>
static class ServiceCollectionExtensions {
    extension(IServiceCollection services) {
        #region ->> Registration Checks <<-

        public bool IsRegistered<T>() =>
            services.IsRegistered(typeof(T), serviceKey: null);

        public bool IsRegistered<T>(object? serviceKey) =>
            services.IsRegistered(typeof(T), serviceKey);

        public bool IsRegistered(Type serviceType, object? serviceKey = null) {
            var count = services.Count;
            for (var i = 0; i < count; i++) {
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
        /// Registers <typeparamref name="T"/> as its own service type unless already registered.
        /// </summary>
        public bool TryRegister<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        ) where T : class =>
            services.TryAdd(ServiceDescriptor.Describe(typeof(T), typeof(T), lifetime));

        /// <summary>
        /// Registers <typeparamref name="TImplementation"/> as <typeparamref name="TService"/>
        /// unless already registered.
        /// </summary>
        public bool TryRegister<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
            where TService : class
            where TImplementation : class, TService =>
            services.TryAdd(ServiceDescriptor.Describe(typeof(TService), typeof(TImplementation), lifetime));

        /// <summary>
        /// Registers a factory-backed <typeparamref name="T"/> unless already registered.
        /// </summary>
        public bool TryRegister<T>(
            Func<IServiceProvider, T> implementationFactory,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        ) where T : class =>
            services.TryAdd(ServiceDescriptor.Describe(typeof(T), implementationFactory, lifetime));

        /// <summary>
        /// Registers a pre-built instance as a singleton unless already registered.
        /// </summary>
        public bool TryRegisterInstance<T>(T instance)
            where T : class =>
            services.TryAdd(new ServiceDescriptor(typeof(T), instance));

        #endregion

        #region ->> TryRegisterKeyed <<-

        /// <summary>
        /// Registers <typeparamref name="T"/> as its own service type under
        /// <paramref name="serviceKey"/> unless that (type, key) pair is already registered.
        /// </summary>
        public bool TryRegisterKeyed<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
            object? serviceKey,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        ) where T : class =>
            services.TryAdd(ServiceDescriptor.DescribeKeyed(typeof(T), serviceKey, typeof(T), lifetime));

        /// <summary>
        /// Registers <typeparamref name="TImplementation"/> as <typeparamref name="TService"/> under
        /// <paramref name="serviceKey"/> unless that (type, key) pair is already registered.
        /// </summary>
        public bool TryRegisterKeyed<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            object? serviceKey,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        )
            where TService : class
            where TImplementation : class, TService =>
            services.TryAdd(ServiceDescriptor.DescribeKeyed(typeof(TService), serviceKey, typeof(TImplementation), lifetime));

        /// <summary>
        /// Registers a factory-backed <typeparamref name="T"/> under <paramref name="serviceKey"/>
        /// unless that (type, key) pair is already registered.
        /// </summary>
        public bool TryRegisterKeyed<T>(
            object? serviceKey,
            Func<IServiceProvider, object?, T> implementationFactory,
            ServiceLifetime lifetime = ServiceLifetime.Singleton
        ) where T : class =>
            services.TryAdd(ServiceDescriptor.DescribeKeyed(typeof(T), serviceKey, implementationFactory, lifetime));

        /// <summary>
        /// Registers a pre-built instance as a keyed singleton unless that (type, key) pair
        /// is already registered.
        /// </summary>
        public bool TryRegisterKeyedInstance<T>(object? serviceKey, T instance)
            where T : class =>
            services.TryAdd(new ServiceDescriptor(typeof(T), serviceKey, instance));

        #endregion

        #region ->> TryRegisterActivated (non-keyed) <<-

        /// <summary>
        /// Registers <typeparamref name="T"/> as a singleton activated at host startup.
        /// </summary>
        public bool TryRegisterActivated<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class {
            if (!services.TryRegister<T>())
                return false;

            services.ActivateSingleton<T>();
            return true;
        }

        /// <summary>
        /// Registers <typeparamref name="TImplementation"/> as <typeparamref name="TService"/>,
        /// activated at host startup.
        /// </summary>
        public bool TryRegisterActivated<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>()
            where TService : class
            where TImplementation : class, TService {
            if (!services.TryRegister<TService, TImplementation>())
                return false;

            services.ActivateSingleton<TService>();
            return true;
        }

        /// <summary>
        /// Registers a factory-backed <typeparamref name="T"/> as a singleton activated at host startup.
        /// </summary>
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

        /// <summary>
        /// Registers <typeparamref name="T"/> as a keyed singleton activated at host startup.
        /// </summary>
        public bool TryRegisterKeyedActivated<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(object? serviceKey)
            where T : class {
            if (!services.TryRegisterKeyed<T>(serviceKey))
                return false;

            services.ActivateKeyedSingleton<T>(serviceKey);
            return true;
        }

        /// <summary>
        /// Registers <typeparamref name="TImplementation"/> as a keyed <typeparamref name="TService"/>,
        /// activated at host startup.
        /// </summary>
        public bool TryRegisterKeyedActivated<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            object? serviceKey
        )
            where TService : class
            where TImplementation : class, TService {
            if (!services.TryRegisterKeyed<TService, TImplementation>(serviceKey))
                return false;

            services.ActivateKeyedSingleton<TService>(serviceKey);
            return true;
        }

        /// <summary>
        /// Registers a factory-backed <typeparamref name="T"/> as a keyed singleton activated
        /// at host startup.
        /// </summary>
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

        #region ->> Core primitive <<-

        /// <summary>
        /// Dedups on <c>(ServiceType, ServiceKey)</c>, adds otherwise. Returns <c>true</c> iff
        /// the descriptor was added by this call.
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
