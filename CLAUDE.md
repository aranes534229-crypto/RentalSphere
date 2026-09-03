# RentalSphere — Project Instructions

ERP/CRM system for event & party equipment rental businesses.
Course project: IT15/L Integrative Programming and Technologies.

## Stack
- ASP.NET Core MVC (C#)
- Entity Framework Core (Code-First: models → migrations → DB)
- Microsoft SQL Server
- Bootstrap + Razor Views

## Roles & Access (RBAC) — enforce these boundaries strictly
- **Admin**: full override across every module. Only role that can delete
  equipment, void transactions, waive fees, manage accounts/roles/settings,
  or view audit logs.
- **Staff**: day-to-day operations — equipment, reservations, transactions,
  billing entry, CRM logging, standard penalty fees. CANNOT delete equipment,
  void transactions, waive fees, or manage accounts/settings — those actions
  must escalate to Admin.
- **Customer**: self-service only, scoped strictly to their own records.
  No access to Maintenance or Reports modules at all.

When generating any controller action or view, check it against these
boundaries before writing it — don't give a role access it isn't supposed
to have, even implicitly.

## The 10 Modules
Equipment Management, Customer Management, Reservation Management,
Rental Transactions, Equipment Availability, Maintenance Management,
Billing, Customer CRM, Damage & Penalty Tracking, Reports

Keep every module's folder structure and layering consistent — if one
module has a Controller/Service/Repository split, all of them should.

## Transaction Lifecycle
Reservation Creation → Availability Verification → Confirmation →
Checkout (creates Rental Transaction, sets CheckoutDate) → Active Rental
(ReturnDate empty) → Return (same transaction record updated with
ReturnDate/condition notes — NOT a new record) → Billing & Payment →
conditional Damage/Penalty Assessment → Closure & Reporting

## Coding Conventions
- Use async/await for all database calls (EF Core).
- Keep DTOs/ViewModels separate from EF entity classes — don't expose
  EF models directly to views.
- Follow standard ASP.NET Core MVC naming (PascalCase for classes/methods,
  camelCase for local variables/parameters).
- Add data annotations or Fluent API validation for all model constraints.