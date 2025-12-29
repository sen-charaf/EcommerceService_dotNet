using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Data;
using Ecommerce.Models;
using Ecommerce.Services;

namespace Ecommerce.Pages.Products
{
    public class DetailsModel : PageModel
    {
        private readonly ProductContext _context;
        private readonly CartService _cartService;
        private readonly RedisCacheService _redis;

        public DetailsModel(ProductContext context, CartService cartService, RedisCacheService redis)
        {
            _context = context;
            _cartService = cartService;
            _redis = redis;
        }

        public Product Product { get; set; } = default!;
        public long ViewCount { get; set; }

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            // Try to get product from Redis cache first
            var cachedProduct = await _redis.GetCachedProductAsync<Product>(id.Value);

            if (cachedProduct != null)
            {
                Product = cachedProduct;
            }
            else
            {
                // Get from database if not in cache
                var product = await _context.Products
                    .Include(p => p.Category)
                    .FirstOrDefaultAsync(m => m.Id == id);

                if (product == null)
                {
                    return NotFound();
                }

                Product = product;

                // Cache the product for 1 hour
                await _redis.CacheProductAsync(id.Value, product, TimeSpan.FromHours(1));
            }

            // Track product view in Redis
            await _redis.IncrementProductViewAsync(id.Value);

            // Get view count for display
            ViewCount = await _redis.GetProductViewCountAsync(id.Value);

            return Page();
        }

        public async Task<IActionResult> OnPostAddToCartAsync(int productId, int quantity = 1)
        {
            // Get product from database
            var product = await _context.Products.FindAsync(productId);

            if (product == null)
            {
                TempData["ErrorMessage"] = "Product not found.";
                return RedirectToPage(new { id = productId });
            }

            if (product.Stock <= 0)
            {
                TempData["ErrorMessage"] = $"{product.Name} is out of stock.";
                return RedirectToPage(new { id = productId });
            }

            // Check if trying to add more than available stock
            var cart = _cartService.GetCart();
            var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == productId);
            var currentQuantityInCart = existingItem?.Quantity ?? 0;

            if (currentQuantityInCart + quantity > product.Stock)
            {
                TempData["ErrorMessage"] = $"Cannot add {quantity} more. Only {product.Stock - currentQuantityInCart} units available.";
                return RedirectToPage(new { id = productId });
            }

            // Add to cart
            _cartService.AddToCart(
                productId: product.Id,
                productName: product.Name,
                price: product.Price,
                quantity: quantity,
                imageUrl: product.ImageUrl
            );

            TempData["SuccessMessage"] = $"✅ {quantity} x {product.Name} added to cart!";
            return RedirectToPage(new { id = productId });
        }
    }
}