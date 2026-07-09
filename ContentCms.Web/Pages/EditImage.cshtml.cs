using ContentCms.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ContentCms.Api.Models;

namespace ContentCms.Web.Pages;

public class EditImageModel : PageModel
{
    private readonly ImageWebDbContext _context;

    public Image? Image { get; set; }
    public string? ErrorMessage { get; set; }

    public EditImageModel(ImageWebDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var image = await _context.Images.FindAsync(id);

        if (image == null || image.IsDeleted)
        {
            return NotFound();
        }

        Image = image;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, string name, string description)
    {
        var existingImage = await _context.Images.FindAsync(id);

        if (existingImage == null || existingImage.IsDeleted)
        {
            ErrorMessage = "Image not found or already deleted.";
            return Page();
        }

        existingImage.Name = name;
        existingImage.Description = description ?? "";
        existingImage.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return RedirectToPage("/Index");
    }
}
