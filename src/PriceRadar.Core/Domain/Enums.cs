namespace PriceRadar.Core.Domain;

// Values are persisted as tinyint; never renumber existing members (spec §5.2).

public enum SourceKind : byte
{
    Fixture = 0,
    Synthetic = 1,
    Live = 2,
}

public enum RunStatus : byte
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}

/// <summary>The last pipeline step a run has completed.</summary>
public enum RunStage : byte
{
    Created = 0,
    Scraped = 1,
    Matched = 2,
    Resolved = 3,
    Loaded = 4,
}

public enum MatchStatus : byte
{
    Pending = 0,
    AutoMatched = 1,
    AiMatched = 2,
    NewBook = 3,
    Manual = 4,
}

public enum ProcessStatus : byte
{
    New = 0,
    Normalized = 1,
    Loaded = 2,
    Rejected = 3,
}

public enum ListingStatus : byte
{
    Active = 0,
    Disappeared = 1,
}

public enum BookOrigin : byte
{
    Seed = 0,
    Rule = 1,
    Ai = 2,
    Manual = 3,
}

public enum MatchDecision : byte
{
    Match = 0,
    NewBook = 1,
    Unsure = 2,
    Error = 3,
}
