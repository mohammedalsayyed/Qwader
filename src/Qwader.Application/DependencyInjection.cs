using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Qwader.Application.Catalog;
using Qwader.Application.Customers;
using Qwader.Application.Sales;
using Qwader.Application.Inventory;
using Qwader.Application.Ledgers;
using Qwader.Application.Lookups;
using Qwader.Application.Purchasing;
using Qwader.Application.Search;
using Qwader.Application.Settings;

namespace Qwader.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<ILookupService, LookupService>();

        services.AddScoped<IInventoryLedger, InventoryLedger>();
        services.AddScoped<IAccountLedger, AccountLedger>();
        services.AddScoped<ISupplierLedger, SupplierLedger>();
        services.AddScoped<ICustomerLedger, CustomerLedger>();

        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IProductExcelService, ProductExcelService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IPurchaseService, PurchaseService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ISaleService, SaleService>();
        return services;
    }
}
