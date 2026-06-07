// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using AbsurdSdk.Configuration;
using AbsurdSdk.Core;
using AbsurdSdk.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AbsurdSdk.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAbsurdWorker(this IServiceCollection services, string queueName, Action<AbsurdWorkerBuilder> configure)
    {
        var registryDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(AbsurdRegistry));
        AbsurdRegistry registry;

        if (registryDescriptor == null)
        {
            registry = new AbsurdRegistry();
            services.AddSingleton(registry);
            services.AddTransient<IJobPublisher, AbsurdJobPublisher>();
        }
        else
        {
            registry = (AbsurdRegistry)registryDescriptor.ImplementationInstance!;
        }

        var workerConfig = new WorkerConfiguration { QueueName = queueName };

        registry.WorkerConfigs.Add(workerConfig);

        var builder = new AbsurdWorkerBuilder(services, registry, workerConfig);

        configure(builder);

        // One to One Pattern. One Worker per Queue. This simplifies the design and
        // avoids complexities of multiple workers consuming from the same queue.
        services.AddSingleton<IHostedService>(sp =>
        {
            return new AbsurdGenericWorker(
                client: sp.GetRequiredService<IAbsurd>(),
                provider: sp,
                registry: sp.GetRequiredService<AbsurdRegistry>(),
                logger: sp.GetRequiredService<ILogger<AbsurdGenericWorker>>(),
                queueName: queueName
            );
        });

        return services;
    }

    public static IServiceCollection AddAbsurdSdk(this IServiceCollection services, string connectionString)
    {
        // Register the Absurd Client as a Singleton since it manages its own
        // connection pooling and is thread-safe.
        services.AddSingleton<IAbsurd>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<Absurd>>();
            var dataSource = NpgsqlDataSource.Create(connectionString);
            return new Absurd(logger, dataSource);
        });

        // Register Publish Abstraction
        services.AddTransient<IEventPublisher, AbsurdEventPublisher>();

        return services;
    }
}