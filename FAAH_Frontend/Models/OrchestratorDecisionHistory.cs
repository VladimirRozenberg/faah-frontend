using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FAAH_Frontend.Models;

public sealed class OrchestratorDecisionHistoryResponse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("page_size")] public int PageSize { get; set; }
    [JsonPropertyName("items")] public List<OrchestratorDecisionCycle> Items { get; set; } = new();
}

public sealed class OrchestratorDecisionCycle : INotifyPropertyChanged
{
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;
    [JsonPropertyName("cycle_id")] public long CycleId { get; set; }
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("situation_summary")] public string? SituationSummary { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("history_complete")] public bool? HistoryComplete { get; set; }
    [JsonPropertyName("rejected_rss_proposals")] public int RejectedRssProposals { get; set; }
    [JsonPropertyName("rss_decisions")] public List<OrchestratorRssDecision> RssDecisions { get; set; } = new();
    [JsonPropertyName("follow_up_analysis")] public List<OrchestratorResearchRequest> FollowUpAnalysis { get; set; } = new();
    [JsonPropertyName("follow_up_jobs")] public List<OrchestratorResearchJob> FollowUpJobs { get; set; } = new();
    [JsonPropertyName("next_instructions")] public string? NextInstructions { get; set; }
    [JsonPropertyName("next_run_at")] public DateTimeOffset? NextRunAt { get; set; }
    [JsonPropertyName("signal_ids")] public List<JsonElement> SignalIds { get; set; } = new();
    [JsonIgnore] public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }
    [JsonIgnore] public string StartedAtDisplay => StartedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "Unavailable";
    [JsonIgnore] public string NextRunAtDisplay => NextRunAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "Not planned";
    [JsonIgnore] public string SourceDisplay => string.IsNullOrWhiteSpace(Source) ? "Unavailable" : Source;
    [JsonIgnore] public string StatusDisplay => string.IsNullOrWhiteSpace(Status) ? "Unavailable" : Status;
    [JsonIgnore] public bool HasError => !string.IsNullOrWhiteSpace(Error);
    [JsonIgnore] public bool HasRssDecisions => RssDecisions.Count > 0;
    [JsonIgnore] public bool HasResearchRequests => FollowUpAnalysis.Count > 0;
    [JsonIgnore] public bool HasResearchJobs => FollowUpJobs.Count > 0;
    [JsonIgnore] public bool HasNextInstructions => !string.IsNullOrWhiteSpace(NextInstructions);
    [JsonIgnore] public bool HasSignals => SignalIds.Count > 0;
    [JsonIgnore] public bool HasRejectedRssProposals => RejectedRssProposals > 0;
    [JsonIgnore] public bool IsPartialHistory => HistoryComplete == false;
}

public sealed class OrchestratorRssDecision
{
    [JsonPropertyName("proposal")] public OrchestratorRssProposal Proposal { get; set; } = new();
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("rejection_reason")] public string? RejectionReason { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonIgnore] public string StatusMeaning => Status switch
    {
        "pending" => "Awaiting evaluation",
        "rejected" => "Policy rejected it",
        "approved" => "Approved; application is unconfirmed",
        "applied" => "Schedule change succeeded; feed execution is not confirmed",
        "failed" => "Application failed",
        "legacy_approved" => "Historical approval; application result unknown",
        _ => Status ?? "Unavailable"
    };
    [JsonIgnore] public bool HasRejectionReason => !string.IsNullOrWhiteSpace(RejectionReason);
    [JsonIgnore] public bool HasError => !string.IsNullOrWhiteSpace(Error);
}

public sealed class OrchestratorRssProposal
{
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("target")] public string? Target { get; set; }
    [JsonPropertyName("parameters")] public JsonElement? Parameters { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("confidence")] public double? Confidence { get; set; }
    [JsonIgnore] public string ParametersDisplay => Parameters is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } value
        ? value.ToString()
        : "Unavailable";
    [JsonIgnore] public string ConfidenceDisplay => Confidence is null ? "Unavailable" : $"{Confidence.Value * 100:0.##}%";
}

public sealed class OrchestratorResearchRequest
{
    [JsonPropertyName("asset_symbol")] public string? AssetSymbol { get; set; }
    [JsonPropertyName("question")] public string? Question { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("priority")] public int? Priority { get; set; }
    [JsonIgnore] public string PriorityDisplay => Priority is null ? "Unavailable" : $"{Priority} / 5";
}

public sealed class OrchestratorResearchJob
{
    [JsonPropertyName("asset_symbol")] public string? AssetSymbol { get; set; }
    [JsonPropertyName("question")] public string? Question { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("result_analysis_id")] public long? ResultAnalysisId { get; set; }
    [JsonPropertyName("result_summary")] public string? ResultSummary { get; set; }
    [JsonPropertyName("result_text")] public string? ResultText { get; set; }
    [JsonIgnore] public bool HasError => !string.IsNullOrWhiteSpace(Error);
    [JsonIgnore] public bool HasResultAnalysis => ResultAnalysisId.HasValue;
    [JsonIgnore] public bool HasResultContent => !string.IsNullOrWhiteSpace(ResultSummary) || !string.IsNullOrWhiteSpace(ResultText);
}
