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
            [FromQuery] bool? lowStock = null,
            [FromQuery] int? productTypeId = null)
        {
            var query = _db.Products.AsNoTracking()
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

            if (productTypeId.HasValue)
                query = query.Where(p => p.ProductTypeId == productTypeId.Value);

            if (lowStock == true)
                query = query.Where(p => p.ProductTypeId == ProductTypeIds.Inventory && p.CurrentStock <= p.MinimumStock);

            var total = await query.CountAsync();
            var items = await query
                .OrderBy(p => p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            await ApplyAvailableStockAsync(items);   // Recipe / Deal: stock = portions possible from ingredients

            return Ok(ApiResponse<IEnumerable<Product>>.Ok(items, totalCount: total));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var product = await _db.Products.AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.Unit)
                .Include(p => p.Components).ThenInclude(c => c.ComponentProduct).ThenInclude(cp => cp!.Unit)
                .FirstOrDefaultAsync(p => p.Id == id && p.BranchId == CurrentBranchId);

            if (product == null)
                return NotFound(ApiResponse<Product>.Fail("Product not found"));

            await ApplyAvailableStockAsync(new List<Product> { product });

            return Ok(ApiResponse<Product>.Ok(product));
        }

        [HttpGet("barcode/{barcode}")]
        public async Task<IActionResult> GetByBarcode(string barcode)
        {
            var product = await _productService.GetByBarcodeAsync(barcode);
            if (product == null)
                return NotFound(ApiResponse<Product>.Fail("Product not found"));

            await ApplyAvailableStockAsync(new List<Product> { product });

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
            var (error, components) = await BuildComponentsAsync(null, dto);
            if (error != null) return BadRequest(ApiResponse<Product>.Fail(error));

            var product = new Product
            {
                BranchId = CurrentBranchId,
                ProductTypeId = dto.ProductTypeId,
                Components = components,
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

            var (error, components) = await BuildComponentsAsync(id, dto);
            if (error != null) return BadRequest(ApiResponse<Product>.Fail(error));

            // A product that is already used inside other recipes/deals must keep a type that is allowed there
            if (dto.ProductTypeId != product.ProductTypeId)
            {
                // (join instead of list.Contains -> works on every SQL Server version)
                var parentTypes = await _db.ProductComponents
                    .Where(x => x.ComponentProductId == id)
                    .Join(_db.Products, x => x.ParentProductId, p => p.Id, (x, p) => p.ProductTypeId)
                    .ToListAsync();
                if (parentTypes.Count > 0)
                {
                    var allowed = dto.ProductTypeId == ProductTypeIds.Inventory
                        || (dto.ProductTypeId == ProductTypeIds.Recipe && !parentTypes.Contains(ProductTypeIds.Recipe));
                    if (!allowed)
                        return BadRequest(ApiResponse<Product>.Fail(
                            "This product is used inside other recipes/deals, so its type cannot be changed"));
                }
            }

            // Sync recipe / deal items: update quantities, remove deleted rows, add new rows
            var oldItems = await _db.ProductComponents.Where(x => x.ParentProductId == id).ToListAsync();
            foreach (var removed in oldItems.Where(o => !components.Any(n => n.ComponentProductId == o.ComponentProductId)))
                _db.ProductComponents.Remove(removed);
            foreach (var item in components)
            {
                var existing = oldItems.FirstOrDefault(o => o.ComponentProductId == item.ComponentProductId);
                if (existing != null) existing.Quantity = item.Quantity;
                else
                {
                    item.ParentProductId = id;
                    _db.ProductComponents.Add(item);
                }
            }

            product.ProductTypeId = dto.ProductTypeId;
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

            var usedInActive = await _db.ProductComponents.AnyAsync(x =>
                x.ComponentProductId == id &&
                _db.Products.Any(p => p.Id == x.ParentProductId && p.IsActive));
            if (usedInActive)
                return BadRequest(ApiResponse<Product>.Fail(
                    "This product is used in a recipe or deal. Remove it from there first."));

            product.IsActive = false;
            product.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(null!, "Product deleted"));
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpPost("{id}/stock-adjustment")]
        public async Task<IActionResult> AdjustStock(int id, [FromBody] StockAdjustmentDto dto)
        {
            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.BranchId == CurrentBranchId);
            if (product != null && product.ProductTypeId != ProductTypeIds.Inventory)
                return BadRequest(ApiResponse<object>.Fail("Stock can only be adjusted for inventory products"));

            var success = await _productService.UpdateStockAsync(id, dto.Quantity, dto.Type, CurrentUserId);
            if (!success) return NotFound(ApiResponse<object>.Fail("Product not found"));
            return Ok(ApiResponse<object>.Ok(null!, "Stock updated"));
        }

        // Recipe / Deal products have no stock of their own: show how many can be made from ingredient stock
        private async Task ApplyAvailableStockAsync(List<Product> items)
        {
            if (!items.Any(p => p.ProductTypeId != ProductTypeIds.Inventory)) return;
            var graph = await StockExpander.LoadAsync(_db, CurrentBranchId);
            foreach (var p in items.Where(p => p.ProductTypeId != ProductTypeIds.Inventory))
                p.CurrentStock = StockExpander.Available(p.Id, graph);
        }

        // Validates the type + items of a product and builds the recipe / deal rows
        private async Task<(string? Error, List<ProductComponent> Items)> BuildComponentsAsync(int? productId, CreateProductDto dto)
        {
            var items = new List<ProductComponent>();

            if (!await _db.ProductTypes.AnyAsync(t => t.Id == dto.ProductTypeId))
                return ("Invalid product type", items);

            if (dto.ProductTypeId == ProductTypeIds.Inventory)
                return (null, items);   // inventory products have no recipe / deal items

            var isRecipe = dto.ProductTypeId == ProductTypeIds.Recipe;
            var rows = dto.Components ?? new List<ProductComponentDto>();

            if (rows.Count == 0)
                return (isRecipe ? "Add at least one ingredient to the recipe" : "Add at least one item to the deal", items);
            if (rows.Any(r => r.Quantity <= 0))
                return ("Quantity must be greater than zero", items);
            if (rows.GroupBy(r => r.ComponentProductId).Any(g => g.Count() > 1))
                return ("The same item is added more than once", items);
            if (productId.HasValue && rows.Any(r => r.ComponentProductId == productId.Value))
                return ("A product cannot contain itself", items);

            var ids = rows.Select(r => r.ComponentProductId).ToList();
            // (filtered in memory instead of list.Contains -> works on every SQL Server version)
            var branchProducts = await _db.Products.AsNoTracking()
                .Where(p => p.BranchId == CurrentBranchId && p.IsActive)
                .Select(p => new { p.Id, p.ProductTypeId })
                .ToListAsync();
            var found = branchProducts.Where(p => ids.Contains(p.Id)).ToList();

            if (found.Count != ids.Count)
                return ("One or more selected items were not found", items);

            var allowed = isRecipe
                ? new[] { ProductTypeIds.Inventory }
                : new[] { ProductTypeIds.Inventory, ProductTypeIds.Recipe };
            if (found.Any(f => !allowed.Contains(f.ProductTypeId)))
                return (isRecipe
                    ? "A recipe can only contain inventory products"
                    : "A deal can only contain inventory or recipe products", items);

            items = rows.Select(r => new ProductComponent
            {
                ComponentProductId = r.ComponentProductId,
                Quantity = r.Quantity
            }).ToList();
            return (null, items);
        }
    }

    // ============================================
    // PRODUCT TYPES CONTROLLER (read only — fixed list)
    // ============================================
    [Authorize]
    [Route("api/product-types")]
    [ApiController]
    public class ProductTypesController : BaseController
    {
        private readonly PosDbContext _db;
        public ProductTypesController(PosDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var types = await _db.ProductTypes.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
            return Ok(ApiResponse<IEnumerable<ProductType>>.Ok(types));
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
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var query = _db.Orders.AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.User)
                .Include(o => o.Table)
                .Include(o => o.Agent)
                .Include(o => o.Payments)
                .Where(o => o.BranchId == CurrentBranchId);

            // Dates are business dates (the day the sale belongs to), both ends included
            if (startDate.HasValue) query = query.Where(o => o.BusinessDate >= startDate.Value.Date);
            if (endDate.HasValue) query = query.Where(o => o.BusinessDate <= endDate.Value.Date);
            if (!string.IsNullOrEmpty(status)) query = query.Where(o => o.Status == status);
            if (!string.IsNullOrEmpty(search))
                query = query.Where(o => o.OrderNumber.Contains(search) ||
                                         (o.Customer != null && o.Customer.Name.Contains(search)));

            var total = await query.CountAsync();
            var orders = await query
                .OrderByDescending(o => o.OrderedAt)
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
                .Include(o => o.Agent)
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
        public async Task<IActionResult> Void(long id, [FromBody] string? reason)
        {
            try
            {
                var order = await _orderService.VoidOrderAsync(id, reason ?? string.Empty);
                return Ok(ApiResponse<Order>.Ok(order, "Order voided"));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
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
    // SALES AGENTS CONTROLLER (Waiter / Rider)
    // ============================================
    [Authorize]
    [Route("api/sales-agents")]
    [ApiController]
    public class SalesAgentsController : BaseController
    {
        private static readonly string[] AgentTypes = { "Waiter", "Rider" };
        private readonly PosDbContext _db;
        public SalesAgentsController(PosDbContext db) => _db = db;

        // GET api/sales-agents?type=Waiter|Rider&search=
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? type = null, [FromQuery] string? search = null)
        {
            var query = _db.SalesAgents.Where(a => a.BranchId == CurrentBranchId && a.IsActive);

            if (!string.IsNullOrEmpty(type)) query = query.Where(a => a.AgentType == type);
            if (!string.IsNullOrEmpty(search))
                query = query.Where(a => a.Name.Contains(search) || (a.Phone != null && a.Phone.Contains(search)));

            var items = await query.OrderBy(a => a.Name).ToListAsync();
            return Ok(ApiResponse<IEnumerable<SalesAgent>>.Ok(items));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var a = await _db.SalesAgents.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == CurrentBranchId);
            return a == null
                ? NotFound(ApiResponse<SalesAgent>.Fail("Agent not found"))
                : Ok(ApiResponse<SalesAgent>.Ok(a));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SalesAgentDto dto)
        {
            var error = Validate(dto);
            if (error != null) return BadRequest(ApiResponse<SalesAgent>.Fail(error));

            var agent = new SalesAgent
            {
                BranchId = CurrentBranchId,
                Name = dto.Name.Trim(),
                Phone = dto.Phone,
                AgentType = dto.AgentType,
                CommissionAmount = dto.CommissionAmount
            };
            _db.SalesAgents.Add(agent);
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<SalesAgent>.Ok(agent, "Agent created"));
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] SalesAgentDto dto)
        {
            var error = Validate(dto);
            if (error != null) return BadRequest(ApiResponse<SalesAgent>.Fail(error));

            var agent = await _db.SalesAgents.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == CurrentBranchId);
            if (agent == null) return NotFound(ApiResponse<SalesAgent>.Fail("Agent not found"));

            // Commission change sirf aage ke orders par lagega, purane orders par nahi
            agent.Name = dto.Name.Trim();
            agent.Phone = dto.Phone;
            agent.AgentType = dto.AgentType;
            agent.CommissionAmount = dto.CommissionAmount;
            agent.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<SalesAgent>.Ok(agent, "Agent updated"));
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var agent = await _db.SalesAgents.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == CurrentBranchId);
            if (agent == null) return NotFound(ApiResponse<SalesAgent>.Fail("Agent not found"));

            agent.IsActive = false;   // soft delete, purani sales ka record bacha rahe
            agent.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(null!, "Agent deleted"));
        }

        private static string? Validate(SalesAgentDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required";
            if (!AgentTypes.Contains(dto.AgentType)) return "Agent type must be Waiter or Rider";
            if (dto.CommissionAmount < 0) return "Commission cannot be negative";
            return null;
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
            var report = await _reportService.GetDailyClosingAsync(CurrentBranchId, date);
            return Ok(ApiResponse<object>.Ok(report));
        }
    }

    // ============================================
    // BUSINESS DAYS CONTROLLER (Day Open / Day Close)
    // ============================================
    [Authorize]
    [Route("api/business-days")]
    [ApiController]
    public class BusinessDaysController : BaseController
    {
        private readonly PosDbContext _db;
        public BusinessDaysController(PosDbContext db) => _db = db;

        // GET api/business-days/current?withSummary=true  -> is a day open right now?
        [HttpGet("current")]
        public async Task<IActionResult> Current([FromQuery] bool withSummary = false)
        {
            var open = await BusinessDayHelper.GetOpenAsync(_db, CurrentBranchId);
            DaySummary? summary = null;
            if (withSummary && open != null)
                summary = await BusinessDayHelper.BuildSummaryAsync(_db, CurrentBranchId, open.BusinessDate.Date, open);

            return Ok(ApiResponse<object>.Ok(new
            {
                isOpen = open != null,
                day = open,
                summary,
                suggestedDate = BusinessTime.LocalToday()
            }));
        }

        // GET api/business-days  -> history (newest first)
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 30)
        {
            var query = _db.CashRegisterSessions.AsNoTracking().Where(s => s.BranchId == CurrentBranchId);
            var total = await query.CountAsync();
            var days = await query
                .OrderByDescending(s => s.OpenedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var userRows = await _db.Users.AsNoTracking()
                .Where(u => u.BranchId == CurrentBranchId)
                .Select(u => new { u.Id, u.FullName })
                .ToListAsync();
            var names = userRows.ToDictionary(u => u.Id, u => u.FullName);

            var items = days.Select(s => new
            {
                s.Id, s.BusinessDate, s.Status, s.OpenedAt, s.ClosedAt,
                s.OpeningBalance, s.ClosingBalance, s.ExpectedCash,
                difference = s.ClosingBalance.HasValue && s.ExpectedCash.HasValue ? s.ClosingBalance.Value - s.ExpectedCash.Value : (decimal?)null,
                s.CashSales, s.CardSales, s.WalletSales, s.TotalExpenses,
                s.Notes, s.CloseNotes,
                openedBy = names.TryGetValue(s.UserId, out var o) ? o : null,
                closedBy = s.ClosedByUserId.HasValue && names.TryGetValue(s.ClosedByUserId.Value, out var cb) ? cb : null
            }).ToList();

            return Ok(ApiResponse<object>.Ok(items, totalCount: total));
        }

        // GET api/business-days/5/summary
        [HttpGet("{id}/summary")]
        public async Task<IActionResult> Summary(int id)
        {
            var day = await _db.CashRegisterSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id && s.BranchId == CurrentBranchId);
            if (day == null) return NotFound(ApiResponse<object>.Fail("Business day not found"));

            var summary = await BusinessDayHelper.BuildSummaryAsync(_db, CurrentBranchId, day.BusinessDate.Date, day);
            return Ok(ApiResponse<object>.Ok(new { day, summary }));
        }

        // POST api/business-days/open
        [HttpPost("open")]
        public async Task<IActionResult> Open([FromBody] OpenDayDto dto)
        {
            if (dto.OpeningBalance < 0)
                return BadRequest(ApiResponse<object>.Fail("Opening cash cannot be negative"));

            var alreadyOpen = await BusinessDayHelper.GetOpenAsync(_db, CurrentBranchId);
            if (alreadyOpen != null)
                return BadRequest(ApiResponse<object>.Fail(
                    $"The day of {alreadyOpen.BusinessDate:dd MMM yyyy} is still open. Close it first."));

            var date = (dto.BusinessDate ?? BusinessTime.LocalToday()).Date;
            if (date > BusinessTime.LocalToday().AddDays(1))
                return BadRequest(ApiResponse<object>.Fail("Business date cannot be in the future"));

            var exists = await _db.CashRegisterSessions.AnyAsync(s => s.BranchId == CurrentBranchId && s.BusinessDate == date);
            if (exists)
                return BadRequest(ApiResponse<object>.Fail(
                    $"A day for {date:dd MMM yyyy} already exists. Reopen it from the history instead."));

            var session = new CashRegisterSession
            {
                BranchId = CurrentBranchId,
                UserId = CurrentUserId,
                OpeningBalance = dto.OpeningBalance,
                Status = "Open",
                OpenedAt = DateTime.UtcNow,
                BusinessDate = date,
                Notes = dto.Notes
            };
            _db.CashRegisterSessions.Add(session);
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<CashRegisterSession>.Ok(session, "Day opened"));
        }

        // POST api/business-days/5/close
        [HttpPost("{id}/close")]
        public async Task<IActionResult> Close(int id, [FromBody] CloseDayDto dto)
        {
            var day = await _db.CashRegisterSessions.FirstOrDefaultAsync(s => s.Id == id && s.BranchId == CurrentBranchId);
            if (day == null) return NotFound(ApiResponse<object>.Fail("Business day not found"));
            if (day.Status != "Open") return BadRequest(ApiResponse<object>.Fail("This day is already closed"));
            if (dto.ClosingBalance < 0) return BadRequest(ApiResponse<object>.Fail("Closing cash cannot be negative"));

            var summary = await BusinessDayHelper.BuildSummaryAsync(_db, CurrentBranchId, day.BusinessDate.Date, day);

            day.Status = "Closed";
            day.ClosedAt = DateTime.UtcNow;
            day.ClosedByUserId = CurrentUserId;
            day.ClosingBalance = dto.ClosingBalance;
            day.ExpectedCash = summary.ExpectedCash;
            day.CashSales = summary.CashSales;
            day.CardSales = summary.CardSales;
            day.WalletSales = summary.WalletSales;
            day.TotalExpenses = summary.TotalExpenses;
            day.CloseNotes = dto.Notes;
            await _db.SaveChangesAsync();

            return Ok(ApiResponse<object>.Ok(new
            {
                day,
                summary,
                difference = dto.ClosingBalance - summary.ExpectedCash
            }, "Day closed"));
        }

        // POST api/business-days/5/reopen  (Admin / Manager)
        [Authorize(Roles = "Admin,Manager")]
        [HttpPost("{id}/reopen")]
        public async Task<IActionResult> Reopen(int id)
        {
            var day = await _db.CashRegisterSessions.FirstOrDefaultAsync(s => s.Id == id && s.BranchId == CurrentBranchId);
            if (day == null) return NotFound(ApiResponse<object>.Fail("Business day not found"));
            if (day.Status != "Closed") return BadRequest(ApiResponse<object>.Fail("This day is not closed"));

            var other = await BusinessDayHelper.GetOpenAsync(_db, CurrentBranchId);
            if (other != null)
                return BadRequest(ApiResponse<object>.Fail(
                    $"The day of {other.BusinessDate:dd MMM yyyy} is open. Close it before reopening another day."));

            day.Status = "Open";
            day.ClosedAt = null;
            day.ClosedByUserId = null;
            day.ClosingBalance = null;
            day.ExpectedCash = null;
            day.CloseNotes = null;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<CashRegisterSession>.Ok(day, "Day reopened"));
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
