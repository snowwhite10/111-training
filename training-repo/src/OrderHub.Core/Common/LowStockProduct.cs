namespace OrderHub.Core.Common;

public record LowStockProduct(string Sku, string Name, int StockQuantity, int SoldQuantityLast30Days);
