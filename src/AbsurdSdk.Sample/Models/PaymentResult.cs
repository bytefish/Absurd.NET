// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AbsurdSdk.Sample.Models;

public class PaymentResult
{
    public bool Success { get; set; }
    public string? TransactionId { get; set; }
    public string? ErrorMessage { get; set; }
}
