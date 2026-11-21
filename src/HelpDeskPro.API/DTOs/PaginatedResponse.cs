namespace HelpDeskPro.API.DTOs;

/// <summary>Contains one page of results and its pagination metadata.</summary>
public sealed record PaginatedResponse<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);
