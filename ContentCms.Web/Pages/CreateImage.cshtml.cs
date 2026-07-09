using Microsoft.AspNetCore.Mvc;
using ContentCms.Web.Data;
using ContentCms.Api.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ContentCms.Web.Pages;

public class CreateImageModel : PageModel
{
    private readonly ImageWebDbContext _context;

    public string? ErrorMessage { get; set; }

    public CreateImageModel(ImageWebDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> OnPostAsync(string name, string description, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorMessage = "Name is required.";
            return Page();
        }

        var image = new Image
        {
            Name = name,
            Description = description ?? "",
            Data = data ?? Array.Empty<byte>(),
            IsBlocked = false,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Images.Add(image);
        await _context.SaveChangesAsync();

        return RedirectToPage("/Index");
    }
}
