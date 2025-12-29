using Ecommerce.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Ecommerce.Services
{
    /// <summary>
    /// Cart service using BROWSER COOKIES (visible in DevTools)
    /// </summary>
    public class CartService
    {
        private const string CartCookieKey = "ShoppingCart";
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CartService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private HttpContext Context => _httpContextAccessor.HttpContext!;

        /// <summary>
        /// Get the current cart from COOKIES
        /// </summary>
        public Cart GetCart()
        {
            var cartCookie = Context.Request.Cookies[CartCookieKey];

            if (string.IsNullOrEmpty(cartCookie))
                return new Cart();

            try
            {
                return JsonSerializer.Deserialize<Cart>(cartCookie) ?? new Cart();
            }
            catch
            {
                return new Cart();
            }
        }

        /// <summary>
        /// Save cart to BROWSER COOKIES
        /// </summary>
        public void SaveCart(Cart cart)
        {
            var cartJson = JsonSerializer.Serialize(cart);

            var cookieOptions = new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(30),  // Cart expires in 30 days
                HttpOnly = false,  // ⭐ FALSE = Visible in DevTools!
                Secure = false,    // Set to true in production (HTTPS only)
                SameSite = SameSiteMode.Lax,
                IsEssential = true
            };

            Context.Response.Cookies.Append(CartCookieKey, cartJson, cookieOptions);
        }

        /// <summary>
        /// Add item to cart
        /// </summary>
        public void AddToCart(int productId, string productName, decimal price, int quantity = 1, string? imageUrl = null)
        {
            var cart = GetCart();
            cart.AddItem(productId, productName, price, quantity, imageUrl);
            SaveCart(cart);
        }

        /// <summary>
        /// Update item quantity
        /// </summary>
        public void UpdateQuantity(int productId, int quantity)
        {
            var cart = GetCart();
            cart.UpdateQuantity(productId, quantity);
            SaveCart(cart);
        }

        /// <summary>
        /// Remove item from cart
        /// </summary>
        public void RemoveItem(int productId)
        {
            var cart = GetCart();
            cart.RemoveItem(productId);
            SaveCart(cart);
        }

        /// <summary>
        /// Clear entire cart
        /// </summary>
        public void ClearCart()
        {
            Context.Response.Cookies.Delete(CartCookieKey);
        }

        /// <summary>
        /// Get total items count (for navbar badge)
        /// </summary>
        public int GetCartItemCount()
        {
            var cart = GetCart();
            return cart.TotalItems;
        }

        /// <summary>
        /// Get cart total amount
        /// </summary>
        public decimal GetCartTotal()
        {
            var cart = GetCart();
            return cart.Total;
        }
    }
}