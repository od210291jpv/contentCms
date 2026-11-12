using System.Globalization;
using System.Text.RegularExpressions;
using ContentCms.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentCms.API.Services
{
    public class TagsService : ITagsService
    {
        private readonly ContentCmsDbContext _context;

        public TagsService(ContentCmsDbContext context)
        {
            _context = context;
        }

        private IQueryable<TagModel> AvailableQuery(int userId, bool isAdmin)
        {
            var query = _context.Tags.AsQueryable();
            return isAdmin ? query : query.Where(t => t.OwnerId == userId);
        }

        public async Task<List<TagModel>> GetAvailableTagsAsync(int userId, bool isAdmin)
        {
            return await AvailableQuery(userId, isAdmin)
                .Include(t => t.Owner)
                .OrderBy(t => t.Name)
                .ThenBy(t => t.Id)
                .ToListAsync();
        }

        public async Task<List<TagUsage>> GetTagsWithUsageAsync(int userId, bool isAdmin)
        {
            var rows = await AvailableQuery(userId, isAdmin)
                .Include(t => t.Owner)
                .OrderBy(t => t.Name)
                .ThenBy(t => t.Id)
                .Select(t => new { Tag = t, Count = t.Contents.Count(c => !c.IsDeleted) })
                .ToListAsync();

            return rows.Select(r => new TagUsage(r.Tag, r.Count)).ToList();
        }

        public async Task<(TagOperationResult Result, TagModel? Tag)> CreateAsync(string name, string? color, int ownerId)
        {
            var normalizedName = TagRules.NormalizeName(name);
            if (normalizedName == null) return (TagOperationResult.InvalidName, null);

            if (await NameTakenAsync(ownerId, normalizedName, exceptId: null))
                return (TagOperationResult.DuplicateName, null);

            var tag = new TagModel
            {
                Name = normalizedName,
                Color = TagRules.NormalizeColor(color),
                OwnerId = ownerId
            };
            _context.Tags.Add(tag);
            await _context.SaveChangesAsync();
            return (TagOperationResult.Success, tag);
        }

        public async Task<TagOperationResult> UpdateAsync(int id, string name, string? color, int userId, bool isAdmin)
        {
            var tag = await _context.Tags.FindAsync(id);
            if (tag == null) return TagOperationResult.NotFound;
            if (!isAdmin && tag.OwnerId != userId) return TagOperationResult.Forbidden;

            var normalizedName = TagRules.NormalizeName(name);
            if (normalizedName == null) return TagOperationResult.InvalidName;

            if (await NameTakenAsync(tag.OwnerId, normalizedName, exceptId: tag.Id))
                return TagOperationResult.DuplicateName;

            tag.Name = normalizedName;
            tag.Color = TagRules.NormalizeColor(color);
            await _context.SaveChangesAsync();
            return TagOperationResult.Success;
        }

        public async Task<TagOperationResult> DeleteAsync(int id, int userId, bool isAdmin)
        {
            var tag = await _context.Tags.FindAsync(id);
            if (tag == null) return TagOperationResult.NotFound;
            if (!isAdmin && tag.OwnerId != userId) return TagOperationResult.Forbidden;

            // Join rows in ContentTags are removed by the cascade delete.
            _context.Tags.Remove(tag);
            await _context.SaveChangesAsync();
            return TagOperationResult.Success;
        }

        public async Task<TagOperationResult> SetContentTagsAsync(int contentId, IEnumerable<int> tagIds, int userId, bool isAdmin)
        {
            var content = await _context.Contents.Include(c => c.Tags).FirstOrDefaultAsync(c => c.Id == contentId);
            if (content == null) return TagOperationResult.NotFound;
            if (!isAdmin && content.OwnerId != userId) return TagOperationResult.Forbidden;

            var requested = tagIds.Distinct().ToHashSet();
            var manageable = await AvailableQuery(userId, isAdmin).ToListAsync();
            var manageableIds = manageable.Select(t => t.Id).ToHashSet();

            // Unassign manageable tags that were not requested; foreign tags stay.
            foreach (var tag in content.Tags.Where(t => manageableIds.Contains(t.Id) && !requested.Contains(t.Id)).ToList())
            {
                content.Tags.Remove(tag);
            }

            // Assign requested tags (unknown / foreign ids are ignored).
            var current = content.Tags.Select(t => t.Id).ToHashSet();
            foreach (var tag in manageable.Where(t => requested.Contains(t.Id) && !current.Contains(t.Id)))
            {
                content.Tags.Add(tag);
            }

            content.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return TagOperationResult.Success;
        }

        private Task<bool> NameTakenAsync(int ownerId, string name, int? exceptId)
        {
            // MySQL default collations are case-insensitive, so this also blocks "News" vs "news".
            return _context.Tags.AnyAsync(t => t.OwnerId == ownerId && t.Name == name && (exceptId == null || t.Id != exceptId));
        }
    }

    /// <summary>Validation, presentation and query helpers for tags.</summary>
    public static class TagRules
    {
        private static readonly Regex ColorRegex = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

        /// <summary>Trims and collapses whitespace. Returns null when the name is empty or too long.</summary>
        public static string? NormalizeName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var normalized = Regex.Replace(name.Trim(), @"\s+", " ");
            return normalized.Length > TagModel.NameMaxLength ? null : normalized;
        }

        /// <summary>Returns a lowercase #rrggbb color, falling back to the default color for invalid input.</summary>
        public static string NormalizeColor(string? color)
        {
            if (string.IsNullOrWhiteSpace(color)) return TagModel.DefaultColor;
            color = color.Trim();
            return ColorRegex.IsMatch(color) ? color.ToLowerInvariant() : TagModel.DefaultColor;
        }

        /// <summary>Picks black or white text for readable contrast on the given background color.</summary>
        public static string TextColorFor(string? background)
        {
            var color = NormalizeColor(background);
            int r = int.Parse(color.Substring(1, 2), NumberStyles.HexNumber);
            int g = int.Parse(color.Substring(3, 2), NumberStyles.HexNumber);
            int b = int.Parse(color.Substring(5, 2), NumberStyles.HexNumber);
            // YIQ perceived brightness
            double brightness = (r * 299 + g * 587 + b * 114) / 1000.0;
            return brightness >= 150 ? "#000000" : "#ffffff";
        }

        /// <summary>
        /// Restricts content to items tagged with the given tags.
        /// matchAll = false: item has at least one of the tags; matchAll = true: item has every tag.
        /// </summary>
        public static IQueryable<ContentModel> ApplyTagFilter(IQueryable<ContentModel> query, IReadOnlyCollection<int>? tagIds, bool matchAll)
        {
            if (tagIds == null || tagIds.Count == 0) return query;

            var ids = tagIds.Distinct().ToList();
            if (matchAll)
            {
                int required = ids.Count;
                return query.Where(c => c.Tags.Count(t => ids.Contains(t.Id)) == required);
            }

            return query.Where(c => c.Tags.Any(t => ids.Contains(t.Id)));
        }

        public static string ErrorMessage(TagOperationResult result) => result switch
        {
            TagOperationResult.NotFound => "Tag or content not found.",
            TagOperationResult.Forbidden => "You are not allowed to change this tag or content.",
            TagOperationResult.InvalidName => $"Tag name is required and must be at most {TagModel.NameMaxLength} characters.",
            TagOperationResult.DuplicateName => "A tag with this name already exists.",
            _ => string.Empty
        };
    }
}
