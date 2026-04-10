// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using AbsurdSdk.Core;
using Microsoft.Extensions.DependencyInjection;

namespace AbsurdSdk.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Extension method to register a job in the DI container. This allows the job to be resolved 
    /// and executed by the Absurd SDK.
    /// </summary>
    public static void RegisterJob<TJob, TParams, TResult>(this IServiceCollection services)
        where TJob : class, IJob<TParams, TResult>
    {
        services.AddTransient<TJob>();
    }
}