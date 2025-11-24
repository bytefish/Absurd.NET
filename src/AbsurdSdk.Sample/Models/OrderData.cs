// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AbsurdSdk.Sample.Models;

public class OrderData
{
    public required string OrderId { get; set; }

    public string CustomerEmail { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public List<string> Items { get; set; } = new();
}
