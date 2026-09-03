using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Services;
using RentalSphere.Data;
using RentalSphere.Data.Seed;
using RentalSphere.Identity;

var builder = WebApplication.CreateBuilder(args);

// --- EF Core + Identity ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString)
           .ConfigureWarnings(w => w.Ignore(
               Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Course-project defaults; tighten for production.
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;

    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// --- Cookie auth redirects ---
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

// --- HTTP context accessor + scoped current-user wrapper ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// --- Identity/admin services ---
builder.Services.AddScoped<RentalSphere.Identity.Services.IAuditLogger, RentalSphere.Identity.Services.AuditLogger>();
builder.Services.AddScoped<RentalSphere.Identity.Services.IAccountsAdminService, RentalSphere.Identity.Services.AccountsAdminService>();

// --- Equipment module services + repositories ---
builder.Services.AddScoped<RentalSphere.Modules.Equipment.Repositories.ICategoryRepository, RentalSphere.Modules.Equipment.Repositories.CategoryRepository>();
builder.Services.AddScoped<RentalSphere.Modules.Equipment.Repositories.IEquipmentRepository, RentalSphere.Modules.Equipment.Repositories.EquipmentRepository>();
builder.Services.AddScoped<RentalSphere.Modules.Equipment.Repositories.IEquipmentItemRepository, RentalSphere.Modules.Equipment.Repositories.EquipmentItemRepository>();
builder.Services.AddScoped<RentalSphere.Modules.Equipment.Services.ICategoryService, RentalSphere.Modules.Equipment.Services.CategoryService>();
builder.Services.AddScoped<RentalSphere.Modules.Equipment.Services.IEquipmentService, RentalSphere.Modules.Equipment.Services.EquipmentService>();
builder.Services.AddScoped<RentalSphere.Modules.Equipment.Services.IEquipmentItemService, RentalSphere.Modules.Equipment.Services.EquipmentItemService>();

// --- Customer Management module ---
builder.Services.AddScoped<RentalSphere.Modules.CustomerManagement.Repositories.ICustomerRepository, RentalSphere.Modules.CustomerManagement.Repositories.CustomerRepository>();
builder.Services.AddScoped<RentalSphere.Modules.CustomerManagement.Services.ICustomerService, RentalSphere.Modules.CustomerManagement.Services.CustomerService>();

// --- Reservation Management module ---
builder.Services.AddScoped<RentalSphere.Modules.ReservationManagement.Repositories.IReservationRepository, RentalSphere.Modules.ReservationManagement.Repositories.ReservationRepository>();
builder.Services.AddScoped<RentalSphere.Modules.ReservationManagement.Services.IReservationService, RentalSphere.Modules.ReservationManagement.Services.ReservationService>();

// --- Rental Transaction Management module ---
builder.Services.AddScoped<RentalSphere.Modules.RentalTransaction.Repositories.IRentalTransactionRepository, RentalSphere.Modules.RentalTransaction.Repositories.RentalTransactionRepository>();
builder.Services.AddScoped<RentalSphere.Modules.RentalTransaction.Services.IRentalTransactionService, RentalSphere.Modules.RentalTransaction.Services.RentalTransactionService>();

// --- Maintenance Management module ---
builder.Services.AddScoped<RentalSphere.Modules.Maintenance.Repositories.IMaintenanceRepository, RentalSphere.Modules.Maintenance.Repositories.MaintenanceRepository>();
builder.Services.AddScoped<RentalSphere.Modules.Maintenance.Services.IMaintenanceService, RentalSphere.Modules.Maintenance.Services.MaintenanceService>();

// --- Equipment Availability (read-only service) ---
builder.Services.AddScoped<RentalSphere.Modules.EquipmentAvailability.Services.IAvailabilityService, RentalSphere.Modules.EquipmentAvailability.Services.AvailabilityService>();

// --- Billing module ---
builder.Services.AddScoped<RentalSphere.Modules.Billing.Repositories.IBillingRepository, RentalSphere.Modules.Billing.Repositories.BillingRepository>();
builder.Services.AddScoped<RentalSphere.Modules.Billing.Services.IBillingService>(sp =>
    new RentalSphere.Modules.Billing.Services.BillingService(
        sp.GetRequiredService<RentalSphere.Modules.Billing.Repositories.IBillingRepository>(),
        sp.GetRequiredService<RentalSphere.Modules.RentalTransaction.Repositories.IRentalTransactionRepository>(),
        sp.GetRequiredService<RentalSphere.Modules.DamagePenalty.Repositories.IDamagePenaltyRepository>(),
        sp.GetRequiredService<RentalSphere.Modules.CustomerManagement.Repositories.ICustomerRepository>(),
        sp.GetRequiredService<RentalSphere.Common.Services.ICurrentUser>(),
        sp.GetRequiredService<RentalSphere.Identity.Services.IAuditLogger>(),
        new RentalSphere.Modules.Billing.Services.BillingOptions(
            LateFeePerDay: builder.Configuration.GetValue<decimal?>("Billing:LateFeePerDay") ?? 100m,
            PaymentTermsDays: builder.Configuration.GetValue<int?>("Billing:PaymentTermsDays") ?? 14)));

// --- Damage & Penalty module ---
builder.Services.AddScoped<RentalSphere.Modules.DamagePenalty.Repositories.IDamagePenaltyRepository, RentalSphere.Modules.DamagePenalty.Repositories.DamagePenaltyRepository>();
builder.Services.AddScoped<RentalSphere.Modules.DamagePenalty.Services.IDamagePenaltyService, RentalSphere.Modules.DamagePenalty.Services.DamagePenaltyService>();

// --- Customer CRM module ---
builder.Services.AddScoped<RentalSphere.Modules.CustomerCrm.Repositories.ICustomerCrmRepository, RentalSphere.Modules.CustomerCrm.Repositories.CustomerCrmRepository>();
builder.Services.AddScoped<RentalSphere.Modules.CustomerCrm.Services.ICustomerCrmService, RentalSphere.Modules.CustomerCrm.Services.CustomerCrmService>();

// --- Reports module ---
builder.Services.AddScoped<RentalSphere.Modules.Reports.Services.IReportsService, RentalSphere.Modules.Reports.Services.ReportsService>();
builder.Services.AddScoped<RentalSphere.Modules.Dashboard.DashboardKpiService>();

// --- Reservation expiry background worker ---
var expiryOpts = new RentalSphere.Modules.ReservationManagement.BackgroundServices.ReservationExpiryOptions
{
    PendingExpiryHours = builder.Configuration.GetValue<double?>("ReservationExpiry:PendingExpiryHours") ?? 0.5,
    ConfirmedExpiryDays = builder.Configuration.GetValue<int?>("ReservationExpiry:ConfirmedExpiryDays") ?? 1,
    ScanIntervalMinutes = builder.Configuration.GetValue<int?>("ReservationExpiry:ScanIntervalMinutes") ?? 5,
};
builder.Services.AddSingleton(expiryOpts);
builder.Services.AddHostedService<RentalSphere.Modules.ReservationManagement.BackgroundServices.ReservationExpiryWorker>();

// --- MVC ---
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<RentalSphere.Common.Filters.GlobalExceptionFilter>();
});

var app = builder.Build();

// --- Pipeline ---
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Seed roles + default users after the schema is in place.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    await IdentitySeeder.SeedAsync(roleManager, userManager);

    await DomainSeeder.SeedAsync(db, userManager);
}

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
