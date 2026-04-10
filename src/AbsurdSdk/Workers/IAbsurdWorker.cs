// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AbsurdSdk.Workers;

public interface IAbsurdWorker
{
    /// <summary>
    /// Closes the worker and releases any resources it is using. This should be called 
    /// when the worker is no longer needed, such as when shutting down the 
    /// application.
    /// </summary>
    Task CloseAsync();
}
