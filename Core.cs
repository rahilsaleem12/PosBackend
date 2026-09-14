// ============================================
// POS.Core — Entities, DTOs, Interfaces
// ============================================
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// ============================================
// ENTITIES
// ============================================
namespace POS.Core.Entities
{
    public abstract class BaseEntity
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }

    public class Branch : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Logo { get; set; }
        public string Currency { get; set; } = "PKR";
        public string? TaxNumber { get; set; }
    }

    public class Role
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class User : BaseEntity
    {
        public int BranchId { get; set; }
        public int RoleId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Avatar { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public Branch? Branch { get; set; }
        public Role? Role { get; set; }
    }

    public class UserActivityLog
    {
        public long Id { get; set; }
        public int UserId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public User? User { get; set; }
    }

    public class RefreshToken
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool IsRevoked { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public User? User { get; set; }
    }

    public class Category : BaseEntity
    {
        public int BranchId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Icon { get; set; }
        public string? Color { get; set; }
        public int SortOrder { get; set; } = 0;
        public ICollection<Product> Products { get; set; } = new List<Product>();
    }

    public class Unit
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Abbreviation { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    public class Product : BaseEntity
    {
        public int BranchId { get; set; }
        public int CategoryId { get; set; }
        public int UnitId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Barcode { get; set; }
        public string? SKU { get; set; }
        public string? ImageUrl { get; set; }
        public decimal PurchasePrice { get; set; } = 0;
        public decimal SalePrice { get; set; } = 0;
        public decimal MinimumPrice { get; set; } = 0;
        public decimal TaxRate { get; set; } = 0;
        public decimal CurrentStock { get; set; } = 0;
        public decimal MinimumStock { get; set; } = 5;
        public decimal MaximumStock { get; set; } = 1000;
        public bool IsFeatured { get; set; } = false;
        public Category? Category { get; set; }
        public Unit? Unit { get; set; }
    }

    public class Supplier : BaseEntity
    {
        public int BranchId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Company { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? TaxNumber { get; set; }
        public decimal OpeningBalance { get; set; } = 0;
        public decimal CurrentBalance { get; set; } = 0;
        public ICollection<Purchase> Purchases { get; set; } = new List<Purchase>();
    }

    public class SupplierLedger
    {
        public long Id { get; set; }
        public int SupplierId { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public int? ReferenceId { get; set; }
        public decimal Debit { get; set; } = 0;
        public decimal Credit { get; set; } = 0;
        public decimal Balance { get; set; } = 0;
        public string? Notes { get; set; }
        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
        public Supplier? Supplier { get; set; }
    }

    public class Purchase
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public int SupplierId { get; set; }
        public int UserId { get; set; }
        public string PurchaseNumber { get; set; } = string.Empty;
        public DateTime PurchaseDate { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "Received";
        public decimal SubTotal { get; set; } = 0;
        public decimal TaxAmount { get; set; } = 0;
        public decimal DiscountAmount { get; set; } = 0;
        public decimal TotalAmount { get; set; } = 0;
        public decimal PaidAmount { get; set; } = 0;
        public decimal DueAmount { get; set; } = 0;
        public string? PaymentMethod { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Supplier? Supplier { get; set; }
        public User? User { get; set; }
        public ICollection<PurchaseItem> Items { get; set; } = new List<PurchaseItem>();
    }

    public class PurchaseItem
    {
        public int Id { get; set; }
        public int PurchaseId { get; set; }
        public int ProductId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal TaxRate { get; set; } = 0;
        public decimal TaxAmount { get; set; } = 0;
        public decimal DiscountAmount { get; set; } = 0;
        public decimal TotalAmount { get; set; }
        public Product? Product { get; set; }
    }

    public class Customer : BaseEntity
    {
        public int BranchId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public decimal OpeningBalance { get; set; } = 0;
        public decimal CreditLimit { get; set; } = 0;
        public decimal CurrentBalance { get; set; } = 0;
        public int LoyaltyPoints { get; set; } = 0;
        public ICollection<Order> Orders { get; set; } = new List<Order>();
    }

    public class CustomerLedger
    {
        public long Id { get; set; }
        public int CustomerId { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public int? ReferenceId { get; set; }
        public decimal Debit { get; set; } = 0;
        public decimal Credit { get; set; } = 0;
        public decimal Balance { get; set; } = 0;
        public string? Notes { get; set; }
        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
        public Customer? Customer { get; set; }
    }

    public class RestaurantTable
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public string TableNumber { get; set; } = string.Empty;
        public int Capacity { get; set; } = 4;
        public string? Section { get; set; }
        public string Status { get; set; } = "Available";
        public bool IsActive { get; set; } = true;
    }

    public class Order
    {
        public long Id { get; set; }
        public int BranchId { get; set; }
        public int? CustomerId { get; set; }
        public int UserId { get; set; }
        public int? TableId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public string OrderType { get; set; } = "Takeaway";
        public string Status { get; set; } = "Pending";
        public DateTime? KOTSentAt { get; set; }
        public decimal SubTotal { get; set; } = 0;
        public decimal TaxAmount { get; set; } = 0;
        public string? DiscountType { get; set; }
        public decimal DiscountValue { get; set; } = 0;
        public decimal DiscountAmount { get; set; } = 0;
        public decimal TotalAmount { get; set; } = 0;
        public decimal PaidAmount { get; set; } = 0;
        public decimal DueAmount { get; set; } = 0;
        public decimal ChangeAmount { get; set; } = 0;
        public string? Notes { get; set; }
        public DateTime OrderedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Customer? Customer { get; set; }
        public User? User { get; set; }
        public RestaurantTable? Table { get; set; }
        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
        public ICollection<OrderPayment> Payments { get; set; } = new List<OrderPayment>();
    }

    public class OrderItem
    {
        public long Id { get; set; }
        public long OrderId { get; set; }
        public int ProductId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TaxRate { get; set; } = 0;
        public decimal TaxAmount { get; set; } = 0;
        public decimal DiscountAmount { get; set; } = 0;
        public decimal TotalAmount { get; set; }
        public string? Notes { get; set; }
        public bool IsKOTSent { get; set; } = false;
        public DateTime? KOTSentAt { get; set; }
        public Product? Product { get; set; }
    }

    public class OrderPayment
    {
        public int Id { get; set; }
        public long OrderId { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? Reference { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class HeldOrder
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public int UserId { get; set; }
        public string? HoldName { get; set; }
        public string OrderData { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class StockTransaction
    {
        public long Id { get; set; }
        public int BranchId { get; set; }
        public int ProductId { get; set; }
        public int UserId { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public string? ReferenceType { get; set; }
        public int? ReferenceId { get; set; }
        public decimal Quantity { get; set; }
        public decimal QuantityBefore { get; set; }
        public decimal QuantityAfter { get; set; }
        public decimal? UnitCost { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Product? Product { get; set; }
    }

    public class Expense
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public int CategoryId { get; set; }
        public int UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Reference { get; set; }
        public string? Notes { get; set; }
        public DateTime ExpenseDate { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ExpenseCategory? Category { get; set; }
        public User? User { get; set; }
    }

    public class ExpenseCategory
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class SyncQueue
    {
        public long Id { get; set; }
        public int? BranchId { get; set; }
        public string? DeviceId { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public string? EntityId { get; set; }
        public string Operation { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public int Attempts { get; set; } = 0;
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SyncedAt { get; set; }
    }
}

// ============================================
// DTOs
// ============================================
namespace POS.Core.DTOs
{
    public record LoginDto(string Username, string Password);

    public record TokenResponseDto(
        string AccessToken,
        string RefreshToken,
        DateTime ExpiresAt,
        UserDto User
    );

    public record UserDto(
        int Id,
        string FullName,
        string Username,
        string? Email,
        string Role,
        int BranchId,
        string? BranchName,
        string? Avatar
    );

    public class CreateProductDto
    {
        public int CategoryId { get; set; }
        public int UnitId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Barcode { get; set; }
        public string? SKU { get; set; }
        public string? ImageUrl { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal SalePrice { get; set; }
        public decimal TaxRate { get; set; }
        public decimal MinimumStock { get; set; } = 5;
        public bool IsFeatured { get; set; }
    }

    public class CreateOrderDto
    {
        public int? CustomerId { get; set; }
        public int? TableId { get; set; }
        public string OrderType { get; set; } = "Takeaway";
        public string? DiscountType { get; set; }
        public decimal DiscountValue { get; set; }
        public string? Notes { get; set; }
        public List<OrderItemDto> Items { get; set; } = new();
        public List<OrderPaymentDto> Payments { get; set; } = new();
    }

    public class OrderItemDto
    {
        public int ProductId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountAmount { get; set; }
        public string? Notes { get; set; }
    }

    public class OrderPaymentDto
    {
        public string PaymentMethod { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? Reference { get; set; }
    }

    public class DashboardDto
    {
        public decimal TodaySales { get; set; }
        public decimal TodayProfit { get; set; }
        public int TodayOrders { get; set; }
        public decimal TodayExpenses { get; set; }
        public decimal MonthSales { get; set; }
        public int LowStockCount { get; set; }
        public List<MonthlySalesDto> MonthlyData { get; set; } = new();
        public List<TopProductDto> TopProducts { get; set; } = new();
        public List<LowStockProductDto> LowStockProducts { get; set; } = new();
    }

    public record MonthlySalesDto(string Month, decimal Sales, decimal Profit);
    public record TopProductDto(string Name, decimal Quantity, decimal Revenue);
    public record LowStockProductDto(int Id, string Name, decimal CurrentStock, decimal MinimumStock, string Unit);

    public class SalesReportDto
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal TotalSales { get; set; }
        public decimal TotalTax { get; set; }
        public decimal TotalDiscount { get; set; }
        public decimal NetSales { get; set; }
        public int TotalOrders { get; set; }
        public List<SalesByDateDto> ByDate { get; set; } = new();
        public List<SalesByCategoryDto> ByCategory { get; set; } = new();
        public List<SalesByUserDto> ByUser { get; set; } = new();
        public List<SalesByCustomerDto> ByCustomer { get; set; } = new();
    }

    public record SalesByDateDto(DateTime Date, int Orders, decimal Sales, decimal Profit);
    public record SalesByCategoryDto(string Category, int Items, decimal Sales, decimal Profit);
    public record SalesByUserDto(string User, int Orders, decimal Sales);
    public record SalesByCustomerDto(string Customer, int Orders, decimal Sales, decimal Balance);

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
        public List<string>? Errors { get; set; }
        public int? TotalCount { get; set; }

        public static ApiResponse<T> Ok(T data, string? message = null, int? totalCount = null) =>
            new() { Success = true, Data = data, Message = message, TotalCount = totalCount };

        public static ApiResponse<T> Fail(string message, List<string>? errors = null) =>
            new() { Success = false, Message = message, Errors = errors };
    }

    public record ChangePasswordDto(string OldPassword, string NewPassword);
    public record CategoryDto(string Name, string? Icon, string? Color, int SortOrder);
    public record CustomerDto(string Name, string? Phone, string? Email, string? Address, decimal CreditLimit);
    public record CustomerPaymentDto(decimal Amount, string? Notes);
    public record SupplierDto(string Name, string? Company, string? Phone, string? Email, string? Address);
    public record StockAdjustmentDto(decimal Quantity, string Type, string? Notes);
    public record UpdateBranchDto(
    string? Name,
    string? Address,
    string? Phone,
    string? Email,
    string? Logo,
    string? TaxNumber
);

public record CreateUserDto(int RoleId, string FullName, string Username, string? Email, string? Phone, string Password);

    public class CreatePurchaseDto
    {
        public int SupplierId { get; set; }
        public string? Notes { get; set; }
        public decimal PaidAmount { get; set; }
        public string? PaymentMethod { get; set; }
        public List<PurchaseItemInputDto> Items { get; set; } = new();
    }

    public record PurchaseItemInputDto(int ProductId, decimal Quantity, decimal UnitCost);
    public record CreateExpenseDto(int CategoryId, string Title, decimal Amount, string? PaymentMethod, string? Notes, DateTime? ExpenseDate);
}

// ============================================
// INTERFACES
// ============================================
namespace POS.Core.Interfaces
{
    using POS.Core.DTOs;
    using POS.Core.Entities;

    public interface IAuthService
    {
        Task<TokenResponseDto> LoginAsync(LoginDto dto);
        Task<TokenResponseDto> RefreshTokenAsync(string refreshToken);
        Task LogoutAsync(int userId);
        Task<bool> ChangePasswordAsync(int userId, string oldPassword, string newPassword);
    }

    public interface IProductService
    {
        Task<Product?> GetByBarcodeAsync(string barcode);
        Task<IEnumerable<Product>> GetLowStockAsync(int branchId);
        Task<bool> UpdateStockAsync(int productId, decimal quantity, string type, int userId);
    }

    public interface IOrderService
    {
        Task<Order> CreateOrderAsync(CreateOrderDto dto, int userId, int branchId);
        Task<Order> CompleteOrderAsync(long orderId);
        Task<Order> VoidOrderAsync(long orderId, string reason);
        Task<string> GenerateKOTAsync(long orderId);
        Task<string> GenerateReceiptAsync(long orderId);
        Task<Order> HoldOrderAsync(long orderId, string holdName);
        Task<Order> ResumeOrderAsync(int heldOrderId);
    }

    public interface IDashboardService
    {
        Task<DashboardDto> GetDashboardDataAsync(int branchId);
    }

    public interface IReportService
    {
        Task<SalesReportDto> GetSalesReportAsync(int branchId, DateTime startDate, DateTime endDate);
        Task<object> GetStockReportAsync(int branchId);
        Task<object> GetExpenseReportAsync(int branchId, DateTime startDate, DateTime endDate);
        Task<object> GetProfitLossAsync(int branchId, DateTime startDate, DateTime endDate);
        Task<object> GetDailyClosingAsync(int branchId, DateTime date);
    }

    public interface ISyncService
    {
        Task<bool> ProcessSyncQueueAsync(List<SyncQueue> items);
        Task<object> GetServerChangesAsync(int branchId, DateTime lastSyncAt);
    }
}
