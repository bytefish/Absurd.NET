// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AbsurdSdk.Exceptions;

public class TimeoutErrorException : Exception
{
    public TimeoutErrorException(string message) : base(message) { }
}
