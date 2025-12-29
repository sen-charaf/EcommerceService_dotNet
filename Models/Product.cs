namespace Ecommerce.Models
{
    public class Product:BaseEntity
    {

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public int Stock { get; set; }

        public string? ImageUrl { get; set; }

        public int CategoryId { get; set; }

        // Navigation Properties
        public Category Category { get; set; } = null!;

        // Helper Properties
        public bool IsInStock => Stock > 0;
    }
}
