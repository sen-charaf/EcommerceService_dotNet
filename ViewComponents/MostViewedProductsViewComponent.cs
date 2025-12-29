using Ecommerce.Data;
using Ecommerce.Services;
using Ecommerce.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.ViewComponents
{
    public class MostViewedProductsViewComponent : ViewComponent
    {
        private readonly ProductContext _context;
        private readonly RedisCacheService _redis;

        public MostViewedProductsViewComponent(ProductContext context, RedisCacheService redis)
        {
            _context = context;
            _redis = redis;
        }

        public async Task<IViewComponentResult> InvokeAsync(int count = 5)
        {
            // Get most viewed product IDs from Redis
            var mostViewed = await _redis.GetMostViewedProductsAsync(count);

            if (!mostViewed.Any())
            {
                return View(new List<ProductWithViews>());
            }

            // Get product details from database
            var productIds = mostViewed.Select(x => x.ProductId).ToList();
            var products = await _context.Products
                .Include(p => p.Category)
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync();

            // Combine products with view counts
            var result = products.Select(p => new ProductWithViews
            {
                Product = p,
                ViewCount = mostViewed.First(x => x.ProductId == p.Id).ViewCount
            })
            .OrderByDescending(x => x.ViewCount)
            .ToList();

            return View(result);
        }
    }

    public class ProductWithViews
    {
        public Product Product { get; set; } = null!;
        public long ViewCount { get; set; }
    }
}