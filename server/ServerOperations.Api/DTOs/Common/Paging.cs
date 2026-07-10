namespace ServerOperations.Api.DTOs.Common;

/// <summary>一覧APIの共通ページング入力。</summary>
public record PagingQuery
{
    public const int MaxPageSize = 100;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize switch
    {
        < 1 => 20,
        > MaxPageSize => MaxPageSize,
        _ => PageSize,
    };

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}

/// <summary>一覧APIの共通ページング出力。</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public long TotalPages => PageSize == 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
}
