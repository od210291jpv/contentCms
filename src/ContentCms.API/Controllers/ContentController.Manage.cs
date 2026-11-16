using ContentCms.API.DTOs;
using ContentCms.API.DTOs.Events;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContentCms.API.Controllers
{
    // Content management endpoints for the mobile app. Users manage their own content; admins manage all content.
    public partial class ContentController
    {
        // GET: api/Content/feed?groupId=3&tagIds=1,2&matchAll=false&page=1&pageSize=20
        /// <summary>The caller's content (all content for admins), newest first, deleted items excluded.</summary>
        [HttpGet("feed")]
        public async Task<ActionResult<ContentPageDto>> Feed(
            [FromHeader(Name = "Authorization")] string? token,
            [FromServices] IContentBrowseService browse,
            [FromQuery] int? groupId,
            [FromQuery] string? tagIds,
            [FromQuery] bool matchAll = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (ApiCaller.From(_authService, token) is not { } caller) return Unauthorized();

            var tags = (tagIds ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            var result = await browse.GetContentAsync(caller.UserId, caller.IsAdmin, new ContentQuery(groupId, tags, matchAll, page, pageSize));
            return Ok(ContentPageDto.From(result));
        }

        // GET: api/Content/5/details
        [HttpGet("{id:int}/details")]
        public async Task<ActionResult<ContentItemDto>> Details(
            [FromHeader(Name = "Authorization")] string? token, [FromServices] IContentBrowseService browse, int id)
        {
            if (ApiCaller.From(_authService, token) is not { } caller) return Unauthorized();

            var content = await browse.GetContentByIdAsync(id, caller.UserId, caller.IsAdmin);
            return content == null ? ApiCaller.Error(StatusCodes.Status404NotFound, "Content not found.") : Ok(ContentItemDto.From(content));
        }

        // PUT: api/Content/5/tags  { "tagIds": [1, 2] }
        /// <summary>Sets the content's tags to exactly the given ones (tags the caller can't manage are left as they are).</summary>
        [HttpPut("{id:int}/tags")]
        public async Task<ActionResult<ContentItemDto>> SetTags(
            [FromHeader(Name = "Authorization")] string? token,
            [FromServices] IContentBrowseService browse,
            [FromServices] ITagsService tags,
            int id,
            [FromBody] SetContentTagsRequest request)
        {
            if (ApiCaller.From(_authService, token) is not { } caller) return Unauthorized();
            if (await browse.GetContentByIdAsync(id, caller.UserId, caller.IsAdmin) == null)
                return ApiCaller.Error(StatusCodes.Status404NotFound, "Content not found.");

            var result = await tags.SetContentTagsAsync(id, request.TagIds ?? new List<int>(), caller.UserId, caller.IsAdmin);
            if (result != TagOperationResult.Success) return ApiCaller.From(result);

            await _events.PublishAsync(id, ContentEventType.TagsChanged);
            return Ok(ContentItemDto.From((await browse.GetContentByIdAsync(id, caller.UserId, caller.IsAdmin))!));
        }

        // PUT: api/Content/5/group  { "groupId": 3 }   (null removes it from its group)
        [HttpPut("{id:int}/group")]
        public async Task<ActionResult<ContentItemDto>> SetGroup(
            [FromHeader(Name = "Authorization")] string? token,
            [FromServices] IContentBrowseService browse,
            int id,
            [FromBody] SetContentGroupRequest request)
        {
            if (ApiCaller.From(_authService, token) is not { } caller) return Unauthorized();

            var result = await browse.SetGroupAsync(id, request.GroupId, caller.UserId, caller.IsAdmin);
            if (result != ContentAccessResult.Success) return ApiCaller.From(result, "Content or group not found.");

            return Ok(ContentItemDto.From((await browse.GetContentByIdAsync(id, caller.UserId, caller.IsAdmin))!));
        }
    }
}
