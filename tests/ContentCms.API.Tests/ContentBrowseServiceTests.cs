using ContentCms.API.DTOs.Events;
using ContentCms.API.Models;
using ContentCms.API.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ContentCms.API.Tests
{
    /// <summary>The content management rules behind the mobile app API.</summary>
    public sealed class ContentBrowseServiceTests : IDisposable
    {
        private sealed class RecordingBus : IRabbitMqEventBus
        {
            public List<ContentUpdateEvent> Events { get; } = new();

            public ValueTask PublishEventAsync(ContentUpdateEvent contentEvent, CancellationToken cancellationToken = default)
            {
                Events.Add(contentEvent);
                return ValueTask.CompletedTask;
            }
        }

        private readonly SqliteConnection _connection;
        private readonly ContentCmsDbContext _db;
        private readonly RecordingBus _bus = new();
        private readonly ContentBrowseService _service;
        private readonly UserModel _alice;
        private readonly UserModel _bob;

        public ContentBrowseServiceTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ContentCmsDbContext(new DbContextOptionsBuilder<ContentCmsDbContext>().UseSqlite(_connection).Options);
            _db.Database.EnsureCreated();
            var publisher = new ContentEventPublisher(_db, _bus, NullLogger<ContentEventPublisher>.Instance);
            _service = new ContentBrowseService(_db, publisher);

            _alice = AddUser("alice");
            _bob = AddUser("bob");
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private UserModel AddUser(string name)
        {
            var user = new UserModel { Username = name, Email = $"{name}@example.com", Password = "x" };
            _db.Users.Add(user);
            _db.SaveChanges();
            return user;
        }

        private GroupModel AddGroup(UserModel owner, string name)
        {
            var group = new GroupModel { Name = name, OwnerId = owner.Id };
            _db.Groups.Add(group);
            _db.SaveChanges();
            return group;
        }

        private ContentModel AddContent(UserModel owner, GroupModel? group = null, bool deleted = false, DateTime? createdAt = null)
        {
            var content = new ContentModel
            {
                OwnerId = owner.Id,
                GroupId = group?.Id,
                Path = $"http://cms/img/{Guid.NewGuid()}.png",
                IsDeleted = deleted,
                CreatedAt = createdAt ?? DateTime.UtcNow
            };
            _db.Contents.Add(content);
            _db.SaveChanges();
            return content;
        }

        [Fact]
        public async Task Feed_ShowsOwnNotDeletedContent_NewestFirst_WithRelations()
        {
            var cats = AddGroup(_alice, "Cats");
            var older = AddContent(_alice, cats, createdAt: DateTime.UtcNow.AddDays(-1));
            var newer = AddContent(_alice);
            AddContent(_alice, deleted: true);
            AddContent(_bob);
            var tag = new TagModel { Name = "cute", OwnerId = _alice.Id };
            _db.Tags.Add(tag);
            older.Tags.Add(tag);
            _db.SaveChanges();

            var page = await _service.GetContentAsync(_alice.Id, isAdmin: false, new ContentQuery());

            Assert.Equal(2, page.TotalCount);
            Assert.Equal(new[] { newer.Id, older.Id }, page.Items.Select(c => c.Id));
            var item = page.Items[1];
            Assert.Equal("Cats", item.Group!.Name);
            Assert.Equal("alice", item.Owner.Username);
            Assert.Equal("cute", Assert.Single(item.Tags).Name);
        }

        [Fact]
        public async Task Feed_FiltersByGroupAndTag_AndPages()
        {
            var cats = AddGroup(_alice, "Cats");
            for (int i = 0; i < 5; i++) AddContent(_alice, cats);
            AddContent(_alice);
            var tagged = AddContent(_alice, cats);
            var tag = new TagModel { Name = "best", OwnerId = _alice.Id };
            _db.Tags.Add(tag);
            tagged.Tags.Add(tag);
            _db.SaveChanges();

            var inGroup = await _service.GetContentAsync(_alice.Id, false, new ContentQuery(GroupId: cats.Id, PageSize: 4, Page: 2));
            Assert.Equal(6, inGroup.TotalCount);
            Assert.Equal(2, inGroup.Items.Count);

            var byTag = await _service.GetContentAsync(_alice.Id, false, new ContentQuery(TagIds: new[] { tag.Id }));
            Assert.Equal(tagged.Id, Assert.Single(byTag.Items).Id);
        }

        [Fact]
        public async Task Admin_SeesEveryonesContentAndGroups()
        {
            AddGroup(_alice, "Cats");
            AddGroup(_bob, "Dogs");
            AddContent(_alice);
            AddContent(_bob);

            Assert.Equal(2, (await _service.GetContentAsync(_alice.Id, isAdmin: true, new ContentQuery())).TotalCount);
            Assert.Equal(new[] { "Cats", "Dogs" }, (await _service.GetGroupsAsync(_alice.Id, isAdmin: true)).Select(g => g.Group.Name));
            Assert.Equal(new[] { "Cats" }, (await _service.GetGroupsAsync(_alice.Id, isAdmin: false)).Select(g => g.Group.Name));
        }

        [Fact]
        public async Task Groups_CountOnlyNotDeletedContent()
        {
            var cats = AddGroup(_alice, "Cats");
            AddContent(_alice, cats);
            AddContent(_alice, cats);
            AddContent(_alice, cats, deleted: true);

            var summary = Assert.Single(await _service.GetGroupsAsync(_alice.Id, false));
            Assert.Equal(2, summary.ContentCount);
        }

        [Fact]
        public async Task SetGroup_MovesOwnContentIntoOwnGroup_AndPublishesGroupChanged()
        {
            var cats = AddGroup(_alice, "Cats");
            var content = AddContent(_alice);

            Assert.Equal(ContentAccessResult.Success, await _service.SetGroupAsync(content.Id, cats.Id, _alice.Id, false));
            Assert.Equal(cats.Id, _db.Contents.AsNoTracking().Single(c => c.Id == content.Id).GroupId);
            var ev = Assert.Single(_bus.Events);
            Assert.Equal(ContentEventType.GroupChanged, ev.EventType);
            Assert.Equal(cats.Id, ev.GroupId);

            // Same group again: nothing to do, no extra event.
            Assert.Equal(ContentAccessResult.Success, await _service.SetGroupAsync(content.Id, cats.Id, _alice.Id, false));
            Assert.Single(_bus.Events);

            Assert.Equal(ContentAccessResult.Success, await _service.SetGroupAsync(content.Id, null, _alice.Id, false));
            Assert.Null(_db.Contents.AsNoTracking().Single(c => c.Id == content.Id).GroupId);
        }

        [Fact]
        public async Task SetGroup_RejectsForeignContentAndForeignGroups()
        {
            var aliceGroup = AddGroup(_alice, "Cats");
            var bobGroup = AddGroup(_bob, "Dogs");
            var aliceContent = AddContent(_alice);
            var bobContent = AddContent(_bob);
            var deleted = AddContent(_alice, deleted: true);

            Assert.Equal(ContentAccessResult.Forbidden, await _service.SetGroupAsync(bobContent.Id, aliceGroup.Id, _alice.Id, false));
            Assert.Equal(ContentAccessResult.Forbidden, await _service.SetGroupAsync(aliceContent.Id, bobGroup.Id, _alice.Id, false));
            Assert.Equal(ContentAccessResult.NotFound, await _service.SetGroupAsync(aliceContent.Id, 9999, _alice.Id, false));
            Assert.Equal(ContentAccessResult.NotFound, await _service.SetGroupAsync(deleted.Id, aliceGroup.Id, _alice.Id, false));
            Assert.Empty(_bus.Events);

            // Admins may organise anybody's content.
            Assert.Equal(ContentAccessResult.Success, await _service.SetGroupAsync(bobContent.Id, bobGroup.Id, _alice.Id, isAdmin: true));
        }

        [Fact]
        public async Task ContentById_IsLimitedToTheCaller()
        {
            var bobContent = AddContent(_bob);

            Assert.Null(await _service.GetContentByIdAsync(bobContent.Id, _alice.Id, false));
            Assert.NotNull(await _service.GetContentByIdAsync(bobContent.Id, _alice.Id, true));
            Assert.Equal(ContentAccessResult.Forbidden, await _service.CanAccessGroupAsync(AddGroup(_bob, "Dogs").Id, _alice.Id, false));
        }
    }
}
