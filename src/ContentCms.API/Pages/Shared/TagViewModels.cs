using ContentCms.API.Models;

namespace ContentCms.API.Pages.Shared
{
    /// <summary>Query-string state of the tag filter, shared by the content list pages.</summary>
    public static class TagFilterQuery
    {
        public const string TagIdsKey = "FilterTagIds";
        public const string ModeKey = "FilterTagMode";
        public const string MatchAllValue = "all";

        public static bool IsMatchAll(string? mode) => string.Equals(mode, MatchAllValue, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Writes the filter as indexed keys (FilterTagIds[0]=1&amp;FilterTagIds[1]=2) so it can live in the
        /// string-keyed route dictionaries used for links, pagination and redirects, and still bind to List&lt;int&gt;.
        /// </summary>
        public static void AddTo<T>(IDictionary<string, T> values, IReadOnlyList<int> tagIds, string? mode, Func<string, T> convert)
        {
            for (int i = 0; i < tagIds.Count; i++)
            {
                values[$"{TagIdsKey}[{i}]"] = convert(tagIds[i].ToString());
            }
            if (tagIds.Count > 0 && IsMatchAll(mode))
            {
                values[ModeKey] = convert(MatchAllValue);
            }
        }
    }

    public class TagFilterViewModel
    {
        public IReadOnlyList<TagModel> AvailableTags { get; init; } = Array.Empty<TagModel>();
        public IReadOnlyCollection<int> SelectedTagIds { get; init; } = Array.Empty<int>();
        public bool MatchAll { get; init; }
        public bool ShowOwner { get; init; }
    }

    public class ManageTagsModalViewModel
    {
        public IReadOnlyList<TagModel> AvailableTags { get; init; } = Array.Empty<TagModel>();
        /// <summary>Route data appended to the form action so the list keeps its filters/page after saving.</summary>
        public IDictionary<string, string> RouteData { get; init; } = new Dictionary<string, string>();
        public bool ShowOwner { get; init; }
    }

    public class TagBadgeViewModel
    {
        public TagModel Tag { get; init; } = null!;
        /// <summary>Optional link (e.g. "filter by this tag"). When null the badge is plain text.</summary>
        public string? Href { get; init; }
    }
}
