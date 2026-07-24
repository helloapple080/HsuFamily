using System.Linq.Expressions;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Common;
using FamilyPortal.Application.DTOs.Member;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Services;
using FamilyPortal.Domain.Entities;
using Xunit;
using LunarDate = FamilyPortal.Domain.ValueObjects.LunarDate;

namespace FamilyPortal.Security.Tests.Application;

/// <summary>驗證家庭成員 use case 的資源限制與不可信農曆文字處理。</summary>
public sealed class MemberServiceTests
{
    /// <summary>建立成員時必須以資料欄位重建農曆顯示文字。</summary>
    [Fact]
    public async Task CreateAsync_RebuildsUntrustedLunarDisplayText()
    {
        var repository = new FakeMemberRepository();
        var service = new MemberService(repository);
        var request = ValidRequest();
        request.LunarBirthday = new LunarDateRequest
        {
            Year = 2026,
            Month = 1,
            Day = 1,
            DisplayText = "不可信內容"
        };

        var result = await service.CreateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("農曆正月初一", result.Value!.LunarBirthday!.DisplayText);
        Assert.Equal("農曆正月初一", repository.Created!.LunarBirthday!.DisplayText);
    }

    /// <summary>閏月顯示文字應由伺服器依月日與閏月旗標產生。</summary>
    [Fact]
    public async Task CreateAsync_FormatsLeapLunarBirthday()
    {
        var repository = new FakeMemberRepository();
        var service = new MemberService(repository);
        var request = ValidRequest();
        request.LunarBirthday = new LunarDateRequest
        {
            Year = 2026,
            Month = 4,
            Day = 15,
            IsLeapMonth = true,
            DisplayText = "任意文字"
        };

        var result = await service.CreateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("農曆閏四月十五", result.Value!.LunarBirthday!.DisplayText);
    }

    /// <summary>啟用成員查詢必須要求不超過一百筆，並傳遞取消權杖。</summary>
    [Fact]
    public async Task GetActiveAsync_RequestsAtMostOneHundredMembers()
    {
        var repository = new FakeMemberRepository();
        var service = new MemberService(repository);
        using var source = new CancellationTokenSource();

        var result = await service.GetActiveAsync(source.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(ValidationLimits.MaxPageSize, repository.LastLimit);
        Assert.Equal(source.Token, repository.LastCancellationToken);
    }

    /// <summary>讀取成員時必須忽略資料庫中的偽造文字並重建標準農曆顯示。</summary>
    [Fact]
    public async Task GetByIdAsync_RebuildsStoredLunarDisplayText()
    {
        var repository = new FakeMemberRepository
        {
            Stored = new Member
            {
                Id = "member-1",
                Name = "許小明",
                Title = "家人",
                LunarBirthday = new LunarDate
                {
                    Year = 2026,
                    Month = 1,
                    Day = 1,
                    DisplayText = "<script>不可信內容</script>"
                }
            }
        };
        var service = new MemberService(repository);

        var result = await service.GetByIdAsync("member-1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("農曆正月初一", result.Value!.LunarBirthday!.DisplayText);
        Assert.NotSame(repository.Stored.LunarBirthday, result.Value.LunarBirthday);
    }

    /// <summary>讀取無效歷史月日時不可拋出例外或轉送資料庫中的顯示文字。</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(13, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 31)]
    public async Task GetByIdAsync_InvalidLegacyLunarDateUsesDeterministicFallback(
        int month,
        int day)
    {
        var repository = new FakeMemberRepository
        {
            Stored = new Member
            {
                Id = "member-1",
                Name = "許小明",
                Title = "家人",
                LunarBirthday = new LunarDate
                {
                    Year = 2026,
                    Month = month,
                    Day = day,
                    DisplayText = "不應轉送"
                }
            }
        };
        var service = new MemberService(repository);

        var result = await service.GetByIdAsync("member-1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(month, result.Value!.LunarBirthday!.Month);
        Assert.Equal(day, result.Value.LunarBirthday.Day);
        Assert.Equal("農曆日期無效", result.Value.LunarBirthday.DisplayText);
    }

    /// <summary>更新不存在的成員時不可寫入資料。</summary>
    [Fact]
    public async Task UpdateAsync_MissingMemberReturnsNotFoundWithoutWrite()
    {
        var repository = new FakeMemberRepository();
        var service = new MemberService(repository);

        var result = await service.UpdateAsync("missing", ValidRequest(), CancellationToken.None);

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Null(repository.Updated);
    }

    /// <summary>更新成員時必須保留建立時間並重建農曆顯示文字。</summary>
    [Fact]
    public async Task UpdateAsync_PreservesCreationMetadataAndRebuildsLunarDisplayText()
    {
        var createdAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var repository = new FakeMemberRepository
        {
            Stored = new Member
            {
                Id = "member-1",
                Name = "舊姓名",
                Title = "舊稱謂",
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            }
        };
        var service = new MemberService(repository);
        var request = ValidRequest();
        request.LunarBirthday = new LunarDateRequest
        {
            Year = 2026,
            Month = 12,
            Day = 30,
            DisplayText = "不可採信"
        };

        var result = await service.UpdateAsync("member-1", request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.Updated);
        Assert.Equal("member-1", repository.Updated!.Id);
        Assert.Equal(createdAt, repository.Updated.CreatedAt);
        Assert.True(repository.Updated.UpdatedAt >= createdAt);
        Assert.Equal("農曆臘月三十", repository.Updated.LunarBirthday!.DisplayText);
    }

    private static UpsertMemberRequest ValidRequest() => new()
    {
        Name = "許小明",
        Title = "家人",
        Birthday = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        IsActive = true
    };

    private sealed class FakeMemberRepository : IMemberRepository
    {
        public Member? Stored { get; init; }
        public Member? Created { get; private set; }
        public Member? Updated { get; private set; }
        public int? LastLimit { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        /// <inheritdoc />
        public Task<List<Member>> GetActiveOrderedAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            LastLimit = limit;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(new List<Member>());
        }

        /// <inheritdoc />
        public Task<Member?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(Stored?.Id == id ? Stored : null);
        }

        /// <inheritdoc />
        public Task<List<Member>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Member>());

        /// <inheritdoc />
        public Task<List<Member>> FindAsync(
            Expression<Func<Member, bool>> filter,
            CancellationToken cancellationToken = default) => Task.FromResult(new List<Member>());

        /// <inheritdoc />
        public Task<Member?> FindOneAsync(
            Expression<Func<Member, bool>> filter,
            CancellationToken cancellationToken = default) => Task.FromResult<Member?>(null);

        /// <inheritdoc />
        public Task CreateAsync(Member entity, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            Created = entity;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task UpdateAsync(string id, Member entity, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            Updated = entity;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(
            Expression<Func<Member, bool>> filter,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        /// <inheritdoc />
        public Task<long> CountAsync(
            Expression<Func<Member, bool>>? filter = null,
            CancellationToken cancellationToken = default) => Task.FromResult(0L);
    }
}
