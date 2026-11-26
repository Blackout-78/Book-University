using System.Text.Json;

namespace BookUniversity;

internal sealed class BookmarkStore
{
    private readonly string _file = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BookUniversity", "bookmarks.json");
    private Dictionary<string, List<int>> _data = new();

    public BookmarkStore()
    {
        try
        {
            if (File.Exists(_file))
                _data = JsonSerializer.Deserialize<Dictionary<string, List<int>>>(File.ReadAllText(_file)) ?? new();
        }
        catch (Exception)
        {
            _data = new();
        }
    }

    public List<int> Get(string key) =>
        _data.TryGetValue(key, out var l) ? l.OrderBy(x => x).ToList() : new List<int>();

    public bool Has(string key, int page) => _data.TryGetValue(key, out var l) && l.Contains(page);

    // Returns false if the bookmark could not be saved to disk.
    public bool Toggle(string key, int page)
    {
        if (!_data.TryGetValue(key, out var l)) _data[key] = l = new List<int>();
        if (!l.Remove(page)) l.Add(page);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_data));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
