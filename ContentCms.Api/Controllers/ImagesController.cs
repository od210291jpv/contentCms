using ContentCms.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ContentCms.Api.Models;

namespace ContentCms.Api.Controllers;

[ApiController]
[Route("api/images")]
public class ImagesController : ControllerBase
{
    private readonly ImageDbContext _context;

    public ImagesController(ImageDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<Image>>> GetImages()
    {
        var images = await _context.Images
            .Where(i => !i.IsDeleted && !i.IsBlocked)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return Ok(images);
    }

    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<Image>> GetImage(int id)
    {
        var image = await _context.Images.FindAsync(id);

        if (image == null || image.IsDeleted)
            return NotFound();

        return Ok(image);
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<Image>> CreateImage([FromBody] Image image)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        _context.Images.Add(image);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetImage), new { id = image.Id }, image);
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<ActionResult<Image>> UpdateImage(int id, [FromBody] Image updated)
    {
        var existing = await _context.Images.FindAsync(id);

        if (existing == null || existing.IsDeleted)
            return NotFound();

        existing.Name = updated.Name;
        existing.Description = updated.Description;
        existing.Data = updated.Data;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(existing);
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<ActionResult> SoftDeleteImage(int id)
    {
        var image = await _context.Images.FindAsync(id);

        if (image == null || image.IsDeleted)
            return NotFound();

        image.IsDeleted = true;
        image.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPut("{id}/toggle-block")]
    [Authorize]
    public async Task<ActionResult<Image>> ToggleBlock(int id)
    {
        var image = await _context.Images.FindAsync(id);

        if (image == null || image.IsDeleted)
            return NotFound();

        image.IsBlocked = !image.IsBlocked;
        image.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(image);
    }
}
