using System.Linq.Expressions;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Itinerary;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Services;
using FamilyPortal.Domain.Entities;
using Xunit;

namespace FamilyPortal.Security.Tests.Application;

/// <summary>驗證行程 use case 的查詢上限、映射與不存在錯誤。</summary>
public sealed class ItineraryServiceTests
{
    /// <summary>搜尋筆數必須夾在一至一百筆之間。</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(101, 100)]
    [InlineData(50, 50)]
    public async Task SearchAsync_ClampsLimit(int requested, int expected)
    {
        var repository = new FakeItineraryRepository();
        var service = new ItineraryService(repository);

        var result = await service.SearchAsync("京都", requested, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, repository.LastLimit);
    }

    /// <summary>參與者查詢必須限制筆數、維持傳入識別碼並傳遞取消權杖。</summary>
    [Fact]
    public async Task ParticipantQuery_UsesBoundedStablePage()
    {
        var repository = new FakeItineraryRepository();
        var service = new ItineraryService(repository);
        using var source = new CancellationTokenSource();

        var result = await service.GetByParticipantAsync("member-1", 101, source.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("member-1", repository.LastParticipantId);
        Assert.Equal(ValidationLimits.MaxPageSize, repository.LastLimit);
        Assert.Equal(source.Token, repository.LastCancellationToken);
    }

    /// <summary>分頁查詢也必須在 Application 邊界限制筆數。</summary>
    [Fact]
    public async Task GetPagedAsync_ClampsLimitAndForwardsCursor()
    {
        var repository = new FakeItineraryRepository();
        var service = new ItineraryService(repository);
        using var source = new CancellationTokenSource();
        var cursorDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var request = new PagedItineraryRequest
        {
            Tag = "家族旅遊",
            CursorDate = cursorDate,
            CursorId = "507f1f77bcf86cd799439011",
            PageSize = 101
        };

        var result = await service.GetPagedAsync(request, source.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("家族旅遊", repository.LastTag);
        Assert.Equal(cursorDate, repository.LastCursorDate);
        Assert.Equal("507f1f77bcf86cd799439011", repository.LastCursorId);
        Assert.Equal(ValidationLimits.MaxPageSize, repository.LastLimit);
        Assert.Equal(source.Token, repository.LastCancellationToken);
    }

    /// <summary>空白或過長搜尋字串不可送往資料庫。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_InvalidKeywordReturnsValidationWithoutQuery(string keyword)
    {
        var repository = new FakeItineraryRepository();
        var service = new ItineraryService(repository);

        var result = await service.SearchAsync(keyword, 20, CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Equal(0, repository.SearchCalls);
    }

    /// <summary>過長搜尋字串不可送往資料庫。</summary>
    [Fact]
    public async Task SearchAsync_OversizedKeywordReturnsValidationWithoutQuery()
    {
        var repository = new FakeItineraryRepository();
        var service = new ItineraryService(repository);

        var result = await service.SearchAsync(
            new string('關', ValidationLimits.MaxShortTextLength + 1),
            20,
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Equal(0, repository.SearchCalls);
    }

    /// <summary>建立行程應映射輸入、保存建立者並傳遞取消權杖。</summary>
    [Fact]
    public async Task CreateAsync_MapsRequestAndCreator()
    {
        var repository = new FakeItineraryRepository();
        var service = new ItineraryService(repository);
        using var source = new CancellationTokenSource();

        var result = await service.CreateAsync(ValidRequest(), "user-1", source.Token);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.Created);
        Assert.Equal("user-1", repository.Created.CreatedBy);
        Assert.Equal("member-1", repository.Created.Participants.Single().MemberId);
        Assert.Equal(source.Token, repository.LastCancellationToken);
    }

    /// <summary>更新不存在的行程時不可寫入資料。</summary>
    [Fact]
    public async Task UpdateAsync_MissingItineraryReturnsNotFoundWithoutWrite()
    {
        var repository = new FakeItineraryRepository();
        var service = new ItineraryService(repository);

        var result = await service.UpdateAsync(
            "missing",
            ValidRequest(),
            CancellationToken.None);

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Null(repository.Updated);
    }

    /// <summary>更新行程時必須保留識別碼、建立者與建立時間。</summary>
    [Fact]
    public async Task UpdateAsync_PreservesCreationMetadata()
    {
        var createdAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var repository = new FakeItineraryRepository
        {
            Stored = new Itinerary
            {
                Id = "itinerary-1",
                Title = "舊行程",
                CreatedBy = "creator-1",
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            }
        };
        var service = new ItineraryService(repository);

        var result = await service.UpdateAsync(
            "itinerary-1",
            ValidRequest(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.Updated);
        Assert.Equal("itinerary-1", repository.Updated.Id);
        Assert.Equal("creator-1", repository.Updated.CreatedBy);
        Assert.Equal(createdAt, repository.Updated.CreatedAt);
        Assert.True(repository.Updated.UpdatedAt >= createdAt);
    }

    /// <summary>刪除不存在的行程時應回傳 NotFound。</summary>
    [Fact]
    public async Task DeleteAsync_MissingItineraryReturnsNotFound()
    {
        var repository = new FakeItineraryRepository();
        var service = new ItineraryService(repository);

        var result = await service.DeleteAsync("missing", CancellationToken.None);

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Null(repository.DeletedId);
    }

    private static UpsertItineraryRequest ValidRequest() => new()
    {
        Title = "京都家族旅遊",
        Description = "家族年度旅遊",
        Date = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        EndDate = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
        Location = "京都",
        Tags = ["家族旅遊"],
        Participants =
        [
            new() { MemberId = "member-1", Name = "許小明" }
        ],
        Photos = ["https://example.com/photo.jpg"],
        Notes = "集合時間 08:00"
    };

    private sealed class FakeItineraryRepository : IItineraryRepository
    {
        public Itinerary? Stored { get; init; }
        public Itinerary? Created { get; private set; }
        public Itinerary? Updated { get; private set; }
        public string? DeletedId { get; private set; }
        public string? LastTag { get; private set; }
        public DateTime? LastCursorDate { get; private set; }
        public string? LastCursorId { get; private set; }
        public string? LastParticipantId { get; private set; }
        public int? LastLimit { get; private set; }
        public int SearchCalls { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        /// <inheritdoc />
        public Task<List<Itinerary>> GetPagedAsync(
            string? tag,
            DateTime? cursorDate,
            string? cursorId,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            LastTag = tag;
            LastCursorDate = cursorDate;
            LastCursorId = cursorId;
            LastLimit = pageSize;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(new List<Itinerary>());
        }

        /// <inheritdoc />
        public Task<List<Itinerary>> GetByParticipantAsync(
            string memberId,
            int limit,
            CancellationToken cancellationToken = default)
        {
            LastParticipantId = memberId;
            LastLimit = limit;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(new List<Itinerary>());
        }

        /// <inheritdoc />
        public Task<List<Itinerary>> SearchAsync(
            string keyword,
            int limit,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            LastLimit = limit;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(new List<Itinerary>());
        }

        /// <inheritdoc />
        public Task<Itinerary?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(Stored?.Id == id ? Stored : null);
        }

        /// <inheritdoc />
        public Task<List<Itinerary>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Itinerary>());

        /// <inheritdoc />
        public Task<List<Itinerary>> FindAsync(
            Expression<Func<Itinerary, bool>> filter,
            CancellationToken cancellationToken = default) => Task.FromResult(new List<Itinerary>());

        /// <inheritdoc />
        public Task<Itinerary?> FindOneAsync(
            Expression<Func<Itinerary, bool>> filter,
            CancellationToken cancellationToken = default) => Task.FromResult<Itinerary?>(null);

        /// <inheritdoc />
        public Task CreateAsync(Itinerary entity, CancellationToken cancellationToken = default)
        {
            Created = entity;
            LastCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task UpdateAsync(
            string id,
            Itinerary entity,
            CancellationToken cancellationToken = default)
        {
            Updated = entity;
            LastCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            DeletedId = id;
            LastCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(
            Expression<Func<Itinerary, bool>> filter,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        /// <inheritdoc />
        public Task<long> CountAsync(
            Expression<Func<Itinerary, bool>>? filter = null,
            CancellationToken cancellationToken = default) => Task.FromResult(0L);
    }
}
