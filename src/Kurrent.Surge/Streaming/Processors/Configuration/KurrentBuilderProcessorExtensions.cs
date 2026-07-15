using Kurrent.Surge.Client;
using Kurrent.Surge.Streaming;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Processors.Configuration;

/// <summary>
/// <see cref="IKurrentBuilder"/> extensions for registering Kurrent processors inside an
/// <c>AddKurrent</c> scope.
/// </summary>
/// <remarks>
/// <para>
/// All overloads delegate to the matching <c>AddKurrentProcessor</c> /
/// <c>AddKurrentBatchProcessor</c> <see cref="IServiceCollection"/> extensions —
/// registration semantics (first-wins dedup, options resolution, drone lifecycle)
/// are identical. See <see cref="KurrentProcessorServiceCollectionExtensions"/>.
/// </para>
/// <para>
/// Processors that do not call <c>opts.Connection(...)</c> inherit the scope's default
/// client options (registered via <c>AddClient</c>) before falling back to
/// <c>ConnectionStrings:{ProcessorId}</c> / <c>ConnectionStrings:kurrentdb</c>.
/// </para>
/// </remarks>
[PublicAPI]
public static class KurrentBuilderProcessorExtensions {
    /// <param name="builder">The Kurrent builder.</param>
    extension(IKurrentBuilder builder) {
        #region ->> Processor <<-

        /// <summary>
        /// Registers a processor as a hosted service. <typeparamref name="TProcessor"/> must be
        /// registered separately in DI — it is resolved at drone startup.
        /// </summary>
        public IKurrentBuilder AddProcessor<TProcessor>(Action<ProcessorOptions>? configure = null)
            where TProcessor : class, IRecordProcessor {
            builder.Services.AddKurrentProcessor<TProcessor>(configure);
            return builder;
        }

        /// <summary>
        /// Registers a processor as a hosted service with <see cref="IServiceProvider"/>-aware configure.
        /// <typeparamref name="TProcessor"/> must be registered separately in DI.
        /// </summary>
        public IKurrentBuilder AddProcessor<TProcessor>(Action<ProcessorOptions, IServiceProvider> configure)
            where TProcessor : class, IRecordProcessor {
            builder.Services.AddKurrentProcessor<TProcessor>(configure);
            return builder;
        }

        /// <summary>
        /// Registers a processor as a hosted service using a factory invoked once per drone at host startup.
        /// </summary>
        public IKurrentBuilder AddProcessor<TProcessor>(
            Func<IServiceProvider, TProcessor> processorFactory,
            Action<ProcessorOptions>? configure = null)
            where TProcessor : class, IRecordProcessor {
            builder.Services.AddKurrentProcessor(processorFactory, configure);
            return builder;
        }

        /// <summary>
        /// Registers a processor as a hosted service using a factory with
        /// <see cref="IServiceProvider"/>-aware configure.
        /// </summary>
        public IKurrentBuilder AddProcessor<TProcessor>(
            Func<IServiceProvider, TProcessor> processorFactory,
            Action<ProcessorOptions, IServiceProvider> configure)
            where TProcessor : class, IRecordProcessor {
            builder.Services.AddKurrentProcessor(processorFactory, configure);
            return builder;
        }

        /// <summary>
        /// Registers a processor as a hosted service using a pre-built instance.
        /// </summary>
        public IKurrentBuilder AddProcessor<TProcessor>(
            TProcessor instance,
            Action<ProcessorOptions>? configure = null)
            where TProcessor : class, IRecordProcessor {
            builder.Services.AddKurrentProcessor(instance, configure);
            return builder;
        }

        /// <summary>
        /// Registers a processor as a hosted service using a pre-built instance with
        /// <see cref="IServiceProvider"/>-aware configure.
        /// </summary>
        public IKurrentBuilder AddProcessor<TProcessor>(
            TProcessor instance,
            Action<ProcessorOptions, IServiceProvider> configure)
            where TProcessor : class, IRecordProcessor {
            builder.Services.AddKurrentProcessor(instance, configure);
            return builder;
        }

        #endregion

        #region ->> Batch Processor <<-

        /// <summary>
        /// Registers a batch processor as a hosted service. <typeparamref name="TProcessor"/> must be
        /// registered separately in DI — it is resolved at drone startup.
        /// </summary>
        public IKurrentBuilder AddBatchProcessor<TProcessor>(Action<ProcessorOptions>? configure = null)
            where TProcessor : class, IRecordBatchProcessor {
            builder.Services.AddKurrentBatchProcessor<TProcessor>(configure);
            return builder;
        }

        /// <summary>
        /// Registers a batch processor as a hosted service with <see cref="IServiceProvider"/>-aware configure.
        /// <typeparamref name="TProcessor"/> must be registered separately in DI.
        /// </summary>
        public IKurrentBuilder AddBatchProcessor<TProcessor>(Action<ProcessorOptions, IServiceProvider> configure)
            where TProcessor : class, IRecordBatchProcessor {
            builder.Services.AddKurrentBatchProcessor<TProcessor>(configure);
            return builder;
        }

        /// <summary>
        /// Registers a batch processor as a hosted service using a factory invoked once per drone at host startup.
        /// </summary>
        public IKurrentBuilder AddBatchProcessor<TProcessor>(
            Func<IServiceProvider, TProcessor> processorFactory,
            Action<ProcessorOptions>? configure = null)
            where TProcessor : class, IRecordBatchProcessor {
            builder.Services.AddKurrentBatchProcessor(processorFactory, configure);
            return builder;
        }

        /// <summary>
        /// Registers a batch processor as a hosted service using a factory with
        /// <see cref="IServiceProvider"/>-aware configure.
        /// </summary>
        public IKurrentBuilder AddBatchProcessor<TProcessor>(
            Func<IServiceProvider, TProcessor> processorFactory,
            Action<ProcessorOptions, IServiceProvider> configure)
            where TProcessor : class, IRecordBatchProcessor {
            builder.Services.AddKurrentBatchProcessor(processorFactory, configure);
            return builder;
        }

        /// <summary>
        /// Registers a batch processor as a hosted service using a pre-built instance.
        /// </summary>
        public IKurrentBuilder AddBatchProcessor<TProcessor>(
            TProcessor instance,
            Action<ProcessorOptions>? configure = null)
            where TProcessor : class, IRecordBatchProcessor {
            builder.Services.AddKurrentBatchProcessor(instance, configure);
            return builder;
        }

        /// <summary>
        /// Registers a batch processor as a hosted service using a pre-built instance with
        /// <see cref="IServiceProvider"/>-aware configure.
        /// </summary>
        public IKurrentBuilder AddBatchProcessor<TProcessor>(
            TProcessor instance,
            Action<ProcessorOptions, IServiceProvider> configure)
            where TProcessor : class, IRecordBatchProcessor {
            builder.Services.AddKurrentBatchProcessor(instance, configure);
            return builder;
        }

        #endregion
    }
}
