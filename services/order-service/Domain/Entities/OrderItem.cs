namespace OrderService.Domain.Entities;

// Mỗi dòng = 1 Key sẽ được cấp. Mua số lượng N => N dòng.
public sealed class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }

    // Logical FK -> Catalog.Product.Id (khác database nên không có ràng buộc thật)
    public Guid ProductId { get; set; }

    public decimal Price { get; set; }

    public Order? Order { get; set; }
}
