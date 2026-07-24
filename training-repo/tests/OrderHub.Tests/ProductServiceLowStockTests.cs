using OrderHub.Core.Domain;

namespace OrderHub.Tests;

public class ProductServiceLowStockTests
{
    [Fact]
    public async Task GetLowStock_FiltersByThresholdAndSortsAscending()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateProductService(db);
        TestSetup.AddProduct(db, sku: "SKU-A001", stock: 15);
        TestSetup.AddProduct(db, sku: "SKU-A002", stock: 3);
        TestSetup.AddProduct(db, sku: "SKU-A003", stock: 10);
        TestSetup.AddProduct(db, sku: "SKU-A004", stock: 8);

        var result = await service.GetLowStockAsync(10);

        Assert.Equal(new[] { "SKU-A002", "SKU-A004" }, result.Select(p => p.Sku));
    }

    [Fact]
    public async Task GetLowStock_ExcludesInactiveProducts()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateProductService(db);
        TestSetup.AddProduct(db, sku: "SKU-A001", stock: 1, isActive: false);
        TestSetup.AddProduct(db, sku: "SKU-A002", stock: 8);

        var result = await service.GetLowStockAsync(10);

        Assert.Single(result);
        Assert.Equal("SKU-A002", result[0].Sku);
    }

    [Fact]
    public async Task GetLowStock_SoldQuantity_ExcludesCancelledOrders()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateProductService(db);
        var customer = TestSetup.AddCustomer(db);
        var product = TestSetup.AddProduct(db, sku: "SKU-A001", stock: 5);

        db.Orders.Add(new Order
        {
            CustomerId = customer.Id,
            Status = OrderStatus.Confirmed,
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            Items = { new OrderItem { ProductId = product.Id, Quantity = 5, UnitPriceSnapshot = product.UnitPrice } }
        });
        db.Orders.Add(new Order
        {
            CustomerId = customer.Id,
            Status = OrderStatus.Cancelled,
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            Items = { new OrderItem { ProductId = product.Id, Quantity = 100, UnitPriceSnapshot = product.UnitPrice } }
        });
        db.SaveChanges();

        var result = await service.GetLowStockAsync(10);

        Assert.Equal(5, result.Single().SoldQuantityLast30Days);
    }

    [Fact]
    public async Task GetLowStock_SoldQuantity_ExcludesSalesOlderThan30Days()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateProductService(db);
        var customer = TestSetup.AddCustomer(db);
        var product = TestSetup.AddProduct(db, sku: "SKU-A001", stock: 5);

        db.Orders.Add(new Order
        {
            CustomerId = customer.Id,
            Status = OrderStatus.Confirmed,
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            Items = { new OrderItem { ProductId = product.Id, Quantity = 5, UnitPriceSnapshot = product.UnitPrice } }
        });
        db.Orders.Add(new Order
        {
            CustomerId = customer.Id,
            Status = OrderStatus.Confirmed,
            CreatedAt = DateTime.UtcNow.AddDays(-40),
            Items = { new OrderItem { ProductId = product.Id, Quantity = 50, UnitPriceSnapshot = product.UnitPrice } }
        });
        db.SaveChanges();

        var result = await service.GetLowStockAsync(10);

        Assert.Equal(5, result.Single().SoldQuantityLast30Days);
    }
}
