using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Commendable;

// One JSON Lines file per duty. Names start with the local start time, so ordinal order is oldest first.
internal sealed class Journal(string folder)
{
    public const int Format = 1;

    private readonly StringBuilder pending = new();
    private string? current;

    public string Folder => folder;

    public static List<string> Saved(string folder) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.jsonl").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
            : [];

    public static IEnumerable<Observation> Read(string path)
    {
        // A crash can leave a cut-off last line, which is skipped.
        foreach (var line in File.ReadLines(path))
        {
            if (ObservationJson.Read(line) is { } observation)
                yield return observation;
        }
    }

    // Keeps the newest `keep` files counting the new one; 0 saves nothing.
    public void Start(DutyStart start, int keep)
    {
        Stop();
        if (keep <= 0)
            return;

        Directory.CreateDirectory(folder);
        foreach (var old in Saved(folder).Skip(keep - 1))
            File.Delete(old);
        current = Path.Combine(folder, $"{start.StartedAt.ToLocalTime():yyyyMMdd-HHmmss} {SafeName(start.DutyName)}.jsonl");
    }

    public void Continue(string path)
    {
        Stop();
        current = path;
    }

    public void Append(Observation observation)
    {
        if (current != null)
            pending.AppendLine(ObservationJson.Write(observation));
    }

    public void Flush()
    {
        if (current == null || pending.Length == 0)
            return;
        var text = pending.ToString();
        pending.Clear();
        File.AppendAllText(current, text);
    }

    public void Stop()
    {
        try
        {
            Flush();
        }
        finally
        {
            current = null;
            pending.Clear();
        }
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
