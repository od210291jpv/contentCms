using ContentCms.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentCms.API.Services
{
    public class ContentService : IContentService
    {
        private readonly ContentCmsDbContext _context;

        public ContentService(ContentCmsDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<ContentModel>> GetAllAsync()
        {
            return await _context.Contents
                .Where(c => !c.IsDeleted)
                .ToListAsync();
        }

        public async Task<ContentModel?> GetByIdAsync(int id)
        {
            return await _context.Contents
                .Include(c => c.Owner)
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        }

        public async Task<ContentModel> CreateAsync(ContentModel content)
        {
            _context.Contents.Add(content);
            await _context.SaveChangesAsync();
            return content;
        }

        public async Task<bool> UpdateAsync(int id, ContentModel content)
        {
            var existingContent = await _context.Contents.FindAsync(id);
            if (existingContent == null || existingContent.IsDeleted)
            {
                return false;
            }

            existingContent.Description = content.Description;
            existingContent.Path = content.Path;
            existingContent.IsPublic = content.IsPublic;
            existingContent.Enabled = content.Enabled;
            existingContent.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> SoftDeleteAsync(int id)
        {
            var content = await _context.Contents.FindAsync(id);
            if (content == null || content.IsDeleted)
            {
                return false;
            }

            content.IsDeleted = true;
            content.DeletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> SetEnabledAsync(int id, bool enabled)
        {
            var content = await _context.Contents.FindAsync(id);
            if (content == null || content.IsDeleted)
            {
                return false;
            }

            content.Enabled = enabled;
            content.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
