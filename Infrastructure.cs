// ============================================
// POS.Infrastructure — DbContext + All Services
// ============================================
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using POS.Core.DTOs;
using POS.Core.Entities;
using POS.Core.Interfaces;

// ============================================
// DB CONTEXT
// ============================================
namespace POS.Infrastructure.Data
{
    public class PosDbContext : DbContext
    {
        public PosDbContext(DbContextOptions<PosDbContext> options) : base(options) { }

        public DbSet<Branch> Branches { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<UserActivityLog> UserActivityLogs { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Unit> Units { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<SupplierLedger> SupplierLedger { get; set; }
        public DbSet<Purchase> Purchases { get; set; }
        public DbSet<PurchaseItem> PurchaseItems { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<CustomerLedger> CustomerLedgers { get; set; }
        public DbSet<RestaurantTable> RestaurantTables { get; set; }
        public DbSet<SalesAgent> SalesAgents { get; set; }
        public DbSet<ProductType> ProductTypes { get; set; }
        public DbSet<ProductComponent> ProductComponents { get; set; }
        public DbSet<CashRegisterSession> CashRegisterSessions { get; set; }
        public DbSet<ProductClass> ProductClasses { get; set; }
        public DbSet<SaleQuote> SaleQuotes { get; set; }
        public DbSet<SaleQuoteItem> SaleQuoteItems { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<OrderPayment> OrderPayments { get; set; }
        public DbSet<HeldOrder> HeldOrders { get; set; }
        public DbSet<StockTransaction> StockTransactions { get; set; }
        public DbSet<Expense> Expenses { get; set; }
        public DbSet<ExpenseCategory> ExpenseCategories { get; set; }
        public DbSet<SyncQueue> SyncQueues { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>()
                .HasIndex(p => p.Barcode)
                .IsUnique()
                .HasFilter("[Barcode] IS NOT NULL");

            modelBuilder.Entity<Order>()
                .HasIndex(o => o.OrderNumber)
                .IsUnique();

            modelBuilder.Entity<Purchase>()
                .HasIndex(p => p.PurchaseNumber)
                .IsUnique();

            modelBuilder.Entity<SalesAgent>(e =>
            {
                e.Property(a => a.Name).HasMaxLength(100).IsRequired();
                e.Property(a => a.Phone).HasMaxLength(30);
                e.Property(a => a.AgentType).HasMaxLength(20).IsRequired();
                e.HasIndex(a => new { a.BranchId, a.AgentType });
            });

            modelBuilder.Entity<Order>()
                .HasOne(o => o.Agent)
                .WithMany()
                .HasForeignKey(o => o.AgentId)
                .OnDelete(DeleteBehavior.NoAction);

            // Catering: sale quotes
            modelBuilder.Entity<SaleQuote>(e =>
            {
                e.Property(x => x.QuoteNumber).HasMaxLength(40).IsRequired();
                e.Property(x => x.PublicToken).HasMaxLength(40).IsRequired();
                e.Property(x => x.Status).HasMaxLength(20).IsRequired();
                e.Property(x => x.EventTime).HasMaxLength(20);
                e.Property(x => x.EventType).HasMaxLength(60);
                e.Property(x => x.Venue).HasMaxLength(300);
                e.Property(x => x.Notes).HasMaxLength(1000);
                e.Property(x => x.Terms).HasMaxLength(2000);
                e.Property(x => x.QuoteDate).HasColumnType("date");
                e.Property(x => x.ValidUntil).HasColumnType("date");
                e.Property(x => x.EventDate).HasColumnType("date");
                e.HasIndex(x => new { x.BranchId, x.QuoteNumber }).IsUnique();
                e.HasIndex(x => x.PublicToken).IsUnique();
                e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.NoAction);
                e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.SaleQuoteId).OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<SaleQuoteItem>(e =>
            {
                e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
                e.Property(x => x.UnitName).HasMaxLength(30);
                e.Property(x => x.Notes).HasMaxLength(300);
                e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
            });

            // Product classes (KOT break-up)
            modelBuilder.Entity<ProductClass>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.HasIndex(x => new { x.BranchId, x.Name });
            });
            modelBuilder.Entity<Product>()
                .HasOne<ProductClass>()
                .WithMany()
                .HasForeignKey(p => p.ProductClassId)
                .OnDelete(DeleteBehavior.NoAction);

            // Business day (day open / close) — existing CashRegisterSessions table
            modelBuilder.Entity<CashRegisterSession>(e =>
            {
                e.Property(s => s.BusinessDate).HasColumnType("date");
                e.Property(s => s.Status).HasMaxLength(20).IsRequired();
                e.Property(s => s.Notes).HasMaxLength(500);
                e.Property(s => s.CloseNotes).HasMaxLength(500);
                e.HasIndex(s => new { s.BranchId, s.BusinessDate });
            });

            modelBuilder.Entity<Order>(e =>
            {
                e.Property(o => o.BusinessDate).HasColumnType("date");
                e.HasIndex(o => o.BusinessDate);
                e.HasOne<CashRegisterSession>().WithMany()
                    .HasForeignKey(o => o.BusinessDayId).OnDelete(DeleteBehavior.NoAction);
            });

            // Product types (Inventory / Recipe / Deal & Combo) — fixed list, no screen for it
            modelBuilder.Entity<ProductType>(e =>
            {
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Name).HasMaxLength(50).IsRequired();
                e.HasIndex(x => x.Name).IsUnique();
                e.HasData(
                    new ProductType { Id = ProductTypeIds.Inventory, Name = "Inventory Product" },
                    new ProductType { Id = ProductTypeIds.Recipe, Name = "Recipe Product" },
                    new ProductType { Id = ProductTypeIds.Deal, Name = "Deal and Combo" });
            });

            modelBuilder.Entity<Product>()
                .HasOne<ProductType>()
                .WithMany()
                .HasForeignKey(p => p.ProductTypeId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<ProductComponent>(e =>
            {
                e.HasOne<Product>().WithMany(p => p.Components)
                    .HasForeignKey(x => x.ParentProductId).OnDelete(DeleteBehavior.NoAction);
                e.HasOne(x => x.ComponentProduct).WithMany()
                    .HasForeignKey(x => x.ComponentProductId).OnDelete(DeleteBehavior.NoAction);
                e.HasIndex(x => new { x.ParentProductId, x.ComponentProductId }).IsUnique();
            });

            // Set decimal precision for all decimal properties
            foreach (var property in modelBuilder.Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties())
                .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                property.SetPrecision(18);
                property.SetScale(3);
            }
        }
    }

    // ============================================
    // RECIPE / DEAL STOCK HELPER
    // ============================================
    public class StockGraph
    {
        public Dictionary<int, int> TypeById { get; set; } = new();
        public Dictionary<int, decimal> StockById { get; set; } = new();
        public ILookup<int, ProductComponent> Components { get; set; } =
            Enumerable.Empty<ProductComponent>().ToLookup(c => c.ParentProductId);
    }

    public static class StockExpander
    {
        // Loads product types, stock and recipe/deal rows of a branch (2 small queries)
        public static async Task<StockGraph> LoadAsync(PosDbContext db, int branchId)
        {
            var prods = await db.Products.AsNoTracking()
                .Where(p => p.BranchId == branchId)
                .Select(p => new { p.Id, p.ProductTypeId, p.CurrentStock })
                .ToListAsync();

            var comps = await db.ProductComponents.AsNoTracking()
                .Where(c => db.Products.Any(p => p.Id == c.ParentProductId && p.BranchId == branchId))
                .ToListAsync();

            return new StockGraph
            {
                TypeById = prods.ToDictionary(p => p.Id, p => p.ProductTypeId),
                StockById = prods.ToDictionary(p => p.Id, p => p.CurrentStock),
                Components = comps.ToLookup(c => c.ParentProductId)
            };
        }

        // Selling `qty` of a product -> how much of each INVENTORY product is used.
        // Inventory: itself. Recipe: its ingredients. Deal: its items (recipe items expand to their ingredients).
        public static void Expand(int productId, decimal qty, StockGraph g, Dictionary<int, decimal> result, int depth = 0)
        {
            g.TypeById.TryGetValue(productId, out var type);

            if (type == ProductTypeIds.Recipe || type == ProductTypeIds.Deal)
            {
                if (depth > 5) throw new Exception("Recipe/deal nesting is too deep");
                foreach (var c in g.Components[productId])
                    Expand(c.ComponentProductId, qty * c.Quantity, g, result, depth + 1);
                return;
            }

            result.TryGetValue(productId, out var current);
            result[productId] = current + qty;
        }

        // How many units can be made/sold with the stock available right now.
        // Inventory product: its own stock. Recipe/Deal: limited by its scarcest ingredient.
        public static decimal Available(int productId, StockGraph g, int depth = 0)
        {
            if (depth > 5) return 0;
            g.TypeById.TryGetValue(productId, out var type);

            if (type != ProductTypeIds.Recipe && type != ProductTypeIds.Deal)
                return g.StockById.TryGetValue(productId, out var stock) ? stock : 0;

            var rows = g.Components[productId].ToList();
            if (rows.Count == 0) return 0;

            decimal min = decimal.MaxValue;
            foreach (var c in rows)
            {
                var have = Math.Max(0, Available(c.ComponentProductId, g, depth + 1));
                var portions = c.Quantity > 0 ? Math.Floor(have / c.Quantity) : 0;
                if (portions < min) min = portions;
            }
            return min == decimal.MaxValue ? 0 : min;
        }
    }

    // ============================================
    // BUSINESS DAY (DAY OPEN / DAY CLOSE) HELPERS
    // ============================================
    public static class BusinessTime
    {
        // Business time zone (Pakistan). Falls back to a fixed +05:00 zone if the server has no tz database.
        private static readonly Lazy<TimeZoneInfo> Zone = new(() =>
        {
            foreach (var id in new[] { "Asia/Karachi", "Pakistan Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
            }
            return TimeZoneInfo.CreateCustomTimeZone("PKT", TimeSpan.FromHours(5), "PKT", "PKT");
        });

        public static DateTime ToLocal(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone.Value);

        public static DateTime LocalToday() => ToLocal(DateTime.UtcNow).Date;
    }

    public class PaymentLine
    {
        public string Method { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class DaySummary
    {
        public DateTime Date { get; set; }
        public decimal TotalSales { get; set; }
        public int TotalOrders { get; set; }
        public int CompletedOrders { get; set; }
        public int VoidedOrders { get; set; }
        public int OpenOrders { get; set; }          // held / pending orders that are not completed
        public int TotalItemsSold { get; set; }
        public decimal AvgOrderValue { get; set; }
        public decimal CashSales { get; set; }       // cash received minus change given
        public decimal CardSales { get; set; }
        public decimal WalletSales { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetCash { get; set; }         // cash sales - expenses
        public decimal OpeningBalance { get; set; }
        public decimal ExpectedCash { get; set; }    // opening + cash sales - expenses
        public List<PaymentLine> PaymentBreakdown { get; set; } = new();
    }

    public static class BusinessDayHelper
    {
        public static Task<CashRegisterSession?> GetOpenAsync(PosDbContext db, int branchId) =>
            db.CashRegisterSessions.FirstOrDefaultAsync(s => s.BranchId == branchId && s.Status == "Open");

        // The day that was running at the given time (works for offline sales that sync later).
        // If none matches, the day that is open now is used.
        public static async Task<CashRegisterSession?> ResolveForAsync(PosDbContext db, int branchId, DateTime atUtc)
        {
            var match = await db.CashRegisterSessions.AsNoTracking()
                .Where(s => s.BranchId == branchId && s.OpenedAt <= atUtc && (s.ClosedAt == null || s.ClosedAt >= atUtc))
                .OrderByDescending(s => s.OpenedAt)
                .FirstOrDefaultAsync();
            return match ?? await GetOpenAsync(db, branchId);
        }

        public static async Task<DateTime> CurrentDateAsync(PosDbContext db, int branchId)
        {
            var open = await GetOpenAsync(db, branchId);
            return open != null ? open.BusinessDate.Date : BusinessTime.LocalToday();
        }

        public static async Task<DaySummary> BuildSummaryAsync(PosDbContext db, int branchId, DateTime businessDate, CashRegisterSession? session)
        {
            var date = businessDate.Date;
            var orders = await db.Orders.AsNoTracking()
                .Include(o => o.Payments)
                .Include(o => o.Items)
                .Where(o => o.BranchId == branchId && o.BusinessDate == date)
                .ToListAsync();

            var completed = orders.Where(o => o.Status == "Completed").ToList();
            var voided = orders.Count(o => o.Status == "Voided");
            var openOrders = orders.Count(o => o.Status != "Completed" && o.Status != "Voided");

            var payments = completed.SelectMany(o => o.Payments).ToList();
            var change = completed.Sum(o => o.ChangeAmount);   // change is always given from the cash drawer

            var cashSales = payments.Where(p => p.PaymentMethod == "Cash").Sum(p => p.Amount) - change;
            var cardSales = payments.Where(p => p.PaymentMethod == "Card" || p.PaymentMethod == "BankTransfer").Sum(p => p.Amount);
            var walletSales = payments.Where(p => p.PaymentMethod == "MobileWallet").Sum(p => p.Amount);

            var breakdown = payments
                .GroupBy(p => p.PaymentMethod)
                .Select(g => new PaymentLine
                {
                    Method = g.Key,
                    Amount = g.Sum(p => p.Amount) - (g.Key == "Cash" ? change : 0)
                })
                .ToList();

            // Expenses: during the open..close time of the day (or the calendar date if there is no day record)
            var expenseQuery = db.Expenses.AsNoTracking().Where(e => e.BranchId == branchId);
            if (session != null)
            {
                var from = session.OpenedAt;
                var to = session.ClosedAt ?? DateTime.UtcNow;
                expenseQuery = expenseQuery.Where(e => e.ExpenseDate >= from && e.ExpenseDate <= to);
            }
            else
            {
                var next = date.AddDays(1);
                expenseQuery = expenseQuery.Where(e => e.ExpenseDate >= date && e.ExpenseDate < next);
            }
            var expenses = (await expenseQuery.SumAsync(e => (decimal?)e.Amount)) ?? 0;

            var totalSales = completed.Sum(o => o.TotalAmount);
            var opening = session?.OpeningBalance ?? 0;

            return new DaySummary
            {
                Date = date,
                TotalSales = totalSales,
                TotalOrders = completed.Count,
                CompletedOrders = completed.Count,
                VoidedOrders = voided,
                OpenOrders = openOrders,
                TotalItemsSold = (int)completed.SelectMany(o => o.Items).Sum(i => i.Quantity),
                AvgOrderValue = completed.Count > 0 ? totalSales / completed.Count : 0,
                CashSales = cashSales,
                CardSales = cardSales,
                WalletSales = walletSales,
                TotalExpenses = expenses,
                NetCash = cashSales - expenses,
                OpeningBalance = opening,
                ExpectedCash = opening + cashSales - expenses,
                PaymentBreakdown = breakdown
            };
        }
    }
}

// ============================================
// AUTH SERVICE
// ============================================
namespace POS.Infrastructure.Services
{
    public class AuthService : IAuthService
    {
        private readonly POS.Infrastructure.Data.PosDbContext _db;
        private readonly IConfiguration _config;

        public AuthService(POS.Infrastructure.Data.PosDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public async Task<TokenResponseDto> LoginAsync(LoginDto dto)
        {
            var user = await _db.Users
                .Include(u => u.Role)
                .Include(u => u.Branch)
                .FirstOrDefaultAsync(u => u.Username == dto.Username && u.IsActive);

            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                throw new UnauthorizedAccessException("Invalid username or password");

            user.LastLoginAt = DateTime.UtcNow;
            _db.UserActivityLogs.Add(new UserActivityLog
            {
                UserId = user.Id,
                Action = "Login",
                Description = $"User {user.Username} logged in"
            });
            await _db.SaveChangesAsync();

            var accessToken = GenerateJwt(user);
            var refreshToken = GenerateRefreshToken();
            var expiryDays = int.Parse(_config["Jwt:RefreshTokenExpiryDays"] ?? "30");

            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                Token = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddDays(expiryDays)
            });
            await _db.SaveChangesAsync();

            return new TokenResponseDto(
                accessToken,
                refreshToken,
                DateTime.UtcNow.AddMinutes(60),
                new UserDto(
                    user.Id,
                    user.FullName,
                    user.Username,
                    user.Email,
                    user.Role!.Name,
                    user.BranchId,
                    user.Branch?.Name,
                    user.Avatar
                )
            );
        }

        public async Task<TokenResponseDto> RefreshTokenAsync(string refreshToken)
        {
            var rt = await _db.RefreshTokens
                .Include(r => r.User).ThenInclude(u => u!.Role)
                .FirstOrDefaultAsync(r =>
                    r.Token == refreshToken &&
                    !r.IsRevoked &&
                    r.ExpiresAt > DateTime.UtcNow);

            if (rt == null)
                throw new UnauthorizedAccessException("Invalid or expired refresh token");

            rt.IsRevoked = true;

            var newAccess = GenerateJwt(rt.User!);
            var newRefresh = GenerateRefreshToken();

            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = rt.UserId,
                Token = newRefresh,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            });
            await _db.SaveChangesAsync();

            return new TokenResponseDto(
                newAccess,
                newRefresh,
                DateTime.UtcNow.AddMinutes(60),
                new UserDto(
                    rt.User!.Id,
                    rt.User.FullName,
                    rt.User.Username,
                    rt.User.Email,
                    rt.User.Role!.Name,
                    rt.User.BranchId,
                    null,
                    rt.User.Avatar
                )
            );
        }

        public async Task LogoutAsync(int userId)
        {
            var tokens = await _db.RefreshTokens
                .Where(t => t.UserId == userId && !t.IsRevoked)
                .ToListAsync();

            foreach (var t in tokens) t.IsRevoked = true;

            _db.UserActivityLogs.Add(new UserActivityLog
            {
                UserId = userId,
                Action = "Logout"
            });
            await _db.SaveChangesAsync();
        }

        public async Task<bool> ChangePasswordAsync(int userId, string oldPassword, string newPassword)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null) throw new Exception("User not found");
            if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
                throw new Exception("Current password is incorrect");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return true;
        }

        private string GenerateJwt(User user)
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expiry = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "60");

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role!.Name),
                new Claim("BranchId", user.BranchId.ToString()),
                new Claim("FullName", user.FullName)
            };

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiry),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static string GenerateRefreshToken()
        {
            var bytes = new byte[64];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }
    }

    // ============================================
    // PRODUCT SERVICE
    // ============================================
    public class ProductService : IProductService
    {
        private readonly POS.Infrastructure.Data.PosDbContext _db;
        public ProductService(POS.Infrastructure.Data.PosDbContext db) => _db = db;

        public async Task<Product?> GetByBarcodeAsync(string barcode)
        {
            return await _db.Products
                .Include(p => p.Category)
                .Include(p => p.Unit)
                .FirstOrDefaultAsync(p => p.Barcode == barcode && p.IsActive);
        }

        public async Task<IEnumerable<Product>> GetLowStockAsync(int branchId)
        {
            return await _db.Products
                .Include(p => p.Unit)
                .Where(p => p.BranchId == branchId && p.IsActive && p.ProductTypeId == ProductTypeIds.Inventory && p.CurrentStock <= p.MinimumStock)
                .OrderBy(p => p.CurrentStock)
                .ToListAsync();
        }

        public async Task<bool> UpdateStockAsync(int productId, decimal quantity, string type, int userId)
        {
            var product = await _db.Products.FindAsync(productId);
            if (product == null) return false;

            var before = product.CurrentStock;
            product.CurrentStock = type == "StockIn"
                ? product.CurrentStock + quantity
                : product.CurrentStock - quantity;
            product.UpdatedAt = DateTime.UtcNow;

            _db.StockTransactions.Add(new StockTransaction
            {
                BranchId = product.BranchId,
                ProductId = productId,
                UserId = userId,
                TransactionType = type,
                ReferenceType = "Manual",
                Quantity = quantity,
                QuantityBefore = before,
                QuantityAfter = product.CurrentStock
            });

            await _db.SaveChangesAsync();
            return true;
        }
    }

    // ============================================
    // ORDER SERVICE
    // ============================================
    public class OrderService : IOrderService
    {
        private readonly POS.Infrastructure.Data.PosDbContext _db;
        public OrderService(POS.Infrastructure.Data.PosDbContext db) => _db = db;

        public async Task<Order> CreateOrderAsync(CreateOrderDto dto, int userId, int branchId)
        {
            try
            {
                // The POS saves a sale locally first and sends it later. The bill number it printed is kept,
                // and sending the same sale twice (retry after a slow connection) returns the same order.
                var clientNumber = string.IsNullOrWhiteSpace(dto.ClientOrderNumber) ? null : dto.ClientOrderNumber.Trim();
                if (clientNumber != null && clientNumber.Length > 40) clientNumber = null;
                if (clientNumber != null)
                {
                    var already = await _db.Orders.FirstOrDefaultAsync(o => o.OrderNumber == clientNumber);
                    if (already != null)
                    {
                        if (already.BranchId == branchId) return already;
                        clientNumber = null;   // number belongs to another branch -> make a new one
                    }
                }

                // Time of the sale on the POS (an offline sale keeps its real time) and the business day it belongs to
                var orderedAt = dto.OrderedAt.HasValue ? dto.OrderedAt.Value.ToUniversalTime() : DateTime.UtcNow;
                if (orderedAt > DateTime.UtcNow.AddMinutes(5) || orderedAt < DateTime.UtcNow.AddDays(-14))
                    orderedAt = DateTime.UtcNow;
                var businessDay = await POS.Infrastructure.Data.BusinessDayHelper.ResolveForAsync(_db, branchId, orderedAt);

                var order = new Order
                {
                    BranchId = branchId,
                    OrderedAt = orderedAt,
                    BusinessDayId = businessDay?.Id,
                    BusinessDate = businessDay != null
                        ? businessDay.BusinessDate.Date
                        : POS.Infrastructure.Data.BusinessTime.ToLocal(orderedAt).Date,
                    CustomerId = dto.CustomerId,
                    UserId = userId,
                    TableId = dto.TableId,
                    OrderType = dto.OrderType,
                    OrderNumber = clientNumber ?? $"ORD-{DateTime.Now:yyyyMMddHHmmss}-{new Random().Next(100, 999)}",
                    DiscountType = dto.DiscountType,
                    DiscountValue = dto.DiscountValue,
                    Notes = dto.Notes,
                    KOTSentAt = dto.KOTSentAt,
                    Status = "Pending"
                };

                // Waiter (Dine In) / Rider (Delivery): agent aur uska fixed commission order par save hota hai
                if (dto.AgentId.HasValue && dto.OrderType != "Takeaway")
                {
                    var agent = await _db.SalesAgents.FirstOrDefaultAsync(a =>
                        a.Id == dto.AgentId.Value && a.BranchId == branchId && a.IsActive)
                        ?? throw new Exception("Selected waiter/rider not found");
                    order.AgentId = agent.Id;
                    order.AgentCommission = agent.CommissionAmount;
                }

                decimal subTotal = 0;
                decimal totalTax = 0;

                foreach (var itemDto in dto.Items)
                {
                    var product = await _db.Products.FindAsync(itemDto.ProductId);
                    if (product == null) continue;

                    var lineBase = itemDto.UnitPrice * itemDto.Quantity;
                    var tax = lineBase * (product.TaxRate / 100);
                    var lineTotal = lineBase + tax - itemDto.DiscountAmount;

                    order.Items.Add(new OrderItem
                    {
                        ProductId = itemDto.ProductId,
                        Quantity = itemDto.Quantity,
                        UnitPrice = itemDto.UnitPrice,
                        TaxRate = product.TaxRate,
                        TaxAmount = tax,
                        DiscountAmount = itemDto.DiscountAmount,
                        TotalAmount = lineTotal,
                        Notes = itemDto.Notes,
                        IsKOTSent = itemDto.IsKOTSent,
                        KOTSentAt = itemDto.IsKOTSent ? (itemDto.KOTSentAt ?? DateTime.UtcNow) : null
                    });

                    subTotal += lineBase;
                    totalTax += tax;
                }

                order.SubTotal = subTotal;
                order.TaxAmount = totalTax;

                if (!string.IsNullOrEmpty(dto.DiscountType))
                {
                    order.DiscountAmount = dto.DiscountType == "Percent"
                        ? subTotal * (dto.DiscountValue / 100)
                        : dto.DiscountValue;
                }

                order.TotalAmount = subTotal + totalTax - order.DiscountAmount;

                if (dto.Payments.Any())
                {
                    foreach (var p in dto.Payments)
                    {
                        order.Payments.Add(new OrderPayment
                        {
                            PaymentMethod = p.PaymentMethod,
                            Amount = p.Amount,
                            Reference = p.Reference
                        });
                    }
                    order.PaidAmount = dto.Payments.Sum(p => p.Amount);
                }

                order.DueAmount = Math.Max(0, order.TotalAmount - order.PaidAmount);
                order.ChangeAmount = Math.Max(0, order.PaidAmount - order.TotalAmount);

                _db.Orders.Add(order);
                await _db.SaveChangesAsync();
                return order;
            }
            catch
            {
                throw;
            }
        }

        public async Task<Order> CompleteOrderAsync(long orderId)
        {
            try
            {
                var order = await _db.Orders
                    .Include(o => o.Items).ThenInclude(i => i.Product)
                    .Include(o => o.Customer)
                    .FirstOrDefaultAsync(o => o.Id == orderId)
                    ?? throw new Exception("Order not found");

                if (order.Status == "Completed")
                    throw new Exception("Order already completed");

                order.Status = "Completed";
                order.CompletedAt = DateTime.UtcNow;

                // Stock out: Inventory item -> its own stock. Recipe item -> its ingredients.
                // Deal item -> every inventory item in it, and the ingredients of recipe items inside it.
                var graph = await POS.Infrastructure.Data.StockExpander.LoadAsync(_db, order.BranchId);
                var consumption = new Dictionary<int, decimal>();
                foreach (var item in order.Items)
                    POS.Infrastructure.Data.StockExpander.Expand(item.ProductId, item.Quantity, graph, consumption);

                var directPrice = new Dictionary<int, decimal>();
                foreach (var item in order.Items)
                    if (consumption.ContainsKey(item.ProductId) && !directPrice.ContainsKey(item.ProductId))
                        directPrice[item.ProductId] = item.UnitPrice;

                // (one lookup per product instead of list.Contains -> works on every SQL Server version)
                var stockProducts = new Dictionary<int, Product>();
                foreach (var stockId in consumption.Keys)
                {
                    var stockProduct = await _db.Products.FindAsync(stockId);
                    if (stockProduct != null) stockProducts[stockId] = stockProduct;
                }

                foreach (var used in consumption)
                {
                    if (!stockProducts.TryGetValue(used.Key, out var sp)) continue;
                    var before = sp.CurrentStock;
                    sp.CurrentStock -= used.Value;

                    _db.StockTransactions.Add(new StockTransaction
                    {
                        BranchId = order.BranchId,
                        ProductId = used.Key,
                        UserId = order.UserId,
                        TransactionType = "StockOut",
                        ReferenceType = "Sale",
                        ReferenceId = (int)order.Id,
                        Quantity = used.Value,
                        QuantityBefore = before,
                        QuantityAfter = sp.CurrentStock,
                        UnitCost = directPrice.TryGetValue(used.Key, out var up) ? up : sp.PurchasePrice
                    });
                }

                if (order.CustomerId.HasValue && order.DueAmount > 0 && order.Customer != null)
                {
                    order.Customer.CurrentBalance += order.DueAmount;
                    _db.CustomerLedgers.Add(new CustomerLedger
                    {
                        CustomerId = order.CustomerId.Value,
                        TransactionType = "Sale",
                        ReferenceId = (int)order.Id,
                        Debit = order.TotalAmount,
                        Credit = order.PaidAmount,
                        Balance = order.Customer.CurrentBalance
                    });
                }

                if (order.TableId.HasValue)
                {
                    var table = await _db.RestaurantTables.FindAsync(order.TableId);
                    if (table != null) table.Status = "Available";
                }

                await _db.SaveChangesAsync();
                return order;
            }
            catch
            {
                throw;
            }
        }

        public async Task<Order> VoidOrderAsync(long orderId, string reason)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                .Include(o => o.Customer)
                .FirstOrDefaultAsync(o => o.Id == orderId)
                ?? throw new Exception("Order not found");

            if (order.Status == "Voided")
                throw new Exception("This order is already voided");

            // Orders of a closed business day cannot be changed (the day was already counted)
            if (order.BusinessDayId.HasValue)
            {
                var day = await _db.CashRegisterSessions.FirstOrDefaultAsync(s => s.Id == order.BusinessDayId.Value);
                if (day != null && day.Status == "Closed")
                    throw new Exception("The business day of this order is already closed. Reopen the day to void it.");
            }

            if (order.Status == "Completed")
            {
                // Put the stock back (recipe / deal -> their ingredients), same maths as the sale
                var graph = await POS.Infrastructure.Data.StockExpander.LoadAsync(_db, order.BranchId);
                var consumption = new Dictionary<int, decimal>();
                foreach (var item in order.Items)
                    POS.Infrastructure.Data.StockExpander.Expand(item.ProductId, item.Quantity, graph, consumption);

                foreach (var used in consumption)
                {
                    var sp = await _db.Products.FindAsync(used.Key);
                    if (sp == null) continue;
                    var before = sp.CurrentStock;
                    sp.CurrentStock += used.Value;

                    _db.StockTransactions.Add(new StockTransaction
                    {
                        BranchId = order.BranchId,
                        ProductId = used.Key,
                        UserId = order.UserId,
                        TransactionType = used.Value >= 0 ? "StockIn" : "StockOut",
                        ReferenceType = "SaleVoid",
                        ReferenceId = (int)order.Id,
                        Quantity = Math.Abs(used.Value),
                        QuantityBefore = before,
                        QuantityAfter = sp.CurrentStock,
                        UnitCost = sp.PurchasePrice
                    });
                }

                // Credit sale: take the due amount back from the customer balance
                if (order.CustomerId.HasValue && order.DueAmount > 0 && order.Customer != null)
                {
                    order.Customer.CurrentBalance -= order.DueAmount;
                    _db.CustomerLedgers.Add(new CustomerLedger
                    {
                        CustomerId = order.CustomerId.Value,
                        TransactionType = "SaleVoid",
                        ReferenceId = (int)order.Id,
                        Debit = order.PaidAmount,
                        Credit = order.TotalAmount,
                        Balance = order.Customer.CurrentBalance
                    });
                }
            }

            var voidNote = $"Voided: {(string.IsNullOrWhiteSpace(reason) ? "no reason" : reason.Trim())}";
            order.Notes = string.IsNullOrWhiteSpace(order.Notes) ? voidNote : $"{order.Notes} | {voidNote}";
            order.Status = "Voided";
            await _db.SaveChangesAsync();
            return order;
        }

        public async Task<string> GenerateKOTAsync(long orderId)
        {
            var order = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.Id == orderId)
                ?? throw new Exception("Order not found");

            foreach (var item in order.Items.Where(i => !i.IsKOTSent))
            {
                item.IsKOTSent = true;
                item.KOTSentAt = DateTime.UtcNow;
            }
            order.KOTSentAt = DateTime.UtcNow;
            order.Status = "KOT";
            await _db.SaveChangesAsync();

            return JsonSerializer.Serialize(new
            {
                KOTNumber = $"KOT-{order.Id}",
                order.OrderNumber,
                Table = order.Table?.TableNumber,
                order.OrderType,
                Time = DateTime.Now.ToString("HH:mm"),
                Items = order.Items.Select(i => new
                {
                    Name = i.Product?.Name,
                    i.Quantity,
                    i.Notes
                })
            });
        }

        public async Task<string> GenerateReceiptAsync(long orderId)
        {
            var order = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Payments)
                .Include(o => o.Customer)
                .Include(o => o.User)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.Id == orderId)
                ?? throw new Exception("Order not found");

            return JsonSerializer.Serialize(new
            {
                order.OrderNumber,
                order.OrderType,
                Table = order.Table?.TableNumber,
                Customer = order.Customer?.Name,
                Cashier = order.User?.FullName,
                DateTime = order.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                Items = order.Items.Select(i => new
                {
                    Name = i.Product?.Name,
                    i.Quantity,
                    i.UnitPrice,
                    i.TaxAmount,
                    i.DiscountAmount,
                    i.TotalAmount
                }),
                order.SubTotal,
                order.TaxAmount,
                order.DiscountAmount,
                order.TotalAmount,
                order.PaidAmount,
                order.DueAmount,
                order.ChangeAmount,
                Payments = order.Payments.Select(p => new { p.PaymentMethod, p.Amount })
            });
        }

        public async Task<Order> HoldOrderAsync(long orderId, string holdName)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId)
                ?? throw new Exception("Order not found");

            order.Status = "Hold";

            _db.HeldOrders.Add(new HeldOrder
            {
                BranchId = order.BranchId,
                UserId = order.UserId,
                HoldName = holdName ?? $"Hold-{DateTime.Now:HHmm}",
                OrderData = JsonSerializer.Serialize(order)
            });

            await _db.SaveChangesAsync();
            return order;
        }

        public async Task<Order> ResumeOrderAsync(int heldOrderId)
        {
            var held = await _db.HeldOrders.FindAsync(heldOrderId)
                ?? throw new Exception("Held order not found");

            var order = JsonSerializer.Deserialize<Order>(held.OrderData)
                ?? throw new Exception("Could not deserialize held order");

            order.Status = "Pending";
            _db.HeldOrders.Remove(held);
            await _db.SaveChangesAsync();
            return order;
        }
    }

    // ============================================
    // DASHBOARD SERVICE
    // ============================================
    public class DashboardService : IDashboardService
    {
        private readonly POS.Infrastructure.Data.PosDbContext _db;
        public DashboardService(POS.Infrastructure.Data.PosDbContext db) => _db = db;

        public async Task<DashboardDto> GetDashboardDataAsync(int branchId)
        {
            var openDay = await POS.Infrastructure.Data.BusinessDayHelper.GetOpenAsync(_db, branchId);
            var today = openDay != null ? openDay.BusinessDate.Date : POS.Infrastructure.Data.BusinessTime.LocalToday();
            var expenseFrom = openDay != null ? openDay.OpenedAt : today;
            var monthStart = new DateTime(today.Year, today.Month, 1);

            var todayOrders = await _db.Orders
                .Where(o => o.BranchId == branchId &&
                            o.Status == "Completed" &&
                            o.BusinessDate == today)
                .ToListAsync();

            var todaySales = todayOrders.Sum(o => o.TotalAmount);

            var todayCost = await _db.OrderItems
                .Where(oi => _db.Orders.Any(o =>
                    o.Id == oi.OrderId &&
                    o.BranchId == branchId &&
                    o.Status == "Completed" &&
                    o.BusinessDate == today))
                .Join(_db.Products,
                    oi => oi.ProductId,
                    p => p.Id,
                    (oi, p) => oi.Quantity * p.PurchasePrice)
                .SumAsync();

            var todayExpenses = await _db.Expenses
                .Where(e => e.BranchId == branchId && e.ExpenseDate >= expenseFrom)
                .SumAsync(e => e.Amount);

            var monthSales = await _db.Orders
                .Where(o => o.BranchId == branchId &&
                            o.Status == "Completed" &&
                            o.BusinessDate >= monthStart)
                .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

            var lowStockCount = await _db.Products
                .Where(p => p.BranchId == branchId &&
                            p.IsActive &&
                            p.ProductTypeId == ProductTypeIds.Inventory &&
                            p.CurrentStock <= p.MinimumStock)
                .CountAsync();

            // Last 6 months data
            var monthlyData = new List<MonthlySalesDto>();
            for (int i = 5; i >= 0; i--)
            {
                var m = today.AddMonths(-i);
                var ms = new DateTime(m.Year, m.Month, 1);
                var me = ms.AddMonths(1);
                var sales = await _db.Orders
                    .Where(o => o.BranchId == branchId &&
                                o.Status == "Completed" &&
                                o.BusinessDate >= ms &&
                                o.BusinessDate < me)
                    .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;
                monthlyData.Add(new MonthlySalesDto(m.ToString("MMM"), sales, sales * 0.3m));
            }

            var topProducts = await _db.OrderItems
                .Where(oi => _db.Orders.Any(o =>
                    o.Id == oi.OrderId &&
                    o.BranchId == branchId &&
                    o.Status == "Completed" &&
                    o.BusinessDate >= monthStart))
                .GroupBy(oi => oi.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    Qty = g.Sum(x => x.Quantity),
                    Rev = g.Sum(x => x.TotalAmount)
                })
                .OrderByDescending(x => x.Rev)
                .Take(5)
                .Join(_db.Products,
                    x => x.ProductId,
                    p => p.Id,
                    (x, p) => new TopProductDto(p.Name, x.Qty, x.Rev))
                .ToListAsync();

            var lowStockProducts = await _db.Products
                .Where(p => p.BranchId == branchId &&
                            p.IsActive &&
                            p.ProductTypeId == ProductTypeIds.Inventory &&
                            p.CurrentStock <= p.MinimumStock)
                .Include(p => p.Unit)
                .Take(10)
                .Select(p => new LowStockProductDto(
                    p.Id, p.Name, p.CurrentStock,
                    p.MinimumStock, p.Unit!.Abbreviation))
                .ToListAsync();

            return new DashboardDto
            {
                TodaySales = todaySales,
                TodayProfit = todaySales - todayCost - todayExpenses,
                TodayOrders = todayOrders.Count,
                TodayExpenses = todayExpenses,
                MonthSales = monthSales,
                LowStockCount = lowStockCount,
                MonthlyData = monthlyData,
                TopProducts = topProducts,
                LowStockProducts = lowStockProducts
            };
        }
    }

    // ============================================
    // REPORT SERVICE
    // ============================================
    public class ReportService : IReportService
    {
        private readonly POS.Infrastructure.Data.PosDbContext _db;
        public ReportService(POS.Infrastructure.Data.PosDbContext db) => _db = db;

        public async Task<SalesReportDto> GetSalesReportAsync(int branchId, DateTime startDate, DateTime endDate)
        {
            var endInclusive = endDate.AddDays(1);
            var orders = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product).ThenInclude(p => p!.Category)
                .Include(o => o.Customer)
                .Include(o => o.User)
                .Include(o => o.Agent)
                .Where(o => o.BranchId == branchId &&
                            o.Status == "Completed" &&
                            o.BusinessDate >= startDate.Date &&
                            o.BusinessDate < endInclusive.Date)
                .ToListAsync();

            var byDate = orders
                .GroupBy(o => o.BusinessDate.Date)
                .Select(g =>
                {
                    var cost = g.SelectMany(o => o.Items)
                        .Sum(i => i.Quantity * (i.Product?.PurchasePrice ?? 0));
                    return new SalesByDateDto(
                        g.Key, g.Count(),
                        g.Sum(o => o.TotalAmount),
                        g.Sum(o => o.TotalAmount) - cost);
                })
                .OrderBy(x => x.Date)
                .ToList();

            var byCategory = orders
                .SelectMany(o => o.Items)
                .GroupBy(i => i.Product?.Category?.Name ?? "Uncategorized")
                .Select(g => new SalesByCategoryDto(
                    g.Key, (int)g.Sum(i => i.Quantity),
                    g.Sum(i => i.TotalAmount),
                    g.Sum(i => i.TotalAmount - (i.Quantity * (i.Product?.PurchasePrice ?? 0)))))
                .OrderByDescending(x => x.Sales)
                .ToList();

            var byUser = orders
                .GroupBy(o => o.User?.FullName ?? "Unknown")
                .Select(g => new SalesByUserDto(g.Key, g.Count(), g.Sum(o => o.TotalAmount)))
                .ToList();

            var byCustomer = orders
                .Where(o => o.Customer != null)
                .GroupBy(o => o.Customer!.Name)
                .Select(g => new SalesByCustomerDto(
                    g.Key, g.Count(),
                    g.Sum(o => o.TotalAmount),
                    g.First().Customer?.CurrentBalance ?? 0))
                .ToList();

            var byAgent = orders
                .Where(o => o.Agent != null)
                .GroupBy(o => new { o.Agent!.Id, o.Agent.Name, o.Agent.AgentType })
                .Select(g => new SalesByAgentDto(
                    g.Key.Name, g.Key.AgentType, g.Count(),
                    g.Sum(o => o.TotalAmount),
                    g.Sum(o => o.AgentCommission)))
                .OrderByDescending(x => x.Sales)
                .ToList();

            return new SalesReportDto
            {
                StartDate = startDate,
                EndDate = endDate,
                TotalSales = orders.Sum(o => o.TotalAmount),
                TotalTax = orders.Sum(o => o.TaxAmount),
                TotalDiscount = orders.Sum(o => o.DiscountAmount),
                NetSales = orders.Sum(o => o.TotalAmount - o.TaxAmount),
                TotalOrders = orders.Count,
                ByDate = byDate,
                ByCategory = byCategory,
                ByUser = byUser,
                ByCustomer = byCustomer,
                ByAgent = byAgent
            };
        }

        public async Task<object> GetStockReportAsync(int branchId)
        {
            var products = await _db.Products
                .Include(p => p.Category)
                .Include(p => p.Unit)
                .Where(p => p.BranchId == branchId && p.IsActive && p.ProductTypeId == ProductTypeIds.Inventory)
                .ToListAsync();

            return new
            {
                TotalProducts = products.Count,
                TotalValue = products.Sum(p => p.CurrentStock * p.PurchasePrice),
                LowStockCount = products.Count(p => p.CurrentStock > 0 && p.CurrentStock <= p.MinimumStock),
                OutOfStockCount = products.Count(p => p.CurrentStock <= 0),
                Items = products.Select(p => new
                {
                    p.Id,
                    p.Name,
                    Category = p.Category?.Name,
                    Unit = p.Unit?.Abbreviation,
                    p.CurrentStock,
                    p.MinimumStock,
                    p.PurchasePrice,
                    p.SalePrice,
                    StockValue = p.CurrentStock * p.PurchasePrice
                })
            };
        }

        public async Task<object> GetExpenseReportAsync(int branchId, DateTime startDate, DateTime endDate)
        {
            var endInclusive = endDate.AddDays(1);
            var expenses = await _db.Expenses
                .Include(e => e.Category)
                .Include(e => e.User)
                .Where(e => e.BranchId == branchId &&
                            e.ExpenseDate >= startDate &&
                            e.ExpenseDate < endInclusive)
                .OrderByDescending(e => e.ExpenseDate)
                .ToListAsync();

            var byCategory = expenses
                .GroupBy(e => e.Category?.Name ?? "Other")
                .Select(g => new { Category = g.Key, Amount = g.Sum(e => e.Amount) })
                .OrderByDescending(x => x.Amount)
                .ToList();

            var byDate = expenses
                .GroupBy(e => e.ExpenseDate.Date)
                .Select(g => new { Date = g.Key, Amount = g.Sum(e => e.Amount) })
                .OrderBy(x => x.Date)
                .ToList();

            var days = (endDate - startDate).Days + 1;

            return new
            {
                TotalAmount = expenses.Sum(e => e.Amount),
                TotalCount = expenses.Count,
                TopCategory = byCategory.FirstOrDefault()?.Category,
                AvgDaily = days > 0 ? expenses.Sum(e => e.Amount) / days : 0,
                ByCategory = byCategory,
                ByDate = byDate,
                Items = expenses.Select(e => new
                {
                    Date = e.ExpenseDate,
                    e.Title,
                    Category = e.Category?.Name,
                    e.Amount,
                    e.PaymentMethod,
                    User = e.User?.FullName,
                    e.Notes
                })
            };
        }

        public async Task<object> GetProfitLossAsync(int branchId, DateTime startDate, DateTime endDate)
        {
            var endInclusive = endDate.AddDays(1);

            var orders = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Where(o => o.BranchId == branchId &&
                            o.Status == "Completed" &&
                            o.BusinessDate >= startDate.Date &&
                            o.BusinessDate < endInclusive.Date)
                .ToListAsync();

            var totalRevenue = orders.Sum(o => o.TotalAmount);
            var totalCost = orders.SelectMany(o => o.Items)
                .Sum(i => i.Quantity * (i.Product?.PurchasePrice ?? 0));
            var grossProfit = totalRevenue - totalCost;

            var totalExpenses = await _db.Expenses
                .Where(e => e.BranchId == branchId &&
                            e.ExpenseDate >= startDate &&
                            e.ExpenseDate < endInclusive)
                .SumAsync(e => e.Amount);

            var netProfit = grossProfit - totalExpenses;

            return new
            {
                TotalRevenue = totalRevenue,
                TotalCost = totalCost,
                GrossProfit = grossProfit,
                GrossMargin = totalRevenue > 0 ? (grossProfit / totalRevenue) * 100 : 0,
                TotalExpenses = totalExpenses,
                NetProfit = netProfit,
                NetMargin = totalRevenue > 0 ? (netProfit / totalRevenue) * 100 : 0
            };
        }

        public async Task<object> GetDailyClosingAsync(int branchId, DateTime? date)
        {
            var businessDate = date.HasValue
                ? date.Value.Date
                : await POS.Infrastructure.Data.BusinessDayHelper.CurrentDateAsync(_db, branchId);

            var session = await _db.CashRegisterSessions.AsNoTracking()
                .Where(s => s.BranchId == branchId && s.BusinessDate == businessDate)
                .OrderByDescending(s => s.OpenedAt)
                .FirstOrDefaultAsync();

            var summary = await POS.Infrastructure.Data.BusinessDayHelper.BuildSummaryAsync(_db, branchId, businessDate, session);
            return new
            {
                summary.Date,
                summary.TotalSales,
                summary.TotalOrders,
                summary.CompletedOrders,
                summary.VoidedOrders,
                summary.OpenOrders,
                summary.TotalItemsSold,
                summary.AvgOrderValue,
                summary.CashSales,
                summary.CardSales,
                summary.WalletSales,
                summary.TotalExpenses,
                summary.NetCash,
                summary.OpeningBalance,
                summary.ExpectedCash,
                summary.PaymentBreakdown,
                Status = session?.Status,
                OpenedAt = session?.OpenedAt,
                ClosedAt = session?.ClosedAt,
                ClosingBalance = session?.ClosingBalance
            };
        }
    }

    // ============================================
    // SYNC SERVICE
    // ============================================
    public class SyncService : ISyncService
    {
        private readonly POS.Infrastructure.Data.PosDbContext _db;
        public SyncService(POS.Infrastructure.Data.PosDbContext db) => _db = db;

        public async Task<bool> ProcessSyncQueueAsync(List<SyncQueue> items)
        {
            foreach (var item in items)
            {
                item.Status = "Synced";
                item.SyncedAt = DateTime.UtcNow;
                _db.SyncQueues.Add(item);
            }
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<object> GetServerChangesAsync(int branchId, DateTime lastSyncAt)
        {
            var products = await _db.Products
                .Include(p => p.Category)
                .Include(p => p.Unit)
                .Where(p => p.BranchId == branchId && p.UpdatedAt > lastSyncAt)
                .ToListAsync();

            var categories = await _db.Categories
                .Where(c => c.BranchId == branchId && c.UpdatedAt > lastSyncAt)
                .ToListAsync();

            var customers = await _db.Customers
                .Where(c => c.BranchId == branchId && c.UpdatedAt > lastSyncAt)
                .ToListAsync();

            var tables = await _db.RestaurantTables
                .Where(t => t.BranchId == branchId)
                .ToListAsync();

            return new { products, categories, customers, tables, serverTime = DateTime.UtcNow };
        }
    }
}