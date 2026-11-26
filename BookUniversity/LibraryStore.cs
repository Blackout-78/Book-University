using System.Text.Json;
using PDFtoImage;

namespace BookUniversity;

internal sealed class LibraryEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public int Pages { get; set; }
    public int LastPage { get; set; }
    public long Size { get; set; }
    public DateTime Added { get; set; }
    public DateTime LastOpened { get; set; } = DateTime.MinValue;
}

internal enum AddResult { Added, Duplicate, Failed }

// PDFs are copied into the app's own folder. Removing one deletes only that copy.
internal sealed class LibraryStore
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BookUniversity");
    private static readonly string Dir = Path.Combine(Root, "library");
    private static readonly string Index = Path.Combine(Root, "library.json");

    public List<LibraryEntry> Entries { get; private set; } = new();

    public LibraryStore()
    {
        try
        {
            if (File.Exists(Index))
                Entries = JsonSerializer.Deserialize<List<LibraryEntry>>(File.ReadAllText(Index)) ?? new();
        }
        catch (Exception)
        {
            Entries = new();
        }
    }

    public string PathOf(LibraryEntry e) => Path.Combine(Dir, e.Id + ".pdf");

    public async Task<AddResult> AddAsync(string title, Stream source)
    {
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(Dir, id + ".pdf");
        LibraryEntry? entry = null;
        try
        {
            Directory.CreateDirectory(Dir);
            await using (var f = File.Create(path)) await source.CopyToAsync(f);
            var size = new FileInfo(path).Length;
            if (Entries.Any(e => e.Title == title && e.Size == size))
            {
                File.Delete(path);
                return AddResult.Duplicate;
            }
            var pages = await Task.Run(() => { using var s = File.OpenRead(path); return Conversion.GetPageCount(s); });
            entry = new LibraryEntry { Id = id, Title = title, Pages = pages, Size = size, Added = DateTime.Now };
            Entries.Add(entry);
            Save();
            return AddResult.Added;
        }
        catch (Exception)
        {
            if (entry != null) Entries.Remove(entry);
            try { File.Delete(path); } catch (Exception) { }
            return AddResult.Failed;
        }
    }

    public void Remove(LibraryEntry e)
    {
        try { File.Delete(PathOf(e)); } catch (Exception) { }
        Entries.Remove(e);
        Save();
    }

    public void MarkOpened(LibraryEntry e, int page)
    {
        e.LastPage = page;
        e.LastOpened = DateTime.Now;
        Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Index, JsonSerializer.Serialize(Entries));
    }
}
