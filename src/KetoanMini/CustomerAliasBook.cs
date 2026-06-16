using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace KetoanMini;

public sealed class CustomerAliasBook
{
    public const string DefaultFileName = "ThongtinKH.xlsx";
    public const string DefaultTableName = "Thong_tin_thanh_toan";

    private readonly Dictionary<string, string> _officialByAlias;
    private readonly Dictionary<string, HashSet<string>> _aliasesByOfficial;
    private readonly List<CustomerAliasEntry> _entries;

    private CustomerAliasBook(Dictionary<string, string> officialByAlias, Dictionary<string, HashSet<string>> aliasesByOfficial, List<CustomerAliasEntry> entries, string sourcePath)
    {
        _officialByAlias = officialByAlias;
        _aliasesByOfficial = aliasesByOfficial;
        _entries = entries;
        SourcePath = sourcePath;
    }

    public static CustomerAliasBook Empty { get; } = new([], [], [], "");

    public string SourcePath { get; }
    public int AliasCount => _officialByAlias.Count;
    public int OfficialCount => _aliasesByOfficial.Count;
    public IReadOnlyCollection<string> OfficialNames => _aliasesByOfficial.Keys.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
    public IReadOnlyList<CustomerAliasEntry> Entries => _entries;

    public static CustomerAliasBook FromEntries(IEnumerable<string> officialNames, IEnumerable<CustomerAliasEntry> entries, string sourcePath = "")
    {
        var officialByAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var aliasesByOfficial = new Dictionary<string, HashSet<string>>(StringComparer.CurrentCultureIgnoreCase);
        var normalizedEntries = new List<CustomerAliasEntry>();

        foreach (var officialName in officialNames.Select(Clean).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.CurrentCultureIgnoreCase))
        {
            if (!aliasesByOfficial.ContainsKey(officialName))
            {
                aliasesByOfficial[officialName] = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            }

            officialByAlias[Normalize(officialName)] = officialName;
        }

        foreach (var entry in entries)
        {
            var officialName = Clean(entry.OfficialName);
            var alias = Clean(entry.Alias);
            if (string.IsNullOrWhiteSpace(officialName) || string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            if (!aliasesByOfficial.TryGetValue(officialName, out var aliases))
            {
                aliases = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
                aliasesByOfficial[officialName] = aliases;
                officialByAlias[Normalize(officialName)] = officialName;
            }

            aliases.Add(alias);
            officialByAlias[Normalize(alias)] = officialName;
            normalizedEntries.Add(new CustomerAliasEntry(officialName, alias));
        }

        return new CustomerAliasBook(officialByAlias, aliasesByOfficial, normalizedEntries, sourcePath);
    }

    public static CustomerAliasBook LoadFromKnownLocations(string dataPath)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var dataDirectory = Path.GetDirectoryName(dataPath) ?? Path.Combine(baseDirectory, "data");
        var candidates = new[]
        {
            Path.Combine(dataDirectory, DefaultFileName),
            Path.Combine(baseDirectory, "data", DefaultFileName),
            Path.Combine(baseDirectory, DefaultFileName),
            Path.Combine(baseDirectory, "template", DefaultFileName)
        };

