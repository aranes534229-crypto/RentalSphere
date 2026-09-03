using RentalSphere.Common.Exceptions;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.Equipment.DTOs;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.Equipment.Repositories;
using RentalSphere.Modules.Equipment.ViewModels;

namespace RentalSphere.Modules.Equipment.Services;

public interface ICategoryService
{
    Task<List<CategoryListItemDto>> ListAsync(string? search = null);
    Task<(List<CategoryListItemDto> Items, int TotalCount)> ListPagedAsync(string? search, int skip, int take);
    Task<CategoryDto> GetAsync(int id);
    Task<int> CreateAsync(CategoryDto dto, string actorUserId);
    Task UpdateAsync(CategoryDto dto, string actorUserId);
    Task DeleteAsync(int id, string actorUserId);
    Task<List<CategoryLookupDto>> GetLookupAsync();
}

public record CategoryLookupDto(int CategoryID, string Name);

public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _repo;
    private readonly IAuditLogger _audit;

    public CategoryService(ICategoryRepository repo, IAuditLogger audit)
    {
        _repo = repo;
        _audit = audit;
    }

    public async Task<List<CategoryListItemDto>> ListAsync(string? search = null)
    {
        // Non-paged callers (e.g. lookup helpers) get everything.
        var (items, _) = await ListPagedAsync(search, 0, int.MaxValue);
        return items;
    }

    public async Task<(List<CategoryListItemDto> Items, int TotalCount)> ListPagedAsync(string? search, int skip, int take)
    {
        var total = await _repo.CountAsync(search);
        var cats = await _repo.ListPagedAsync(search, skip, take);
        var items = cats.Select(c => new CategoryListItemDto
        {
            CategoryID = c.CategoryID,
            Name = c.Name,
            Description = c.Description,
            EquipmentCount = c.Equipment.Count,
        }).ToList();
        return (items, total);
    }

    public async Task<CategoryDto> GetAsync(int id)
    {
        var c = await _repo.GetAsync(id) ?? throw new NotFoundException($"Category {id} not found.");
        return new CategoryDto { CategoryID = c.CategoryID, Name = c.Name, Description = c.Description };
    }

    public async Task<int> CreateAsync(CategoryDto dto, string actorUserId)
    {
        if (await _repo.NameExistsAsync(dto.Name))
            throw new InvalidOperationException($"Category '{dto.Name}' already exists.");

        var c = new Category { Name = dto.Name.Trim(), Description = dto.Description };
        await _repo.AddAsync(c);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Category.Create", "Category", c.CategoryID.ToString(),
            newValues: new { c.Name });
        return c.CategoryID;
    }

    public async Task UpdateAsync(CategoryDto dto, string actorUserId)
    {
        var c = await _repo.GetAsync(dto.CategoryID)
            ?? throw new NotFoundException($"Category {dto.CategoryID} not found.");

        if (await _repo.NameExistsAsync(dto.Name, dto.CategoryID))
            throw new InvalidOperationException($"Category '{dto.Name}' already exists.");

        var oldValues = new { c.Name };
        c.Name = dto.Name.Trim();
        c.Description = dto.Description;
        _repo.Update(c);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Category.Update", "Category", c.CategoryID.ToString(),
            oldValues: oldValues,
            newValues: new { c.Name });
    }

    public async Task DeleteAsync(int id, string actorUserId)
    {
        var c = await _repo.GetAsync(id) ?? throw new NotFoundException($"Category {id} not found.");
        if (await _repo.HasEquipmentAsync(id))
            throw new InvalidOperationException(
                "Cannot delete this category because equipment is still assigned to it. " +
                "Reassign or delete that equipment first.");

        var snapshot = new { c.Name };
        await _repo.RemoveAsync(c);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Category.Delete", "Category", id.ToString(),
            oldValues: snapshot);
    }

    public async Task<List<CategoryLookupDto>> GetLookupAsync()
    {
        var cats = await _repo.ListAsync();
        return cats.Select(c => new CategoryLookupDto(c.CategoryID, c.Name)).ToList();
    }
}
