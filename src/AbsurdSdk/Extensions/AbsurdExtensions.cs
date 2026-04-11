// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using AbsurdSdk.Core;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AbsurdSdk.Extensions;

public static class AbsurdExtensions
{
    /// <summary>
    /// Extension method to register a job with the Absurd client using its static metadata and dependency injection.
    /// </summary>
    public static void UseJob<TJob, TParams, TResult>(this IAbsurd client, IServiceProvider provider, Action<TaskRegistrationOptions>? configure = null)
        where TJob : class, IJob<TParams, TResult>
    {
        // Access static metadata directly (gets the default instance)
        TaskRegistrationOptions options = TJob.Options;

        // If there's any additional configuration, apply it
        if (configure != null)
        {
            configure(options);
        }

        client.RegisterTask(options, async (ctx, jsonParams) =>
        {
            using IServiceScope scope = provider.CreateScope();

            TJob job = scope.ServiceProvider.GetRequiredService<TJob>();

            TParams? typedParams = default;

            if (jsonParams is not null)
            {
                typedParams = jsonParams.Deserialize<TParams>();
            }

            var result = await job.ExecuteAsync(ctx, typedParams!);

            return (object)result!;
        });
    }
}