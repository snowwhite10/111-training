using ModelContextProtocol.Server;
using OrderHub.Core.Domain;
using OrderHub.Core.Services;
using System.ComponentModel;

[McpServerResourceType]
public class OrderHubResources(IOrderService orderService)
{
    [McpServerResource(UriTemplate = "orderhub://discount-rules",
        Name = "會員折扣規則", MimeType = "text/markdown")]
    [Description("目前生效的會員折扣規則與計算方式")]
    public string DiscountRules()
    {
        var lines = Enum.GetValues<CustomerTier>()
            .Select(tier => $"- {tier}：{FormatRate(orderService.GetDiscountRate(tier))}");

        return $"""
            # OrderHub 會員折扣規則
            {string.Join("\n", lines)}
            折扣在訂單總額上折抵一次，單價快照（UnitPriceSnapshot）為下單當下原價。
            """;
    }

    // 折扣規則直接讀 OrderService.GetDiscountRate,規則改版時只要改那一處
    private static string FormatRate(decimal discountRate)
    {
        if (discountRate == 0)
            return "不打折";

        var percentagePaid = (1 - discountRate) * 100;
        return percentagePaid % 10 == 0
            ? $"{percentagePaid / 10:0.#} 折"
            : $"{percentagePaid:0.#} 折";
    }
}
