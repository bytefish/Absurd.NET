// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AbsurdSdk.Exceptions;

public class CancelledTaskException : Exception
{
    public CancelledTaskException() : base("Task cancelled") { }
}
