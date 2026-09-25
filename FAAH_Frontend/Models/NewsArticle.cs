using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
namespace FAAH_Frontend.Models;
public sealed class NewsResponse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("page_size")] public int PageSize { get; set; }
    [JsonPropertyName("items")] public required List<NewsArticle> Items { get; set; }
}
public sealed class NewsArticle
{
    [JsonPropertyName("src_id")] public required int Id { get; set; }
    [JsonPropertyName("src_title")] public string? RawTitle { get; set; }
    [JsonPropertyName("src_content")] public string? Content { get; set; }
    [JsonPropertyName("src_type")] public string? SourceType { get; set; }
    [JsonPropertyName("src_original_url")] public string? OriginalUrl { get; set; }
    [JsonPropertyName("src_published_at")] public DateTime? PublishedAt { get; set; }
    [JsonPropertyName("src_created_at")] public DateTime? CreatedAt { get; set; }
    public string Title => string.IsNullOrWhiteSpace(RawTitle) ? "Untitled article" : RawTitle;
    public string Summary => string.IsNullOrWhiteSpace(Content) ? "No content available." : Content.Length > 450 ? Content[..450] + "…" : Content;
    public string Source => Uri.TryCreate(OriginalUrl, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http") ? uri.Host : SourceType ?? "Unknown source";
    public DateTime? SortDate => PublishedAt ?? CreatedAt;
    // Preserve backend wall time: PostgreSQL fields have no declared timezone.
    public string DateDisplay => SortDate?.ToString("yyyy-MM-dd HH:mm") ?? "Date unavailable";
    public string DateLabel => PublishedAt is not null ? "Published" : "Collected";
}
