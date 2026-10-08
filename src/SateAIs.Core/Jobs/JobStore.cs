using System.Text.Json;
using SateAIs.Core.Wording;

namespace SateAIs.Core.Jobs;

/// <summary>
/// The jobs this user follows, kept in one JSON file per user so the list survives across
/// projects and restarts. Every operation reads the whole list, changes it and writes it back.
/// </summary>
public sealed class JobStore
{
    public static readonly IReadOnlySet<string> ValidStatuses =
        new HashSet<string> { "pending", "processing", "completed", "failed", "unknown" };

    private static readonly string[] RequestKeys = ["scene_id", "date", "date_start", "date_end"];

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public JobStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SateAIs", "ArcGISPro", "jobs.json");

    /// <summary>Newest first by submission time; entries whose time cannot be read go last.</summary>
    public IReadOnlyList<TrackedJob> ListAll() =>
        Read()
            .Select((job, index) => (job, index, when: JobSummary.ParseIso8601(job.SubmittedAt)))
            .OrderBy(x => x.when is null ? 1 : 0)
            .ThenByDescending(x => x.when ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.index)
            .Select(x => x.job)
            .ToList();

    /// <summary>Track a job. A job already tracked is returned unchanged.</summary>
    public TrackedJob Add(
        string analysisType,
        string jobId,
        string? submittedAt = null,
        string status = "pending",
        IReadOnlyDictionary<string, string?>? request = null,
        string requestSource = "")
    {
        var jobs = Read();
        var existing = jobs.FirstOrDefault(j => j.JobId == jobId);
        if (existing is not null)
        {
            return existing;
        }
        var context = RequestContext(request);
        var job = new TrackedJob
        {
            JobId = jobId,
            AnalysisType = analysisType,
            SubmittedAt = submittedAt ?? JobSummary.IsoString(DateTimeOffset.UtcNow),
            Status = ValidStatuses.Contains(status) ? status : "unknown",
            Polygon = context.GetValueOrDefault("polygon"),
            SceneId = context.GetValueOrDefault("scene_id"),
            Date = context.GetValueOrDefault("date"),
            DateStart = context.GetValueOrDefault("date_start"),
            DateEnd = context.GetValueOrDefault("date_end"),
            RequestSource = requestSource,
        };
        jobs.Insert(0, job);
        Write(jobs);
        return job;
    }

    /// <summary>Update a job's status. Figures are only overwritten when given, so a plain poll keeps what a refresh learned.</summary>
    public TrackedJob? UpdateStatus(
        string jobId,
        string status,
        string? errorCode = null,
        string? errorMessage = null,
        string? completedAt = null,
        double? areaSqkm = null,
        double? creditsUsed = null)
    {
        var jobs = Read();
        var job = jobs.FirstOrDefault(j => j.JobId == jobId);
        if (job is null)
        {
            return null;
        }
        job.Status = ValidStatuses.Contains(status) ? status : "unknown";
        job.ErrorCode = errorCode ?? job.ErrorCode;
        job.ErrorMessage = errorMessage ?? job.ErrorMessage;
        job.CompletedAt = completedAt ?? job.CompletedAt;
        job.AreaSqkm = areaSqkm ?? job.AreaSqkm;
        job.CreditsUsed = creditsUsed ?? job.CreditsUsed;
        Write(jobs);
        return job;
    }

    /// <summary>
    /// Merge request parameters learned later (from a refresh). Incoming values win, except the
    /// polygon, which is only filled when missing so the exact area the user drew is kept.
    /// </summary>
    public TrackedJob? SetRequestContext(string jobId, IReadOnlyDictionary<string, string?>? request, string source)
    {
        var context = RequestContext(request);
        var jobs = Read();
        var job = jobs.FirstOrDefault(j => j.JobId == jobId);
        if (job is null)
        {
            return null;
        }
        job.SceneId = context.GetValueOrDefault("scene_id") ?? job.SceneId;
        job.Date = context.GetValueOrDefault("date") ?? job.Date;
        job.DateStart = context.GetValueOrDefault("date_start") ?? job.DateStart;
        job.DateEnd = context.GetValueOrDefault("date_end") ?? job.DateEnd;
        job.Polygon ??= context.GetValueOrDefault("polygon");
        var keepSource = source.Length == 0 || (source == "unavailable" && job.RequestSource.Length > 0);
        if (!keepSource)
        {
            job.RequestSource = source;
        }
        Write(jobs);
        return job;
    }

    public TrackedJob? SetDetectionCount(string jobId, int count)
    {
        var jobs = Read();
        var job = jobs.FirstOrDefault(j => j.JobId == jobId);
        if (job is null)
        {
            return null;
        }
        job.DetectionCount = Math.Max(0, count);
        Write(jobs);
        return job;
    }

    public bool Remove(string jobId)
    {
        var jobs = Read();
        var removed = jobs.RemoveAll(j => j.JobId == jobId) > 0;
        if (removed)
        {
            Write(jobs);
        }
        return removed;
    }

    /// <summary>Drop jobs submitted more than <paramref name="days"/> ago. Entries without a readable time are kept.</summary>
    public int CleanupExpired(int days = 30, DateTimeOffset? now = null)
    {
        var cutoff = (now ?? DateTimeOffset.UtcNow).AddDays(-days);
        var jobs = Read();
        var removed = jobs.RemoveAll(j => JobSummary.ParseIso8601(j.SubmittedAt) is { } when && when < cutoff);
        if (removed > 0)
        {
            Write(jobs);
        }
        return removed;
    }

    /// <summary>Only the allowlisted, non-empty string parameters of a request.</summary>
    internal static Dictionary<string, string> RequestContext(IReadOnlyDictionary<string, string?>? request)
    {
        var context = new Dictionary<string, string>();
        if (request is null)
        {
            return context;
        }
        foreach (var key in RequestKeys.Append("polygon"))
        {
            if (request.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            {
                context[key] = value;
            }
        }
        return context;
    }

    /// <summary>The string parameters of a server-side <c>request_params</c> object.</summary>
    public static Dictionary<string, string?> RequestFromJson(JsonElement? requestParams)
    {
        var request = new Dictionary<string, string?>();
        if (requestParams is not { ValueKind: JsonValueKind.Object } element)
        {
            return request;
        }
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                request[property.Name] = property.Value.GetString();
            }
        }
        return request;
    }

    private List<TrackedJob> Read()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            return document.RootElement.EnumerateArray()
                .Select(TrackedJob.FromJson)
                .Where(job => job is not null)
                .Select(job => job!)
                .ToList();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return [];
        }
    }

    private void Write(List<TrackedJob> jobs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(jobs, WriteOptions));
        File.Move(temp, FilePath, overwrite: true);
    }
}