        var path = candidates.FirstOrDefault(File.Exists);
        return path is null ? Empty : Load(path);
    }

    public static CustomerAliasBook Load(string path)
    {
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            var rows = ReadAliasRows(path);
            var officialNames = new List<string>();
            var entries = new List<CustomerAliasEntry>();

            foreach (var row in rows)
            {
                var officialName = Clean(row.FirstOrDefault() ?? "");
                if (string.IsNullOrWhiteSpace(officialName))
                {
                    continue;
                }

                officialNames.Add(officialName);

                foreach (var value in row.Skip(1).Where(value => !string.IsNullOrWhiteSpace(value)).Select(Clean))
                {
                    if (!string.Equals(value, officialName, StringComparison.CurrentCultureIgnoreCase))
                    {
                        entries.Add(new CustomerAliasEntry(officialName, value));
                    }
                }
            }

            return FromEntries(officialNames, entries, path);
        }
        catch
        {
            return Empty;
        }
    }

    public string Resolve(string input)
    {
        var text = Clean(input);
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        return _officialByAlias.TryGetValue(Normalize(text), out var officialName) ? officialName : text;
    }

    public IReadOnlyList<string> FindOfficialNames(string text, bool showAllWhenEmpty, int take = 20)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized) && !showAllWhenEmpty)
        {
            return [];
        }

        return _aliasesByOfficial
            .Select(item =>
            {
                var normalizedOfficial = Normalize(item.Key);
                var aliasMatch = item.Value
                    .Select(Normalize)
                    .Where(alias => string.IsNullOrWhiteSpace(normalized) || alias.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) || alias.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(alias => alias.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(alias => alias.Length)
                    .FirstOrDefault();
                return new
                {
                    Official = item.Key,
                    Matched = string.IsNullOrWhiteSpace(normalized) || normalizedOfficial.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) || normalizedOfficial.Contains(normalized, StringComparison.OrdinalIgnoreCase) || aliasMatch is not null,
                    Rank = normalizedOfficial.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) || aliasMatch?.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) == true ? 0 : 1,
                    AliasLength = aliasMatch?.Length ?? normalizedOfficial.Length
                };
            })
            .Where(item => item.Matched)
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.AliasLength)
            .ThenBy(item => item.Official, StringComparer.CurrentCultureIgnoreCase)
            .Take(take)
            .Select(item => item.Official)
            .ToList();
    }

    public IReadOnlyList<string> AliasesFor(string officialName)
    {
        return _aliasesByOfficial.TryGetValue(Resolve(officialName), out var aliases)
            ? aliases.OrderBy(alias => alias, StringComparer.CurrentCultureIgnoreCase).ToList()
            : [];
    }

    private static List<List<string>> ReadAliasRows(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var sharedStrings = ReadSharedStrings(archive);
        var tableRange = ReadTableRange(archive);
        if (tableRange is null)
        {
            return [];
        }

        var worksheetPath = ResolveWorksheetPath(archive);
        var values = ReadWorksheetCells(archive, worksheetPath, sharedStrings);
        var rows = new List<List<string>>();

        for (var row = tableRange.Value.StartRow + 1; row <= tableRange.Value.EndRow; row++)
        {
            var rowValues = new List<string>();
            for (var column = tableRange.Value.StartColumn; column <= tableRange.Value.EndColumn; column++)
            {
                rowValues.Add(values.TryGetValue((row, column), out var value) ? value : "");
            }

            if (rowValues.Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                rows.Add(rowValues);
            }
        }

        return rows;
    }

    private static CellRange? ReadTableRange(ZipArchive archive)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            var table = doc.Root;
            var name = table?.Attribute("name")?.Value ?? table?.Attribute("displayName")?.Value ?? "";
            if (!string.Equals(name, DefaultTableName, StringComparison.OrdinalIgnoreCase) && archive.Entries.Any(item => item.FullName.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }
            }

            var reference = table?.Attribute("ref")?.Value;
            if (!string.IsNullOrWhiteSpace(reference) && TryParseRange(reference, out var range))
            {
                return range;
            }

            var autoFilter = table?.Element(main + "autoFilter")?.Attribute("ref")?.Value;
            if (!string.IsNullOrWhiteSpace(autoFilter) && TryParseRange(autoFilter, out range))
            {
                return range;
            }
        }

        return null;
    }

    private static string ResolveWorksheetPath(ZipArchive archive)
    {
        XNamespace documentRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
        {
            return "xl/worksheets/sheet1.xml";
        }

        using var workbookStream = workbookEntry.Open();
        using var relsStream = relsEntry.Open();
        var workbook = XDocument.Load(workbookStream);
        var rels = XDocument.Load(relsStream);
        var firstSheet = workbook.Descendants(main + "sheet").FirstOrDefault();
        var relationshipId = firstSheet?.Attribute(documentRelationships + "id")?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            return "xl/worksheets/sheet1.xml";
        }

        var relationship = rels.Root?
            .Elements(packageRelationships + "Relationship")
            .FirstOrDefault(item => string.Equals(item.Attribute("Id")?.Value, relationshipId, StringComparison.OrdinalIgnoreCase));
        var target = relationship?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            return "xl/worksheets/sheet1.xml";
        }

        target = target.Replace('\\', '/');
        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? target : $"xl/{target.TrimStart('/')}";
    }

    private static Dictionary<(int Row, int Column), string> ReadWorksheetCells(ZipArchive archive, string worksheetPath, IReadOnlyList<string> sharedStrings)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var result = new Dictionary<(int Row, int Column), string>();
        var worksheetEntry = archive.GetEntry(worksheetPath) ?? archive.GetEntry("xl/worksheets/sheet1.xml");
        if (worksheetEntry is null)
        {
            return result;
        }

        using var stream = worksheetEntry.Open();
        var doc = XDocument.Load(stream);
        foreach (var cell in doc.Descendants(main + "c"))
        {
            var reference = cell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(reference) || !TryParseCellReference(reference, out var row, out var column))
            {
                continue;
            }

            var cellType = cell.Attribute("t")?.Value ?? "";
            string value;
            if (cellType == "s")
            {
                var sharedIndexText = cell.Element(main + "v")?.Value ?? "";
                value = int.TryParse(sharedIndexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count
                    ? sharedStrings[sharedIndex]
                    : "";
            }
            else if (cellType == "inlineStr")
            {
                value = string.Concat(cell.Descendants(main + "t").Select(text => text.Value));
            }
            else
            {
                value = cell.Element(main + "v")?.Value ?? "";
            }

            result[(row, column)] = Clean(value);
        }

        return result;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        return doc.Descendants(main + "si")
            .Select(item => Clean(string.Concat(item.Descendants(main + "t").Select(text => text.Value))))
            .ToList();
    }

    private static bool TryParseRange(string reference, out CellRange range)
    {
        var parts = reference.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && TryParseCellReference(parts[0], out var row, out var column))
        {
            range = new CellRange(row, column, row, column);
            return true;
        }

        if (parts.Length == 2 &&
            TryParseCellReference(parts[0], out var startRow, out var startColumn) &&
            TryParseCellReference(parts[1], out var endRow, out var endColumn))
        {
            range = new CellRange(startRow, startColumn, endRow, endColumn);
            return true;
        }

        range = default;
        return false;
    }

    private static bool TryParseCellReference(string reference, out int row, out int column)
    {
        row = 0;
        column = 0;
        var letters = new string(reference.TakeWhile(char.IsLetter).ToArray());
        var digits = new string(reference.SkipWhile(char.IsLetter).TakeWhile(char.IsDigit).ToArray());
        if (letters.Length == 0 || !int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out row))
        {
            return false;
        }

        foreach (var letter in letters.ToUpperInvariant())
        {
            column = (column * 26) + (letter - 'A' + 1);
        }

        return column > 0 && row > 0;
    }

    private static string Clean(string value)
    {
        return string.Join(" ", (value ?? "").Replace('\u00A0', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string Normalize(string value)
    {
        return Clean(TextUtil.RemoveDiacritics(value)).ToLowerInvariant();
    }

    private readonly record struct CellRange(int StartRow, int StartColumn, int EndRow, int EndColumn);
}

public sealed record CustomerAliasEntry(string OfficialName, string Alias);
