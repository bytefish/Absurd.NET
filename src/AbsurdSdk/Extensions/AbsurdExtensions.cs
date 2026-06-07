// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using AbsurdSdk.Core;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AbsurdSdk.Extensions;

public static class AbsurdExtensions
{
    // Die elegante Erweiterung, die Dependency Injection mit dem Core-Client verheiratet
    public static void UseJob<TJob, TParams, TResult>(
        this IAbsurd client,
        IServiceProvider provider,
        string jobName,
        int defaultMaxAttempts = 5,
        CancellationPolicy? defaultCancellation = null)
        where TJob : class, IJob<TParams, TResult>
    {
        var options = new TaskRegistrationOptions
        {
            Name = jobName,
            DefaultMaxAttempts = defaultMaxAttempts,
            DefaultCancellation = defaultCancellation
        };

        client.RegisterTask(options, async (ctx, jsonParams) =>
        {
            using IServiceScope scope = provider.CreateScope();
            TJob job = scope.ServiceProvider.GetRequiredService<TJob>();

            TParams? typedParams = default;
            if (jsonParams is not null)
            {
                typedParams = jsonParams.Deserialize<TParams>(new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }

            var result = await job.ExecuteAsync(ctx, typedParams!);
            return (object)result!;
        });
    }
}