using Ecommerce.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace Ecommerce.Services
{
    public interface IChatService
    {
        Task<string> GetChatResponseAsync(string userMessage);
    }

    public class ChatService : IChatService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly KnowledgeBase _knowledgeBase;

        public ChatService(IConfiguration config, IHttpClientFactory httpClientFactory, ProductContext dbContext)
        {
            _httpClient = httpClientFactory.CreateClient();
            _apiKey = config["Groq:ApiKey"];
            _knowledgeBase = new KnowledgeBase(dbContext);

            // Set Groq API authorization header
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }

        public async Task<string> GetChatResponseAsync(string userMessage)
        {
            try
            {
                // Step 1: Retrieve relevant context from knowledge base (RAG)
                var relevantContext = await _knowledgeBase.GetRelevantContextAsync(userMessage);

                // Step 2: Build the RAG prompt for Groq
                var systemPrompt = @"You are a helpful customer service assistant for an electronics e-commerce shop.
You can ONLY answer questions about:
- Electronics products (laptops, phones, tablets, cameras, headphones, accessories, etc.)
- Product specifications, prices, and availability
- Shipping and delivery information
- Return and warranty policies
- Order status and payment methods

If the user asks about anything else (politics, medical advice, general knowledge, unrelated topics, etc.), 
politely redirect them to ask about our electronics products and services.

Here is the relevant information from our shop:
" + relevantContext;

                // Groq uses OpenAI-compatible chat format
                var payload = new
                {
                    model = "llama-3.3-70b-versatile", // Fast and accurate model
                    messages = new[]
                    {
                        new
                        {
                            role = "system",
                            content = systemPrompt
                        },
                        new
                        {
                            role = "user",
                            content = userMessage
                        }
                    },
                    temperature = 0.7,
                    max_tokens = 1024,
                    top_p = 1,
                    stream = false
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Groq API endpoint
                var response = await _httpClient.PostAsync(
                    "https://api.groq.com/openai/v1/chat/completions",
                    content
                );

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Groq API Error: {errorContent}");
                    return "Sorry, I'm having trouble connecting. Please try again.";
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<GroqResponse>(responseBody);

                var reply = result?.Choices?[0]?.Message?.Content
                    ?? "Sorry, I couldn't process your request.";

                return reply;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Chat error: {ex.Message}");
                return "An error occurred. Please try again later.";
            }
        }
    }

    // Knowledge Base with RAG functionality - LOADS FROM DATABASE
    public class KnowledgeBase
    {
        private readonly ProductContext _dbContext;
        private List<KnowledgeItem> _cachedItems;
        private DateTime _lastCacheUpdate;

        public KnowledgeBase(ProductContext dbContext)
        {
            _dbContext = dbContext;
            _cachedItems = new List<KnowledgeItem>();
            _lastCacheUpdate = DateTime.MinValue;
        }

        public async Task<string> GetRelevantContextAsync(string query)
        {
            // Refresh cache every 5 minutes
            if ((DateTime.Now - _lastCacheUpdate).TotalMinutes > 5)
            {
                await RefreshCacheAsync();
            }

            query = query.ToLower();
            var relevantItems = new List<KnowledgeItem>();

            // Find relevant items based on keywords
            foreach (var item in _cachedItems)
            {
                if (ContainsRelevantKeywords(query, item))
                {
                    relevantItems.Add(item);
                }
            }

            // If no specific match, return general info (policies)
            if (relevantItems.Count == 0)
            {
                relevantItems = _cachedItems.Where(i =>
                    i.Category == "Shipping" ||
                    i.Category == "Returns" ||
                    i.Category == "Warranty" ||
                    i.Category == "Support").ToList();
            }

            // Limit to top 10 most relevant items to avoid token limits
            relevantItems = relevantItems.Take(10).ToList();

            var context = string.Join("\n\n", relevantItems.Select(i => $"[{i.Category}] {i.Content}"));
            return context;
        }

        private async Task RefreshCacheAsync()
        {
            var items = new List<KnowledgeItem>();

            // Load products from database
            var products = await _dbContext.Products
                .Include(p => p.Category)
                .Where(p => p.Stock > 0) // Only active products
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Category,
                    p.Price,
                    p.Description,
                    p.Stock
                })
                .ToListAsync();

            foreach (var product in products)
            {
                var stockStatus = product.Stock > 0 ? "In stock" : "Out of stock";
                var content = $"{product.Name} - ${product.Price} - {product.Description}. {stockStatus}.";

                items.Add(new KnowledgeItem
                {
                    Category = product.Category?.Name ?? "Electronics",
                    Content = content,
                    Keywords = GenerateKeywords(product.Name, product.Category?.Name ?? "", product.Description)
                });
            }

            // Load static policies (you can also store these in DB)
            items.AddRange(await LoadPoliciesFromDatabaseAsync());

            _cachedItems = items;
            _lastCacheUpdate = DateTime.Now;
        }

        private async Task<List<KnowledgeItem>> LoadPoliciesFromDatabaseAsync()
        {
            var policies = new List<KnowledgeItem>();

            // Option 1: Load from a Settings/Policies table in your database
            // Uncomment if you have a ShopPolicies table
            /*
            var dbPolicies = await _dbContext.ShopPolicies
                .Where(p => p.IsActive)
                .ToListAsync();

            foreach (var policy in dbPolicies)
            {
                policies.Add(new KnowledgeItem
                {
                    Category = policy.Category,
                    Content = policy.Content,
                    Keywords = GenerateKeywords(policy.Category, policy.Content, "")
                });
            }
            */

            // Option 2: Fallback to hardcoded policies if DB is empty
            if (!policies.Any())
            {
                policies.AddRange(GetDefaultPolicies());
            }

            return await Task.FromResult(policies);
        }

        private List<KnowledgeItem> GetDefaultPolicies()
        {
            return new List<KnowledgeItem>
            {
                new KnowledgeItem
                {
                    Category = "Shipping",
                    Content = "Free shipping on orders over $50. Standard delivery 3-5 business days. Express shipping available for $15 (1-2 days).",
                    Keywords = new List<string> { "shipping", "delivery", "ship", "send", "freight", "transport" }
                },
                new KnowledgeItem
                {
                    Category = "Returns",
                    Content = "30-day return policy. Products must be unused and in original packaging. Full refund issued within 5-7 business days.",
                    Keywords = new List<string> { "return", "refund", "exchange", "money back", "send back" }
                },
                new KnowledgeItem
                {
                    Category = "Warranty",
                    Content = "All products include manufacturer's warranty. Extended warranty available for purchase: 1 year $49, 2 years $89.",
                    Keywords = new List<string> { "warranty", "guarantee", "protection", "coverage" }
                },
                new KnowledgeItem
                {
                    Category = "Payment",
                    Content = "We accept Visa, Mastercard, American Express, PayPal, and Apple Pay. Installment plans available through Affirm.",
                    Keywords = new List<string> { "payment", "pay", "credit card", "paypal", "visa", "mastercard" }
                },
                new KnowledgeItem
                {
                    Category = "Support",
                    Content = "Customer support available Mon-Fri 9AM-6PM EST. Email: support@electronicsshop.com, Phone: 1-800-TECH-HELP",
                    Keywords = new List<string> { "support", "help", "contact", "customer service", "assistance" }
                }
            };
        }

        private List<string> GenerateKeywords(string name, string category, string description)
        {
            var keywords = new List<string>();

            // Add words from name
            if (!string.IsNullOrWhiteSpace(name))
                keywords.AddRange(name.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries));

            // Add category
            if (!string.IsNullOrWhiteSpace(category))
                keywords.Add(category.ToLower());

            // Add important words from description
            if (!string.IsNullOrWhiteSpace(description))
            {
                var descWords = description.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                keywords.AddRange(descWords.Where(w => w.Length > 3).Take(5));
            }

            return keywords.Distinct().ToList();
        }

        private bool ContainsRelevantKeywords(string query, KnowledgeItem item)
        {
            var itemText = (item.Category + " " + item.Content).ToLower();

            // Check category match
            if (query.Contains(item.Category.ToLower())) return true;

            // Check keywords
            if (item.Keywords != null && item.Keywords.Any())
            {
                foreach (var keyword in item.Keywords)
                {
                    if (query.Contains(keyword.ToLower()))
                    {
                        return true;
                    }
                }
            }

            // Check content match
            var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in queryWords)
            {
                if (word.Length > 3 && itemText.Contains(word))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class KnowledgeItem
    {
        public string Category { get; set; }
        public string Content { get; set; }
        public List<string> Keywords { get; set; }
    }

    // Groq API Response Models (OpenAI-compatible format)
    public class GroqResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("object")]
        public string Object { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("created")]
        public long Created { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("model")]
        public string Model { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("choices")]
        public Choice[] Choices { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("usage")]
        public Usage Usage { get; set; }
    }

    public class Choice
    {
        [System.Text.Json.Serialization.JsonPropertyName("index")]
        public int Index { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public Message Message { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; }
    }

    public class Message
    {
        [System.Text.Json.Serialization.JsonPropertyName("role")]
        public string Role { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("content")]
        public string Content { get; set; }
    }

    public class Usage
    {
        [System.Text.Json.Serialization.JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}