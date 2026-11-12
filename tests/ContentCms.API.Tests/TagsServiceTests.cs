using ContentCms.API.Models;
using ContentCms.API.Pages.Shared;
using ContentCms.API.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContentCms.API.Tests
{
    /// <summary>Runs against in-memory SQLite so the relational query translation and cascades are exercised.</summary>
    public sealed class TagsServiceTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ContentCmsDbContext _db;
        private readonly TagsService _service;
        private readonly UserModel _alice;
        private readonly UserModel _bob;
        private readonly UserModel _admin;

        public TagsServiceTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ContentCmsDbContext(new DbContextOptionsBuilder<ContentCmsDbContext>().UseSqlite(_connection).Options);
            _db.Database.EnsureCreated();

            _alice = AddUser("alice", UserRole.User);
            _bob = AddUser("bob", UserRole.User);
            _admin = AddUser("admin", UserRole.Admin);
            _service = new TagsService(_db);
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private UserModel AddUser(string name, UserRole role)
        {
            var user = new UserModel { Username = name, Email = $"{name}@example.com", Password = "x", Role = role };
            _db.Users.Add(user);
            _db.SaveChanges();
            return user;
        }

        private ContentModel AddContent(UserModel owner, bool deleted = false)
        {
            var content = new ContentModel { OwnerId = owner.Id, Path = $"/img/{Guid.NewGuid()}.png", IsDeleted = deleted };
            _db.Contents.Add(content);
            _db.SaveChanges();
            return content;
        }

        private async Task<TagModel> CreateTag(UserModel owner, string name)
        {
            var (result, tag) = await _service.CreateAsync(name, "#FF0000", owner.Id);
            Assert.Equal(TagOperationResult.Success, result);
            return tag!;
        }

        private List<int> TagIdsOf(ContentModel content)
        {
            _db.ChangeTracker.Clear();
            return _db.Contents.Include(c => c.Tags).Single(c => c.Id == content.Id).Tags.Select(t => t.Id).OrderBy(i => i).ToList();
        }

        [Fact]
        public async Task Create_normalizes_name_and_color()
        {
            var (result, tag) = await _service.CreateAsync("  summer   2026 ", "#ABCDEF", _alice.Id);

            Assert.Equal(TagOperationResult.Success, result);
            Assert.Equal("summer 2026", tag!.Name);
            Assert.Equal("#abcdef", tag.Color);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Create_rejects_empty_name(string name)
        {
            var (result, _) = await _service.CreateAsync(name, null, _alice.Id);
            Assert.Equal(TagOperationResult.InvalidName, result);
        }

        [Fact]
        public async Task Create_rejects_too_long_name()
        {
            var (result, _) = await _service.CreateAsync(new string('a', TagModel.NameMaxLength + 1), null, _alice.Id);
            Assert.Equal(TagOperationResult.InvalidName, result);
        }

        [Fact]
        public async Task Duplicate_names_are_rejected_per_owner_only()
        {
            await CreateTag(_alice, "news");

            var (dupe, _) = await _service.CreateAsync("news", null, _alice.Id);
            var (otherOwner, _) = await _service.CreateAsync("news", null, _bob.Id);

            Assert.Equal(TagOperationResult.DuplicateName, dupe);
            Assert.Equal(TagOperationResult.Success, otherOwner);
        }

        [Fact]
        public async Task Users_see_own_tags_and_admins_see_all()
        {
            await CreateTag(_alice, "a");
            await CreateTag(_bob, "b");

            Assert.Equal(new[] { "a" }, (await _service.GetAvailableTagsAsync(_alice.Id, false)).Select(t => t.Name));
            Assert.Equal(new[] { "a", "b" }, (await _service.GetAvailableTagsAsync(_admin.Id, true)).Select(t => t.Name));
        }

        [Fact]
        public async Task Update_and_delete_require_ownership_unless_admin()
        {
            var tag = await CreateTag(_alice, "a");

            Assert.Equal(TagOperationResult.Forbidden, await _service.UpdateAsync(tag.Id, "x", null, _bob.Id, false));
            Assert.Equal(TagOperationResult.Forbidden, await _service.DeleteAsync(tag.Id, _bob.Id, false));
            Assert.Equal(TagOperationResult.Success, await _service.UpdateAsync(tag.Id, "renamed", "#00ff00", _admin.Id, true));
            Assert.Equal(TagOperationResult.NotFound, await _service.UpdateAsync(9999, "x", null, _alice.Id, false));

            _db.ChangeTracker.Clear();
            var updated = await _db.Tags.SingleAsync(t => t.Id == tag.Id);
            Assert.Equal("renamed", updated.Name);
            Assert.Equal("#00ff00", updated.Color);
        }

        [Fact]
        public async Task Update_rejects_rename_to_existing_name()
        {
            await CreateTag(_alice, "a");
            var b = await CreateTag(_alice, "b");

            Assert.Equal(TagOperationResult.DuplicateName, await _service.UpdateAsync(b.Id, "a", null, _alice.Id, false));
            // Keeping its own name is not a conflict.
            Assert.Equal(TagOperationResult.Success, await _service.UpdateAsync(b.Id, "b", "#123456", _alice.Id, false));
        }

        [Fact]
        public async Task Set_tags_assigns_and_unassigns()
        {
            var content = AddContent(_alice);
            var a = await CreateTag(_alice, "a");
            var b = await CreateTag(_alice, "b");
            var c = await CreateTag(_alice, "c");

            Assert.Equal(TagOperationResult.Success, await _service.SetContentTagsAsync(content.Id, new[] { a.Id, b.Id }, _alice.Id, false));
            Assert.Equal(new[] { a.Id, b.Id }, TagIdsOf(content));

            Assert.Equal(TagOperationResult.Success, await _service.SetContentTagsAsync(content.Id, new[] { b.Id, c.Id }, _alice.Id, false));
            Assert.Equal(new[] { b.Id, c.Id }, TagIdsOf(content));

            Assert.Equal(TagOperationResult.Success, await _service.SetContentTagsAsync(content.Id, Array.Empty<int>(), _alice.Id, false));
            Assert.Empty(TagIdsOf(content));
        }

        [Fact]
        public async Task Set_tags_ignores_foreign_tags_and_keeps_tags_user_cannot_manage()
        {
            var content = AddContent(_alice);
            var own = await CreateTag(_alice, "own");
            var bobs = await CreateTag(_bob, "bobs");
            var adminTag = await CreateTag(_admin, "reviewed");

            // Admin tags Alice's content with an admin-owned tag.
            await _service.SetContentTagsAsync(content.Id, new[] { adminTag.Id }, _admin.Id, true);

            // Alice tries to attach Bob's tag and saves without the admin tag (she can't see it).
            await _service.SetContentTagsAsync(content.Id, new[] { own.Id, bobs.Id }, _alice.Id, false);

            Assert.Equal(new[] { own.Id, adminTag.Id }.OrderBy(i => i), TagIdsOf(content));
        }

        [Fact]
        public async Task Set_tags_on_someone_elses_content_is_forbidden()
        {
            var content = AddContent(_alice);
            var bobs = await CreateTag(_bob, "bobs");

            Assert.Equal(TagOperationResult.Forbidden, await _service.SetContentTagsAsync(content.Id, new[] { bobs.Id }, _bob.Id, false));
            Assert.Equal(TagOperationResult.NotFound, await _service.SetContentTagsAsync(9999, new[] { bobs.Id }, _bob.Id, false));
        }

        [Fact]
        public async Task Deleting_a_tag_removes_it_from_content()
        {
            var content = AddContent(_alice);
            var a = await CreateTag(_alice, "a");
            var b = await CreateTag(_alice, "b");
            await _service.SetContentTagsAsync(content.Id, new[] { a.Id, b.Id }, _alice.Id, false);

            Assert.Equal(TagOperationResult.Success, await _service.DeleteAsync(a.Id, _alice.Id, false));

            Assert.Equal(new[] { b.Id }, TagIdsOf(content));
        }

        [Fact]
        public async Task Usage_counts_only_non_deleted_content()
        {
            var tag = await CreateTag(_alice, "a");
            var live = AddContent(_alice);
            var deleted = AddContent(_alice, deleted: true);
            await _service.SetContentTagsAsync(live.Id, new[] { tag.Id }, _alice.Id, false);
            await _service.SetContentTagsAsync(deleted.Id, new[] { tag.Id }, _alice.Id, false);

            var usage = Assert.Single(await _service.GetTagsWithUsageAsync(_alice.Id, false));
            Assert.Equal(1, usage.ContentCount);
        }

        [Fact]
        public async Task Tag_filter_matches_any_or_all()
        {
            var a = await CreateTag(_alice, "a");
            var b = await CreateTag(_alice, "b");
            var onlyA = AddContent(_alice);
            var both = AddContent(_alice);
            var none = AddContent(_alice);
            await _service.SetContentTagsAsync(onlyA.Id, new[] { a.Id }, _alice.Id, false);
            await _service.SetContentTagsAsync(both.Id, new[] { a.Id, b.Id }, _alice.Id, false);

            List<int> Filter(int[] ids, bool all) =>
                TagRules.ApplyTagFilter(_db.Contents, ids, all).Select(c => c.Id).OrderBy(i => i).ToList();

            Assert.Equal(new[] { onlyA.Id, both.Id }, Filter(new[] { a.Id, b.Id }, all: false));
            Assert.Equal(new[] { both.Id }, Filter(new[] { a.Id, b.Id }, all: true));
            Assert.Equal(new[] { both.Id }, Filter(new[] { b.Id }, all: false));
            // Duplicate ids must not make "all" impossible to satisfy.
            Assert.Equal(new[] { both.Id }, Filter(new[] { a.Id, b.Id, b.Id }, all: true));
            // No tags selected -> no filtering.
            Assert.Equal(new[] { onlyA.Id, both.Id, none.Id }, Filter(Array.Empty<int>(), all: true));
        }
    }

    public class TagRulesTests
    {
        [Theory]
        [InlineData(null, TagModel.DefaultColor)]
        [InlineData("red", TagModel.DefaultColor)]
        [InlineData("#12345", TagModel.DefaultColor)]
        [InlineData("#AbCdEf", "#abcdef")]
        public void NormalizeColor_validates_hex(string? input, string expected)
        {
            Assert.Equal(expected, TagRules.NormalizeColor(input));
        }

        [Theory]
        [InlineData("#ffffff", "#000000")]
        [InlineData("#ffc107", "#000000")]
        [InlineData("#000000", "#ffffff")]
        [InlineData("#0d6efd", "#ffffff")]
        public void TextColor_has_contrast(string background, string expected)
        {
            Assert.Equal(expected, TagRules.TextColorFor(background));
        }

        [Fact]
        public void Filter_query_uses_indexed_keys_and_only_emits_mode_for_all()
        {
            var any = new Dictionary<string, string>();
            TagFilterQuery.AddTo(any, new[] { 4, 7 }, "any", v => v);
            Assert.Equal(new Dictionary<string, string> { ["FilterTagIds[0]"] = "4", ["FilterTagIds[1]"] = "7" }, any);

            var all = new Dictionary<string, string>();
            TagFilterQuery.AddTo(all, new[] { 4 }, "ALL", v => v);
            Assert.Equal("all", all["FilterTagMode"]);

            var empty = new Dictionary<string, string>();
            TagFilterQuery.AddTo(empty, Array.Empty<int>(), "all", v => v);
            Assert.Empty(empty);
        }
    }
}
