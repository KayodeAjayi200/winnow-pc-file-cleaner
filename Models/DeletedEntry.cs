namespace FileTinder.Models;

/// <summary>Record of a file deleted during this session (local files only).</summary>
public class DeletedEntry
{
    public FileItem File       { get; init; } = null!;
    public DateTime DeletedAt  { get; init; } = DateTime.Now;

    public string TimeFormatted => DeletedAt.ToString("HH:mm:ss");
}
