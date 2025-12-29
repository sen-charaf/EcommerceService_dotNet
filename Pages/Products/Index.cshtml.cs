using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Data;
using Ecommerce.Models;
using Ecommerce.Services;

namespace Ecommerce.Pages.Products
{
    public class IndexModel : PageModel
    {
        private readonly ProductContext _context;
        private readonly CartService _cartService;

        public IndexModel(ProductContext context, CartService cartService)
        {
            _context = context;
            _cartService = cartService;
        }

        public IList<Product> Product { get; set; } = default!;

        public async Task OnGetAsync()
        {
            Product = await _context.Products
                .Include(p => p.Category)
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostAddToCartAsync(int productId, string productName)
        {
            // Get product from database
            var product = await _context.Products.FindAsync(productId);

            if (product == null)
            {
                TempData["CartMessage"] = "❌ Product not found.";
                return RedirectToPage();
            }

            if (product.Stock <= 0)
            {
                TempData["CartMessage"] = $"❌ {product.Name} is out of stock.";
                return RedirectToPage();
            }

            // Check if already in cart and at stock limit
            var cart = _cartService.GetCart();
            var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == productId);

            if (existingItem != null && existingItem.Quantity >= product.Stock)
            {
                TempData["CartMessage"] = $"❌ Cannot add more. Only {product.Stock} units available.";
                return RedirectToPage();
            }

            // Add to cart
            _cartService.AddToCart(
                productId: product.Id,
                productName: product.Name,
                price: product.Price,
                quantity: 1,
                imageUrl: product.ImageUrl
            );

            TempData["CartMessage"] = $"✅ {product.Name} added to cart!";
            return RedirectToPage();
        }
    }
}