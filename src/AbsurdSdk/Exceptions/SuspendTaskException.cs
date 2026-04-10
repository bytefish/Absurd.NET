// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AbsurdSdk.Exceptions;

public class SuspendTaskException : Exception
{
    public SuspendTaskException() : base("Task suspended") { }
}
