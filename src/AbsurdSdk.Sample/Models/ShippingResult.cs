// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AbsurdSdk.Sample.Models;

public class ShippingResult
{
    public string TrackingNumber { get; set; } = string.Empty;
    public DateTime EstimatedDelivery { get; set; }
}
