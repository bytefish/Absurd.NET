// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AbsurdSdk.Sample.Models;

public class ShippingResult
{
    public required string TrackingNumber { get; set; }

    public required DateTime EstimatedDelivery { get; set; }
}
