using LogoDetect.Models;

namespace LogoDetect.Services;

public class EdlGenerator
{
    public IEnumerable<EdlEntry> GenerateEdlEntries(
        IEnumerable<(TimeSpan Time, bool HasLogo)> keyframes,
        TimeSpan minDuration,
        IEnumerable<TimeSpan> sceneChanges)
    {
        var entries = new List<EdlEntry>();
        EdlEntry? currentEntry = null;
        var orderedSceneChanges = sceneChanges.OrderBy(x => x).ToList();

        foreach (var (time, hasLogo) in keyframes.OrderBy(x => x.Time))
        {
            if (hasLogo && currentEntry == null)
            {
                // Find the nearest scene change before this point
                var sceneChange = orderedSceneChanges
                    .Where(x => x <= time)
                    .LastOrDefault(time);

                currentEntry = new EdlEntry
                {
                    StartTime = sceneChange,
                    Description = "Logo Segment"
                };
            }
            else if (!hasLogo && currentEntry != null)
            {
                // Find the nearest scene change after this point
                var sceneChange = orderedSceneChanges
                    .Where(x => x >= time)
                    .FirstOrDefault(time);

                currentEntry.EndTime = sceneChange;

                if (currentEntry.EndTime - currentEntry.StartTime >= minDuration)
                {
                    entries.Add(currentEntry);
                }
                
                currentEntry = null;
            }
        }

        // Handle case where video ends with logo present
        if (currentEntry != null)
        {
            currentEntry.EndTime = keyframes.Max(x => x.Time);
            if (currentEntry.EndTime - currentEntry.StartTime >= minDuration)
            {
                entries.Add(currentEntry);
            }
        }

        return entries;
    }

    public void WriteEdlFile(string path, IEnumerable<EdlEntry> entries)
    {
        File.WriteAllLines(path, entries.Select(e => e.ToString()));
    }
}
