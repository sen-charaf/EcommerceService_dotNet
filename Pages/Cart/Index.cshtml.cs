using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Data;
using Ecommerce.Models;
using Ecommerce.Services;

namespace Ecommerce.Pages.Cart
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

        // Properties
        public Models.Cart Cart { get; set; } = new();
        public List<Product> Products { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            // Get cart from session
            Cart = _cartService.GetCart();

            // Load product details from database
            if (Cart.Items.Any())
            {
                var productIds = Cart.Items.Select(i => i.ProductId).ToList();
                Products = await _context.Products
                    .Include(p => p.Category)
                    .Where(p => productIds.Contains(p.Id))
                    .ToListAsync();
            }

            return Page();
        }

        public async Task<IActionResult> OnPostUpdateQuantityAsync(int productId, int quantity)
        {
            if (quantity < 1)
            {
                TempData["ErrorMessage"] = "Quantity must be at least 1.";
                return RedirectToPage();
            }

            // Get product to check stock
            var product = await _context.Products.FindAsync(productId);
            if (product == null)
            {
                TempData["ErrorMessage"] = "Product not found.";
                return RedirectToPage();
            }

            // Check stock availability
            if (quantity > product.Stock)
            {
                TempData["ErrorMessage"] = $"Sorry, only {product.Stock} units available.";
                return RedirectToPage();
            }

            // Update cart
            _cartService.UpdateQuantity(productId, quantity);

            TempData["SuccessMessage"] = "Cart updated successfully!";
            return RedirectToPage();
        }

        public IActionResult OnPostRemoveItem(int productId)
        {
            _cartService.RemoveItem(productId);
            TempData["SuccessMessage"] = "Item removed from cart.";
            return RedirectToPage();
        }

        public IActionResult OnPostClearCart()
        {
            _cartService.ClearCart();
            TempData["SuccessMessage"] = "Cart cleared successfully!";
            return RedirectToPage();
        }
    }
}