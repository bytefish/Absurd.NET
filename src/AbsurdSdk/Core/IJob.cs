// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AbsurdSdk.Core;

public interface IJob<TParams, TResult>
{
    Task<TResult> ExecuteAsync(TaskContext ctx, TParams args);
}
