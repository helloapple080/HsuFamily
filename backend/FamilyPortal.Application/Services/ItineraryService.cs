using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Common;
using FamilyPortal.Application.DTOs.Itinerary;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.ValueObjects;

namespace FamilyPortal.Application.Services;

/// <summary>協調行程資料存取、資源上限與 transport 映射。</summary>
public sealed class ItineraryService(IItineraryRepository itineraryRepository) : IItineraryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ItineraryResponse>>> GetPagedAsync(
        PagedItineraryRequest request,
        CancellationToken cancellationToken)
    {
        var itineraries = await itineraryRepository.GetPagedAsync(
            request.Tag,
            request.CursorDate,
            request.CursorId,
            ClampLimit(request.PageSize),
            cancellationToken);

        return SuccessList(itineraries);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ItineraryResponse>> GetByIdAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var itinerary = await itineraryRepository.GetByIdAsync(id, cancellationToken);
        return itinerary is null
            ? ServiceResult<ItineraryResponse>.Failure(ServiceError.NotFound, "行程不存在")
            : ServiceResult<ItineraryResponse>.Success(MapToResponse(itinerary));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ItineraryResponse>>> SearchAsync(
        string keyword,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedKeyword = keyword?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            return ServiceResult<IReadOnlyList<ItineraryResponse>>.Failure(
                ServiceError.Validation,
                "搜尋關鍵字不可為空");
        }

        if (normalizedKeyword.Length > ValidationLimits.MaxShortTextLength)
        {
            return ServiceResult<IReadOnlyList<ItineraryResponse>>.Failure(
                ServiceError.Validation,
                $"搜尋關鍵字最多 {ValidationLimits.MaxShortTextLength} 個字元");
        }

        var itineraries = await itineraryRepository.SearchAsync(
            normalizedKeyword,
            ClampLimit(limit),
            cancellationToken);

        return SuccessList(itineraries);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ItineraryResponse>>> GetByParticipantAsync(
        string memberId,
        int limit,
        CancellationToken cancellationToken)
    {
        var itineraries = await itineraryRepository.GetByParticipantAsync(
            memberId,
            ClampLimit(limit),
            cancellationToken);

        return SuccessList(itineraries);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ItineraryResponse>> CreateAsync(
        UpsertItineraryRequest request,
        string userId,
        CancellationToken cancellationToken)
    {
        var itinerary = MapRequest(request);
        itinerary.CreatedBy = userId;

        await itineraryRepository.CreateAsync(itinerary, cancellationToken);
        return ServiceResult<ItineraryResponse>.Success(MapToResponse(itinerary));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ItineraryResponse>> UpdateAsync(
        string id,
        UpsertItineraryRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await itineraryRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return ServiceResult<ItineraryResponse>.Failure(ServiceError.NotFound, "行程不存在");
        }

        var itinerary = MapRequest(request);
        itinerary.Id = existing.Id;
        itinerary.CreatedBy = existing.CreatedBy;
        itinerary.CreatedAt = existing.CreatedAt;
        itinerary.UpdatedAt = DateTime.UtcNow;

        await itineraryRepository.UpdateAsync(id, itinerary, cancellationToken);
        return ServiceResult<ItineraryResponse>.Success(MapToResponse(itinerary));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> DeleteAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var existing = await itineraryRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return ServiceResult<bool>.Failure(ServiceError.NotFound, "行程不存在");
        }

        await itineraryRepository.DeleteAsync(id, cancellationToken);
        return ServiceResult<bool>.Success(true);
    }

    private static int ClampLimit(int limit) =>
        Math.Clamp(limit, 1, ValidationLimits.MaxPageSize);

    private static ServiceResult<IReadOnlyList<ItineraryResponse>> SuccessList(
        IEnumerable<Itinerary> itineraries) =>
        ServiceResult<IReadOnlyList<ItineraryResponse>>.Success(
            itineraries.Select(MapToResponse).ToList());

    private static Itinerary MapRequest(UpsertItineraryRequest request) => new()
    {
        Title = request.Title,
        Description = request.Description,
        Date = request.Date,
        EndDate = request.EndDate,
        Location = request.Location,
        Tags = [.. request.Tags],
        Participants = request.Participants.Select(MapParticipant).ToList(),
        Photos = [.. request.Photos],
        Notes = request.Notes,
        LinkedEventId = request.LinkedEventId
    };

    private static ParticipantSnapshot MapParticipant(ParticipantRequest request) => new()
    {
        MemberId = request.MemberId,
        Name = request.Name
    };

    private static ItineraryResponse MapToResponse(Itinerary itinerary) => new()
    {
        Id = itinerary.Id,
        Title = itinerary.Title,
        Description = itinerary.Description,
        Date = itinerary.Date,
        EndDate = itinerary.EndDate,
        Location = itinerary.Location,
        Tags = [.. itinerary.Tags],
        Participants = itinerary.Participants.Select(participant => new ParticipantSnapshot
        {
            MemberId = participant.MemberId,
            Name = participant.Name
        }).ToList(),
        Photos = [.. itinerary.Photos],
        Notes = itinerary.Notes,
        LinkedEventId = itinerary.LinkedEventId,
        CreatedBy = itinerary.CreatedBy,
        CreatedAt = itinerary.CreatedAt,
        UpdatedAt = itinerary.UpdatedAt
    };
}
