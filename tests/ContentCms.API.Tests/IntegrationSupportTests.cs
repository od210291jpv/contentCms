using ContentCms.API.DTOs.Events;
using ContentCms.API.Models;
using ContentCms.API.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ContentCms.API.Tests
{
    /// <summary>Covers the pieces external consumers (UnoGame) rely on: the sync snapshot and change events.</summary>
    public sealed class IntegrationSupportTests : IDisposable
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
        private readonly ContentEventPublisher _publisher;
        private readonly UserModel _alice;
        private readonly UserModel _bob;

        public IntegrationSupportTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ContentCmsDbContext(new DbContextOptionsBuilder<ContentCmsDbContext>().UseSqlite(_connection).Options);
            _db.Database.EnsureCreated();
            _publisher = new ContentEventPublisher(_db, _bus, NullLogger<ContentEventPublisher>.Instance);

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

        private ContentModel AddContent(UserModel owner, GroupModel? group = null, bool deleted = false, bool enabled = true, bool isPublic = true)
        {
            var content = new ContentModel
            {
                OwnerId = owner.Id,
                GroupId = group?.Id,
                Path = $"/img/{Guid.NewGuid()}.png",
                IsDeleted = deleted,
                Enabled = enabled,
                IsPublic = isPublic
            };
            _db.Contents.Add(content);
            _db.SaveChanges();
            return content;
        }

        [Fact]
        public async Task Snapshot_IncludesDisabledPrivateAndDeletedOwnContent_WithTags()
        {
            var group = AddGroup(_alice, "Cats");
            var visible = AddContent(_alice, group);
            AddContent(_alice, deleted: true);
            AddContent(_alice, enabled: false, isPublic: false);
            AddContent(_bob);

            var tag = new TagModel { Name = "cute", OwnerId = _alice.Id };
            _db.Tags.Add(tag);
            visible.Tags.Add(tag);
            _db.SaveChanges();

            var service = new ContentService(_db, _publisher);
            var snapshot = await service.GetOwnedWithRelationsAsync(_alice.Id);

            Assert.Equal(3, snapshot.Count);
            Assert.All(snapshot, c => Assert.Equal(_alice.Id, c.OwnerId));
            var item = snapshot.Single(c => c.Id == visible.Id);
            Assert.Equal(group.Id, item.GroupId);
            Assert.Equal("cute", Assert.Single(item.Tags).Name);
        }

        [Fact]
        public async Task Publisher_IncludesGroupAndTagIds()
        {
            var group = AddGroup(_alice, "Dogs");
            var content = AddContent(_alice, group);
            var tag = new TagModel { Name = "good", OwnerId = _alice.Id };
            _db.Tags.Add(tag);
            content.Tags.Add(tag);
            _db.SaveChanges();

            await _publisher.PublishAsync(content.Id, ContentEventType.TagsChanged);

            var ev = Assert.Single(_bus.Events);
            Assert.Equal(ContentEventType.TagsChanged, ev.EventType);
            Assert.Equal(group.Id, ev.GroupId);
            Assert.Equal(new[] { tag.Id }, ev.TagIds);
        }

        [Fact]
        public async Task GroupBulkOperations_PublishOneEventPerContent()
        {
            var group = AddGroup(_alice, "Birds");
            AddContent(_alice, group);
            AddContent(_alice, group);
            AddContent(_alice); // not in the group
            var service = new GroupsService(_db, _publisher);

            await service.BulkToggleEnabledAsync(group.Id, false);
            Assert.Equal(2, _bus.Events.Count(e => e.EventType == ContentEventType.Disabled));

            await service.BulkToggleVisibilityAsync(group.Id, false);
            Assert.Equal(2, _bus.Events.Count(e => e.EventType == ContentEventType.MadePrivate));

            await service.BulkToggleDeleteAsync(group.Id, true);
            Assert.Equal(2, _bus.Events.Count(e => e.EventType == ContentEventType.Deleted && e.IsDeleted));
        }

        [Fact]
        public async Task DeletingGroup_PublishesGroupChangedWithNullGroup()
        {
            var group = AddGroup(_alice, "Fish");
            var content = AddContent(_alice, group);
            var service = new GroupsService(_db, _publisher);

            Assert.True(await service.DeleteGroupAsync(group.Id));

            var ev = Assert.Single(_bus.Events);
            Assert.Equal(ContentEventType.GroupChanged, ev.EventType);
            Assert.Equal(content.Id, ev.Id);
            Assert.Null(ev.GroupId);
            Assert.Null(_db.Contents.AsNoTracking().Single(c => c.Id == content.Id).GroupId);
        }
    }
}
