namespace Merfit.Application.Common.Models;

public sealed class PageRequest
{
    private const int MaxPageSize = 100;
    private int _pageSize = 20;

    public int Page { get; init; } = 1;

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value <= 0 ? 20 : Math.Min(value, MaxPageSize);
    }

    public int Skip => (Math.Max(Page, 1) - 1) * PageSize;
}

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount) =>
        new() { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
}
