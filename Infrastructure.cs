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
                .Where(p => p.BranchId == branchId && p.IsActive && p.CurrentStock <= p.MinimumStock)
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
                var order = new Order
                {
                    BranchId = branchId,
                    CustomerId = dto.CustomerId,
                    UserId = userId,
                    TableId = dto.TableId,
                    OrderType = dto.OrderType,
                    OrderNumber = $"ORD-{DateTime.Now:yyyyMMddHHmmss}-{new Random().Next(100, 999)}",
                    DiscountType = dto.DiscountType,
                    DiscountValue = dto.DiscountValue,
                    Notes = dto.Notes,
                    Status = "Pending"
                };

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
                        Notes = itemDto.Notes
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

                foreach (var item in order.Items)
                {
                    if (item.Product == null) continue;
                    var before = item.Product.CurrentStock;
                    item.Product.CurrentStock -= item.Quantity;

                    _db.StockTransactions.Add(new StockTransaction
                    {
                        BranchId = order.BranchId,
                        ProductId = item.ProductId,
                        UserId = order.UserId,
                        TransactionType = "StockOut",
                        ReferenceType = "Sale",
                        ReferenceId = (int)order.Id,
                        Quantity = item.Quantity,
                        QuantityBefore = before,
                        QuantityAfter = item.Product.CurrentStock,
                        UnitCost = item.UnitPrice
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
            var order = await _db.Orders.FindAsync(orderId)
                ?? throw new Exception("Order not found");
            order.Status = "Voided";
            order.Notes = $"Voided: {reason}";
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
            var today = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);

            var todayOrders = await _db.Orders
                .Where(o => o.BranchId == branchId &&
                            o.Status == "Completed" &&
                            o.CompletedAt >= today)
                .ToListAsync();

            var todaySales = todayOrders.Sum(o => o.TotalAmount);

            var todayCost = await _db.OrderItems
                .Where(oi => _db.Orders.Any(o =>
                    o.Id == oi.OrderId &&
                    o.BranchId == branchId &&
                    o.Status == "Completed" &&
                    o.CompletedAt >= today))
                .Join(_db.Products,
                    oi => oi.ProductId,
                    p => p.Id,
                    (oi, p) => oi.Quantity * p.PurchasePrice)
                .SumAsync();

            var todayExpenses = await _db.Expenses
                .Where(e => e.BranchId == branchId && e.ExpenseDate >= today)
                .SumAsync(e => e.Amount);

            var monthSales = await _db.Orders
                .Where(o => o.BranchId == branchId &&
                            o.Status == "Completed" &&
                            o.CompletedAt >= monthStart)
                .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

            var lowStockCount = await _db.Products
                .Where(p => p.BranchId == branchId &&
                            p.IsActive &&
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
                                o.CompletedAt >= ms &&
                                o.CompletedAt < me)
                    .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;
                monthlyData.Add(new MonthlySalesDto(m.ToString("MMM"), sales, sales * 0.3m));
            }

            var topProducts = await _db.OrderItems
                .Where(oi => _db.Orders.Any(o =>
                    o.Id == oi.OrderId &&
                    o.BranchId == branchId &&
                    o.Status == "Completed" &&
                    o.CompletedAt >= monthStart))
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
                .Where(o => o.BranchId == branchId &&
                            o.Status == "Completed" &&
                            o.CompletedAt >= startDate &&
                            o.CompletedAt < endInclusive)
                .ToListAsync();

            var byDate = orders
                .GroupBy(o => o.CompletedAt!.Value.Date)
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
                ByCustomer = byCustomer
            };
        }

        public async Task<object> GetStockReportAsync(int branchId)
        {
            var products = await _db.Products
                .Include(p => p.Category)
                .Include(p => p.Unit)
                .Where(p => p.BranchId == branchId && p.IsActive)
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
                            o.CompletedAt >= startDate &&
                            o.CompletedAt < endInclusive)
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

        public async Task<object> GetDailyClosingAsync(int branchId, DateTime date)
        {
            var nextDay = date.AddDays(1);
            var orders = await _db.Orders
                .Include(o => o.Payments)
                .Include(o => o.Items)
                .Where(o => o.BranchId == branchId &&
                            o.CompletedAt >= date &&
                            o.CompletedAt < nextDay)
                .ToListAsync();

            var completed = orders.Where(o => o.Status == "Completed").ToList();
            var voided = orders.Where(o => o.Status == "Voided").ToList();

            var allPayments = completed.SelectMany(o => o.Payments).ToList();
            var paymentBreakdown = allPayments
                .GroupBy(p => p.PaymentMethod)
                .Select(g => new { Method = g.Key, Amount = g.Sum(p => p.Amount) })
                .ToList();

            var expenses = await _db.Expenses
                .Where(e => e.BranchId == branchId &&
                            e.ExpenseDate >= date &&
                            e.ExpenseDate < nextDay)
                .SumAsync(e => e.Amount);

            var totalSales = completed.Sum(o => o.TotalAmount);
            var cashSales = allPayments.Where(p => p.PaymentMethod == "Cash").Sum(p => p.Amount);
            var cardSales = allPayments.Where(p => p.PaymentMethod is "Card" or "BankTransfer").Sum(p => p.Amount);
            var walletSales = allPayments.Where(p => p.PaymentMethod == "MobileWallet").Sum(p => p.Amount);

            return new
            {
                Date = date,
                TotalSales = totalSales,
                TotalOrders = completed.Count,
                CompletedOrders = completed.Count,
                VoidedOrders = voided.Count,
                TotalItemsSold = (int)completed.SelectMany(o => o.Items).Sum(i => i.Quantity),
                AvgOrderValue = completed.Any() ? totalSales / completed.Count : 0,
                CashSales = cashSales,
                CardSales = cardSales,
                WalletSales = walletSales,
                TotalExpenses = expenses,
                NetCash = cashSales - expenses,
                PaymentBreakdown = paymentBreakdown
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