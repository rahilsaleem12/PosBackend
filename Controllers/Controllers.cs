// ============================================
// POS.API — All Controllers
// ============================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using POS.Core.DTOs;
using POS.Core.Entities;
using POS.Core.Interfaces;
using POS.Infrastructure.Data;

namespace POS.API.Controllers
{
    // ============================================
    // BASE CONTROLLER
    // ============================================
    [ApiController]
    [Route("api/[controller]")]
    public abstract class BaseController : ControllerBase
    {
        protected int CurrentUserId =>
            int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        protected int CurrentBranchId =>
            int.Parse(User.FindFirst("BranchId")?.Value ?? "0");

        protected string CurrentUserRole =>
            User.FindFirst(ClaimTypes.Role)?.Value ?? "";
    }

    // ============================================
    // AUTH CONTROLLER
    // ============================================
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        public AuthController(IAuthService authService) => _authService = authService;

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            try
            {
                var result = await _authService.LoginAsync(dto);
                return Ok(ApiResponse<TokenResponseDto>.Ok(result, "Login successful"));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ApiResponse<object>.Fail(ex.Message));
            }
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] string refreshToken)
        {
            try
            {
                var result = await _authService.RefreshTokenAsync(refreshToken);
                return Ok(ApiResponse<TokenResponseDto>.Ok(result));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ApiResponse<object>.Fail(ex.Message));
            }
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            await _authService.LogoutAsync(userId);
            return Ok(ApiResponse<object>.Ok(null!, "Logged out"));
        }

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            try
            {
                await _authService.ChangePasswordAsync(userId, dto.OldPassword, dto.NewPassword);
                return Ok(ApiResponse<object>.Ok(null!, "Password changed successfully"));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }
    }

    // ============================================
    // PRODUCTS CONTROLLER
    // ============================================
    [Authorize]
    [Route("api/products")]
    [ApiController]
    public class ProductsController : BaseController
    {
        private readonly PosDbContext _db;
        private readonly IProductService _productService;

        public ProductsController(PosDbContext db, IProductService productService)
        {
            _db = db;
            _productService = productService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? search = null,
            [FromQuery] int? categoryId = null,
            [FromQuery] bool? lowStock = null)
        {
            var query = _db.Products
                .Include(p => p.Category)
                .Include(p => p.Unit)
                .Where(p => p.BranchId == CurrentBranchId && p.IsActive);

            if (!string.IsNullOrEmpty(search))
                query = query.Where(p =>
                    p.Name.Contains(search) ||
                    (p.Barcode != null && p.Barcode.Contains(search)) ||
                    (p.SKU != null && p.SKU.Contains(search)));

            if (categoryId.HasValue)
                query = query.Where(p => p.CategoryId == categoryId.Value);

            if (lowStock == true)
                query = query.Where(p => p.CurrentStock <= p.MinimumStock);

            var total = await query.CountAsync();
            var items = await query
                .OrderBy(p => p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(ApiResponse<IEnumerable<Product>>.Ok(items, totalCount: total));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var product = await _db.Products
                .Include(p => p.Category)
                .Include(p => p.Unit)
                .FirstOrDefaultAsync(p => p.Id == id && p.BranchId == CurrentBranchId);

            if (product == null)
                return NotFound(ApiResponse<Product>.Fail("Product not found"));

            return Ok(ApiResponse<Product>.Ok(product));
        }

        [HttpGet("barcode/{barcode}")]
        public async Task<IActionResult> GetByBarcode(string barcode)
        {
            var product = await _productService.GetByBarcodeAsync(barcode);
            if (product == null)
                return NotFound(ApiResponse<Product>.Fail("Product not found"));

            return Ok(ApiResponse<Product>.Ok(product));
        }

        [HttpGet("low-stock")]
        public async Task<IActionResult> LowStock()
        {
            var items = await _productService.GetLowStockAsync(CurrentBranchId);
            return Ok(ApiResponse<IEnumerable<Product>>.Ok(items));
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateProductDto dto)
        {
            var product = new Product
            {
                BranchId = CurrentBranchId,
                CategoryId = dto.CategoryId,
                UnitId = dto.UnitId,
                Name = dto.Name,
                Description = dto.Description,
                Barcode = dto.Barcode,
                SKU = dto.SKU,
                ImageUrl = dto.ImageUrl,
                PurchasePrice = dto.PurchasePrice,
                SalePrice = dto.SalePrice,
                TaxRate = dto.TaxRate,
                MinimumStock = dto.MinimumStock,
                IsFeatured = dto.IsFeatured
            };

            _db.Products.Add(product);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = product.Id },
                ApiResponse<Product>.Ok(product, "Product created"));
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] CreateProductDto dto)
        {
            var product = await _db.Products.FindAsync(id);
            if (product == null || product.BranchId != CurrentBranchId)
                return NotFound(ApiResponse<Product>.Fail("Product not found"));

            product.CategoryId = dto.CategoryId;
            product.UnitId = dto.UnitId;
            product.Name = dto.Name;
            product.Description = dto.Description;
            product.Barcode = dto.Barcode;
            product.ImageUrl = dto.ImageUrl;
            product.PurchasePrice = dto.PurchasePrice;
            product.SalePrice = dto.SalePrice;
            product.TaxRate = dto.TaxRate;
            product.MinimumStock = dto.MinimumStock;
            product.IsFeatured = dto.IsFeatured;
            product.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Ok(ApiResponse<Product>.Ok(product, "Product updated"));
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _db.Products.FindAsync(id);
            if (product == null || product.BranchId != CurrentBranchId)
                return NotFound(ApiResponse<Product>.Fail("Product not found"));

            product.IsActive = false;
            product.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(null!, "Product deleted"));
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpPost("{id}/stock-adjustment")]
        public async Task<IActionResult> AdjustStock(int id, [FromBody] StockAdjustmentDto dto)
        {
            var success = await _productService.UpdateStockAsync(id, dto.Quantity, dto.Type, CurrentUserId);
            if (!success) return NotFound(ApiResponse<object>.Fail("Product not found"));
            return Ok(ApiResponse<object>.Ok(null!, "Stock updated"));
        }
    }

    // ============================================
    // CATEGORIES CONTROLLER
    // ============================================
    [Authorize]
    [Route("api/categories")]
    [ApiController]
    public class CategoriesController : BaseController
    {
        private readonly PosDbContext _db;
        public CategoriesController(PosDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var cats = await _db.Categories
                .Where(c => c.BranchId == CurrentBranchId && c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ToListAsync();
            return Ok(ApiResponse<IEnumerable<Category>>.Ok(cats));
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CategoryDto dto)
        {
            var cat = new Category
            {
                BranchId = CurrentBranchId,
                Name = dto.Name,
                Icon = dto.Icon,
                Color = dto.Color,
                SortOrder = dto.SortOrder
            };
            _db.Categories.Add(cat);
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<Category>.Ok(cat, "Category created"));
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] CategoryDto dto)
        {
            var cat = await _db.Categories.FindAsync(id);
            if (cat == null) return NotFound();
            cat.Name = dto.Name;
            cat.Icon = dto.Icon;
            cat.Color = dto.Color;
            cat.SortOrder = dto.SortOrder;
            cat.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<Category>.Ok(cat));
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var cat = await _db.Categories.FindAsync(id);
            if (cat == null) return NotFound();
            cat.IsActive = false;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(null!, "Deleted"));
        }
    }

    // ============================================
    // UNITS CONTROLLER
    // ============================================
    [Authorize]
    [Route("api/units")]
    [ApiController]
    public class UnitsController : BaseController
    {
        private readonly PosDbContext _db;
        public UnitsController(PosDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var units = await _db.Units.Where(u => u.IsActive).ToListAsync();
            return Ok(ApiResponse<IEnumerable<Unit>>.Ok(units));
        }
    }

    // ============================================
    // ORDERS CONTROLLER
    // ============================================
    [Authorize]
    [Route("api/orders")]
    [ApiController]
    public class OrdersController : BaseController
    {
        private readonly IOrderService _orderService;
        private readonly PosDbContext _db;

        public OrdersController(IOrderService orderService, PosDbContext db)
        {
            _orderService = orderService;
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var query = _db.Orders
                .Include(o => o.Customer)
                .Include(o => o.User)
                .Include(o => o.Table)
                .Where(o => o.BranchId == CurrentBranchId);

            if (startDate.HasValue) query = query.Where(o => o.CreatedAt >= startDate.Value);
            if (endDate.HasValue) query = query.Where(o => o.CreatedAt <= endDate.Value.AddDays(1));
            if (!string.IsNullOrEmpty(status)) query = query.Where(o => o.Status == status);

            var total = await query.CountAsync();
            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(ApiResponse<IEnumerable<Order>>.Ok(orders, totalCount: total));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(long id)
        {
            var order = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Payments)
                .Include(o => o.Customer)
                .Include(o => o.User)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.Id == id && o.BranchId == CurrentBranchId);

            if (order == null) return NotFound(ApiResponse<Order>.Fail("Order not found"));
            return Ok(ApiResponse<Order>.Ok(order));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrderDto dto)
        {
            try
            {
                var order = await _orderService.CreateOrderAsync(dto, CurrentUserId, CurrentBranchId);
                return CreatedAtAction(nameof(GetById), new { id = order.Id },
                    ApiResponse<Order>.Ok(order, "Order created"));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        [HttpPost("{id}/complete")]
        public async Task<IActionResult> Complete(long id)
        {
            try
            {
                var order = await _orderService.CompleteOrderAsync(id);
                return Ok(ApiResponse<Order>.Ok(order, "Order completed"));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpPost("{id}/void")]
        public async Task<IActionResult> Void(long id, [FromBody] string reason)
        {
            var order = await _orderService.VoidOrderAsync(id, reason);
            return Ok(ApiResponse<Order>.Ok(order, "Order voided"));
        }

        [HttpPost("{id}/kot")]
        public async Task<IActionResult> SendKOT(long id)
        {
            var kotData = await _orderService.GenerateKOTAsync(id);
            return Ok(ApiResponse<string>.Ok(kotData, "KOT generated"));
        }

        [HttpGet("{id}/receipt")]
        public async Task<IActionResult> GetReceipt(long id)
        {
            var receipt = await _orderService.GenerateReceiptAsync(id);
            return Ok(ApiResponse<string>.Ok(receipt));
        }

        [HttpPost("{id}/hold")]
        public async Task<IActionResult> Hold(long id, [FromBody] string holdName)
        {
            var order = await _orderService.HoldOrderAsync(id, holdName);
            return Ok(ApiResponse<Order>.Ok(order, "Order held"));
        }

        [HttpGet("held")]
        public async Task<IActionResult> GetHeldOrders()
        {
            var held = await _db.HeldOrders
                .Where(h => h.BranchId == CurrentBranchId)
                .OrderByDescending(h => h.CreatedAt)
                .ToListAsync();
            return Ok(ApiResponse<IEnumerable<HeldOrder>>.Ok(held));
        }

        [HttpPost("held/{id}/resume")]
        public async Task<IActionResult> ResumeHeld(int id)
        {
            var order = await _orderService.ResumeOrderAsync(id);
            return Ok(ApiResponse<Order>.Ok(order, "Order resumed"));
        }
    }

    // ============================================
    // CUSTOMERS CONTROLLER
    // ============================================
    [Authorize]
    [Route("api/customers")]
    [ApiController]
    public class CustomersController : BaseController
    {
        private readonly PosDbContext _db;
        public CustomersController(PosDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            var query = _db.Customers
                .Where(c => c.BranchId == CurrentBranchId && c.IsActive);

            if (!string.IsNullOrEmpty(search))
                query = query.Where(c =>
                    c.Name.Contains(search) ||
                    (c.Phone != null && c.Phone.Contains(search)));

            var total = await query.CountAsync();
            var items = await query
                .OrderBy(c => c.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(ApiResponse<IEnumerable<Customer>>.Ok(items, totalCount: total));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var c = await _db.Customers.FindAsync(id);
            return c == null ? NotFound() : Ok(ApiResponse<Customer>.Ok(c));
        }

        [HttpGet("{id}/ledger")]
        public async Task<IActionResult> GetLedger(
            int id,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate)
        {
            var query = _db.CustomerLedgers.Where(l => l.CustomerId == id);
            if (startDate.HasValue) query = query.Where(l => l.TransactionDate >= startDate);
            if (endDate.HasValue) query = query.Where(l => l.TransactionDate <= endDate.Value.AddDays(1));
            var ledger = await query.OrderByDescending(l => l.TransactionDate).ToListAsync();
            return Ok(ApiResponse<IEnumerable<CustomerLedger>>.Ok(ledger));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CustomerDto dto)
        {
            var customer = new Customer
            {
                BranchId = CurrentBranchId,
                Name = dto.Name,
                Phone = dto.Phone,
                Email = dto.Email,
                Address = dto.Address,
                CreditLimit = dto.CreditLimit
            };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<Customer>.Ok(customer, "Customer created"));
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] CustomerDto dto)
        {
            var c = await _db.Customers.FindAsync(id);
            if (c == null) return NotFound();
            c.Name = dto.Name;
            c.Phone = dto.Phone;
            c.Email = dto.Email;
            c.Address = dto.Address;
            c.CreditLimit = dto.CreditLimit;
            c.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<Customer>.Ok(c));
        }

        [HttpPost("{id}/payment")]
        public async Task<IActionResult> RecordPayment(int id, [FromBody] CustomerPaymentDto dto)
        {
            var customer = await _db.Customers.FindAsync(id);
            if (customer == null) return NotFound();

            var ledger = new CustomerLedger
            {
                CustomerId = id,
                TransactionType = "Payment",
                Credit = dto.Amount,
                Balance = customer.CurrentBalance - dto.Amount,
                Notes = dto.Notes,
                TransactionDate = DateTime.UtcNow
            };

            customer.CurrentBalance -= dto.Amount;
            _db.CustomerLedgers.Add(ledger);
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(null!, "Payment recorded"));
        }
    }

    // ============================================
    // SUPPLIERS CONTROLLER
    // ============================================
    [Authorize]
    [Route("api/suppliers")]
    [ApiController]
    public class SuppliersController : BaseController
    {
        private readonly PosDbContext _db;
        public SuppliersController(PosDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? search = null)
        {
            var query = _db.Suppliers.Where(s => s.BranchId == CurrentBranchId && s.IsActive);
            if (!string.IsNullOrEmpty(search))
                query = query.Where(s => s.Name.Contains(search));
            var items = await query.OrderBy(s => s.Name).ToListAsync();
            return Ok(ApiResponse<IEnumerable<Supplier>>.Ok(items));
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SupplierDto dto)
        {
            var supplier = new Supplier
            {
                BranchId = CurrentBranchId,
                Name = dto.Name,
                Company = dto.Company,
                Phone = dto.Phone,
                Email = dto.Email,
                Address = dto.Address
            };
            _db.Suppliers.Add(supplier);
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<Supplier>.Ok(supplier, "Supplier created"));
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] SupplierDto dto)
        {
            var s = await _db.Suppliers.FindAsync(id);
            if (s == null) return NotFound();
            s.Name = dto.Name; s.Company = dto.Company; s.Phone = dto.Phone;
            s.Email = dto.Email; s.Address = dto.Address; s.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<Supplier>.Ok(s));
        }

        [HttpGet("{id}/ledger")]
        public async Task<IActionResult> GetLedger(int id)
        {
            var ledger = await _db.SupplierLedger
                .Where(l => l.SupplierId == id)
                .OrderByDescending(l => l.TransactionDate)
                .ToListAsync();
            return Ok(ApiResponse<IEnumerable<SupplierLedger>>.Ok(ledger));
        }
    }

    // ============================================
    // PURCHASES CONTROLLER
    // ============================================
    [Authorize(Roles = "Admin,Manager")]
    [Route("api/purchases")]
    [ApiController]
    public class PurchasesController : BaseController
    {
        private readonly PosDbContext _db;
        public PurchasesController(PosDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var items = await _db.Purchases
                .Include(p => p.Supplier)
                .Include(p => p.User)
                .Where(p => p.BranchId == CurrentBranchId)
                .OrderByDescending(p => p.PurchaseDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            return Ok(ApiResponse<IEnumerable<Purchase>>.Ok(items));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePurchaseDto dto)
        {
            try
            {
                // Validate input
                if (dto.SupplierId <= 0)
                    return BadRequest(ApiResponse<object>.Fail("Valid SupplierId is required"));
                if (dto.Items == null || dto.Items.Count == 0)
                    return BadRequest(ApiResponse<object>.Fail("At least one item is required"));

                var purchase = new Purchase
                {
                    BranchId = CurrentBranchId,
                    SupplierId = dto.SupplierId,
                    UserId = CurrentUserId,
                    PurchaseNumber = $"PO-{DateTime.Now:yyyyMMdd-HHmmss}",
                    Notes = dto.Notes,
                    PaidAmount = dto.PaidAmount,
                    PaymentMethod = dto.PaymentMethod,
                    PurchaseDate = DateTime.UtcNow
                };

                decimal subTotal = 0;

                foreach (var item in dto.Items)
                {
                    var product = await _db.Products.FindAsync(item.ProductId);
                    if (product == null) 
                        return BadRequest(ApiResponse<object>.Fail($"Product with ID {item.ProductId} not found"));

                    var lineTotal = item.Quantity * item.UnitCost;
                    purchase.Items.Add(new PurchaseItem
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        UnitCost = item.UnitCost,
                        TotalAmount = lineTotal
                    });
                    subTotal += lineTotal;

                    var before = product.CurrentStock;
                    product.CurrentStock += item.Quantity;
                    product.PurchasePrice = item.UnitCost;
                    product.UpdatedAt = DateTime.UtcNow;

                    _db.StockTransactions.Add(new StockTransaction
                    {
                        BranchId = CurrentBranchId,
                        ProductId = item.ProductId,
                        UserId = CurrentUserId,
                        TransactionType = "StockIn",
                        ReferenceType = "Purchase",
                        Quantity = item.Quantity,
                        QuantityBefore = before,
                        QuantityAfter = product.CurrentStock,
                        UnitCost = item.UnitCost
                    });
                }

                purchase.SubTotal = subTotal;
                purchase.TotalAmount = subTotal;
                purchase.DueAmount = subTotal - dto.PaidAmount;

                _db.Purchases.Add(purchase);

                var supplier = await _db.Suppliers.FindAsync(dto.SupplierId);
                if (supplier == null)
                    return BadRequest(ApiResponse<object>.Fail($"Supplier with ID {dto.SupplierId} not found"));

                supplier.CurrentBalance += purchase.DueAmount;
                _db.SupplierLedger.Add(new SupplierLedger
                {
                    SupplierId = dto.SupplierId,
                    TransactionType = "Purchase",
                    Debit = purchase.TotalAmount,
                    Credit = dto.PaidAmount,
                    Balance = supplier.CurrentBalance,
                    TransactionDate = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
                return Ok(ApiResponse<Purchase>.Ok(purchase, "Purchase recorded successfully"));
            }
            catch (DbUpdateException dbEx)
            {
                var innerMsg = dbEx.InnerException?.Message ?? dbEx.Message;
                return BadRequest(ApiResponse<object>.Fail($"Database error: {innerMsg}"));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail($"Error: {ex.Message}"));
            }
        }
    }

    // ============================================
    // EXPENSES CONTROLLER
    // ============================================
    [Authorize]
    [Route("api/expenses")]
    [ApiController]
    public class ExpensesController : BaseController
    {
        private readonly PosDbContext _db;
        public ExpensesController(PosDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] int? categoryId = null)
        {
            var query = _db.Expenses
                .Include(e => e.Category)
                .Include(e => e.User)
                .Where(e => e.BranchId == CurrentBranchId);

            if (startDate.HasValue) query = query.Where(e => e.ExpenseDate >= startDate);
            if (endDate.HasValue) query = query.Where(e => e.ExpenseDate <= endDate.Value.AddDays(1));
            if (categoryId.HasValue) query = query.Where(e => e.CategoryId == categoryId.Value);

            var items = await query.OrderByDescending(e => e.ExpenseDate).ToListAsync();
            return Ok(ApiResponse<IEnumerable<Expense>>.Ok(items));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateExpenseDto dto)
        {
            var expense = new Expense
            {
                BranchId = CurrentBranchId,
                CategoryId = dto.CategoryId,
                UserId = CurrentUserId,
                Title = dto.Title,
                Amount = dto.Amount,
                PaymentMethod = dto.PaymentMethod,
                Notes = dto.Notes,
                ExpenseDate = dto.ExpenseDate ?? DateTime.UtcNow
            };
            _db.Expenses.Add(expense);
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<Expense>.Ok(expense, "Expense added"));
        }

        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories()
        {
            var cats = await _db.ExpenseCategories
                .Where(c => c.BranchId == CurrentBranchId && c.IsActive)
                .ToListAsync();
            return Ok(ApiResponse<IEnumerable<ExpenseCategory>>.Ok(cats));
        }
    }

    // ============================================
    // DASHBOARD CONTROLLER
    // ============================================
    [Authorize]
    [Route("api/dashboard")]
    [ApiController]
    public class DashboardController : BaseController
    {
        private readonly IDashboardService _dashboardService;
        public DashboardController(IDashboardService dashboardService)
            => _dashboardService = dashboardService;

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var data = await _dashboardService.GetDashboardDataAsync(CurrentBranchId);
            return Ok(ApiResponse<DashboardDto>.Ok(data));
        }
    }

    // ============================================
    // REPORTS CONTROLLER
    // ============================================
    [Authorize]
    [Route("api/reports")]
    [ApiController]
    public class ReportsController : BaseController
    {
        private readonly IReportService _reportService;
        public ReportsController(IReportService reportService)
            => _reportService = reportService;

        [HttpGet("sales")]
        public async Task<IActionResult> Sales(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate)
        {
            var report = await _reportService.GetSalesReportAsync(CurrentBranchId, startDate, endDate);
            return Ok(ApiResponse<SalesReportDto>.Ok(report));
        }

        [HttpGet("stock")]
        public async Task<IActionResult> Stock()
        {
            var report = await _reportService.GetStockReportAsync(CurrentBranchId);
            return Ok(ApiResponse<object>.Ok(report));
        }

        [HttpGet("expenses")]
        public async Task<IActionResult> Expenses(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate)
        {
            var report = await _reportService.GetExpenseReportAsync(CurrentBranchId, startDate, endDate);
            return Ok(ApiResponse<object>.Ok(report));
        }

        [HttpGet("profit-loss")]
        public async Task<IActionResult> ProfitLoss(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate)
        {
            var report = await _reportService.GetProfitLossAsync(CurrentBranchId, startDate, endDate);
            return Ok(ApiResponse<object>.Ok(report));
        }

        [HttpGet("daily-closing")]
        public async Task<IActionResult> DailyClosing([FromQuery] DateTime? date)
        {
            var report = await _reportService.GetDailyClosingAsync(
                CurrentBranchId, date ?? DateTime.Today);
            return Ok(ApiResponse<object>.Ok(report));
        }
    }

    // ============================================
    // TABLES CONTROLLER
    // ============================================
    [Authorize]
    [Route("api/tables")]
    [ApiController]
    public class TablesController : BaseController
    {
        private readonly PosDbContext _db;
        public TablesController(PosDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var tables = await _db.RestaurantTables
                .Where(t => t.BranchId == CurrentBranchId && t.IsActive)
                .OrderBy(t => t.Section).ThenBy(t => t.TableNumber)
                .ToListAsync();
            return Ok(ApiResponse<IEnumerable<RestaurantTable>>.Ok(tables));
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Create([FromBody] RestaurantTable dto)
        {
            dto.BranchId = CurrentBranchId;
            dto.Status = "Available";
            _db.RestaurantTables.Add(dto);
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<RestaurantTable>.Ok(dto, "Table created"));
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Update(int id, [FromBody] RestaurantTable dto)
        {
            var t = await _db.RestaurantTables.FindAsync(id);
            if (t == null) return NotFound();
            t.TableNumber = dto.TableNumber;
            t.Capacity = dto.Capacity;
            t.Section = dto.Section;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<RestaurantTable>.Ok(t));
        }

        [HttpPatch("{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] string status)
        {
            var table = await _db.RestaurantTables.FindAsync(id);
            if (table == null) return NotFound();
            table.Status = status;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<RestaurantTable>.Ok(table));
        }
    }

    // ============================================
    // USERS CONTROLLER
    // ============================================
    [Authorize(Roles = "Admin")]
    [Route("api/users")]
    [ApiController]
    public class UsersController : BaseController
    {
        private readonly PosDbContext _db;
        public UsersController(PosDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var users = await _db.Users
                .Include(u => u.Role)
                .Where(u => u.BranchId == CurrentBranchId)
                .Select(u => new
                {
                    u.Id, u.FullName, u.Username, u.Email,
                    u.Phone, u.IsActive, u.LastLoginAt,
                    Role = u.Role!.Name
                })
                .ToListAsync();
            return Ok(ApiResponse<object>.Ok(users));
        }

        [HttpGet("roles")]
        public async Task<IActionResult> GetRoles()
        {
            var roles = await _db.Roles.Where(r => r.IsActive).ToListAsync();
            return Ok(ApiResponse<IEnumerable<Role>>.Ok(roles));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
        {
            if (await _db.Users.AnyAsync(u => u.Username == dto.Username))
                return BadRequest(ApiResponse<object>.Fail("Username already exists"));

            var user = new User
            {
                BranchId = CurrentBranchId,
                RoleId = dto.RoleId,
                FullName = dto.FullName,
                Username = dto.Username,
                Email = dto.Email,
                Phone = dto.Phone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(
                new { user.Id, user.FullName, user.Username }, "User created"));
        }
    }

    // ============================================
    // BRANCH CONTROLLER — GET + UPDATE branch info
    // ============================================
    [Authorize]
    [Route("api/branch")]
    [ApiController]
    public class BranchController : BaseController
    {
        private readonly PosDbContext _db;
        public BranchController(PosDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var branch = await _db.Branches.FindAsync(CurrentBranchId);
            if (branch == null) return NotFound(ApiResponse<object>.Fail("Branch not found"));
            return Ok(ApiResponse<object>.Ok(new {
                branch.Id, branch.Name, branch.Address,
                branch.Phone, branch.Email, branch.Logo,
                branch.TaxNumber
            }));
        }

        [Authorize(Roles = "Admin")]
        [HttpPut]
        public async Task<IActionResult> Update([FromBody] UpdateBranchDto dto)
        {
            var branch = await _db.Branches.FindAsync(CurrentBranchId);
            if (branch == null) return NotFound(ApiResponse<object>.Fail("Branch not found"));

            if (dto.Name    != null) branch.Name    = dto.Name;
            if (dto.Address != null) branch.Address = dto.Address;
            if (dto.Phone   != null) branch.Phone   = dto.Phone;
            if (dto.Email   != null) branch.Email   = dto.Email;
            if (dto.Logo    != null) branch.Logo    = dto.Logo;
            if (dto.TaxNumber != null) branch.TaxNumber = dto.TaxNumber;

            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(new {
                branch.Id, branch.Name, branch.Address,
                branch.Phone, branch.Email, branch.Logo,
                branch.TaxNumber
            }, "Branch updated"));
        }
    }

    // ============================================
    // SYNC CONTROLLER
    // ============================================
    [Authorize]
    [Route("api/sync")]
    [ApiController]
    public class SyncController : BaseController
    {
        private readonly ISyncService _syncService;
        public SyncController(ISyncService syncService)
            => _syncService = syncService;

        [HttpPost("push")]
        public async Task<IActionResult> Push([FromBody] List<SyncQueue> items)
        {
            var result = await _syncService.ProcessSyncQueueAsync(items);
            return Ok(ApiResponse<bool>.Ok(result, "Sync processed"));
        }

        [HttpGet("pull")]
        public async Task<IActionResult> Pull([FromQuery] DateTime lastSyncAt)
        {
            var changes = await _syncService.GetServerChangesAsync(CurrentBranchId, lastSyncAt);
            return Ok(ApiResponse<object>.Ok(changes));
        }
    }
}
