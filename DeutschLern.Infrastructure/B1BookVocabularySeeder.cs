using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeutschLern.Domain;
using Microsoft.EntityFrameworkCore;

namespace DeutschLern.Infrastructure;

public static partial class B1BookVocabularySeeder
{
    private const string LessonPrefix = "B1 Wortschatz";

    public static async Task SeedAsync(
        LearningDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var level = await dbContext.LanguageLevels.SingleAsync(
            x => x.Code == "B1",
            cancellationToken);
        var source = ReadResource<SourceEntry>("b1-source.json");
        var translations = ReadResource<PersianEntry>("b1-fa.json")
            .ToDictionary(x => x.Id);

        ValidateResources(source, translations);

        var existingTitles = await dbContext.Lessons
            .Where(x => x.LanguageLevelId == level.Id &&
                        x.GermanTitle.StartsWith(LessonPrefix))
            .Select(x => x.GermanTitle)
            .ToHashSetAsync(cancellationToken);
        var nextOrder = await dbContext.Lessons
            .Where(x => x.LanguageLevelId == level.Id)
            .MaxAsync(x => (int?)x.Order, cancellationToken) ?? 0;

        foreach (var group in CreateGroups(source))
        {
            var title = $"{LessonPrefix} {group.Label}";
            if (existingTitles.Contains(title))
            {
                continue;
            }

            var words = CreateWords(group.Entries, translations);
            dbContext.Lessons.Add(new Lesson
            {
                LanguageLevelId = level.Id,
                GermanTitle = title,
                PersianTitle = $"واژگان B1 - {group.Label}",
                Order = ++nextOrder,
                Vocabularies = words,
                Quiz = CreateQuiz(words)
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static List<Vocabulary> CreateWords(
        IReadOnlyList<SourceEntry> entries,
        IReadOnlyDictionary<int, PersianEntry> translations)
    {
        var parsed = entries
            .Select(entry => new ParsedEntry(entry, ParseMetadata(entry.Raw)))
            .ToList();
        var duplicateCounts = parsed
            .GroupBy(x => x.Metadata.Word, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        var usedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var words = new List<Vocabulary>(parsed.Count);

        foreach (var item in parsed)
        {
            var word = item.Metadata.Word;
            if (duplicateCounts[word] > 1)
            {
                word = QualifyWord(item, parsed);
            }

            if (!usedWords.Add(word))
            {
                word = $"{word} (Variante {item.Entry.Id})";
                usedWords.Add(word);
            }

            var persian = translations[item.Entry.Id];
            var vocabulary = new Vocabulary
            {
                GermanWord = word,
                PersianMeaning = persian.Meaning,
                WordType = item.Metadata.Type,
                Article = item.Metadata.Article,
                PluralForm = item.Metadata.Plural,
                Examples =
                [
                    new ExampleSentence
                    {
                        GermanText = item.Entry.Example,
                        PersianTranslation = persian.Translation
                    }
                ]
            };
            vocabulary.ValidateForPublishing();
            words.Add(vocabulary);
        }

        return words;
    }

    private static string QualifyWord(
        ParsedEntry item,
        IReadOnlyList<ParsedEntry> group)
    {
        var matches = group
            .Where(x => string.Equals(
                x.Metadata.Word,
                item.Metadata.Word,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Select(x => x.Metadata.Type).Distinct().Count() > 1)
        {
            return $"{item.Metadata.Word} ({TypeLabel(item.Metadata.Type)})";
        }

        if (item.Metadata.Article is not null &&
            matches.Select(x => x.Metadata.Article).Distinct().Count() > 1)
        {
            return $"{item.Metadata.Word} ({item.Metadata.Article.ToString()!.ToLowerInvariant()})";
        }

        if (!string.IsNullOrWhiteSpace(item.Metadata.Plural) &&
            matches.Select(x => x.Metadata.Plural).Distinct().Count() > 1)
        {
            return $"{item.Metadata.Word} (Plural {item.Metadata.Plural})";
        }

        var region = RegionMarker().Match(item.Entry.Raw);
        return region.Success
            ? $"{item.Metadata.Word} ({region.Value.Trim('(', ')')})"
            : $"{item.Metadata.Word} (Variante)";
    }

    private static string TypeLabel(WordType type) => type switch
    {
        WordType.Noun => "Nomen",
        WordType.Verb => "Verb",
        WordType.Adjective => "Adjektiv",
        WordType.Adverb => "Adverb",
        _ => "Ausdruck"
    };

    private static Quiz CreateQuiz(IReadOnlyList<Vocabulary> words)
    {
        var selected = words
            .Where((_, index) => index % Math.Max(1, words.Count / 10) == 0)
            .Take(10)
            .ToList();

        return new Quiz
        {
            Questions = selected.Select((word, index) =>
            {
                var distractors = words
                    .Where(x => x.GermanWord != word.GermanWord)
                    .Select(x => x.PersianMeaning)
                    .Distinct()
                    .Skip(index)
                    .Concat(words.Select(x => x.PersianMeaning).Distinct())
                    .Where(x => x != word.PersianMeaning)
                    .Take(3)
                    .ToList();
                var options = distractors
                    .Append(word.PersianMeaning)
                    .OrderBy(x => StableOptionOrder(x, index))
                    .ToList();

                return new QuizQuestion
                {
                    Order = index + 1,
                    Text = $"واژه «{word.GermanWord}» چه معنایی دارد؟",
                    Options = options.Select(option => new QuizOption
                    {
                        Text = option,
                        IsCorrect = option == word.PersianMeaning
                    }).ToList()
                };
            }).ToList()
        };
    }

    private static int StableOptionOrder(string value, int questionIndex)
    {
        var hash = new HashCode();
        hash.Add(value, StringComparer.Ordinal);
        hash.Add(questionIndex);
        return hash.ToHashCode();
    }

    private static IReadOnlyList<VocabularyGroup> CreateGroups(
        IReadOnlyList<SourceEntry> source)
    {
        var definitions = new[]
        {
            new GroupDefinition("A", "A"),
            new GroupDefinition("B", "B"),
            new GroupDefinition("C-D", "CD"),
            new GroupDefinition("E-F", "EF"),
            new GroupDefinition("G", "G"),
            new GroupDefinition("H", "H"),
            new GroupDefinition("I-J", "IJ"),
            new GroupDefinition("K", "K"),
            new GroupDefinition("L", "L"),
            new GroupDefinition("M", "M"),
            new GroupDefinition("N", "N"),
            new GroupDefinition("O-P", "OP"),
            new GroupDefinition("Q-R", "QR"),
            new GroupDefinition("S", "S"),
            new GroupDefinition("T-U", "TU"),
            new GroupDefinition("V", "V"),
            new GroupDefinition("W", "W"),
            new GroupDefinition("X-Z", "XYZ")
        };

        var uniqueEntries = source
            .GroupBy(x => x.Raw, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToList();
        var groups = definitions.Select(definition => new VocabularyGroup(
            definition.Label,
            uniqueEntries.Where(entry =>
            {
                var first = FirstLetter(ParseMetadata(entry.Raw).Word);
                return definition.Letters.Contains(first);
            }).ToList())).ToList();
        var assignedIds = groups.SelectMany(x => x.Entries)
            .Select(x => x.Id)
            .ToHashSet();
        groups[^1].Entries.AddRange(uniqueEntries.Where(x => !assignedIds.Contains(x.Id)));
        return groups;
    }

    private static char FirstLetter(string word)
    {
        var first = word.FirstOrDefault(char.IsLetter);
        return char.ToUpperInvariant(first) switch
        {
            'Ä' => 'A',
            'Ö' => 'O',
            'Ü' => 'U',
            var value => value
        };
    }

    private static WordMetadata ParseMetadata(string raw)
    {
        var value = RegionPrefix().Replace(raw.Trim(), "");
        NounArticle? article = null;
        var isNoun = false;

        foreach (var prefix in NounPrefixes)
        {
            if (!value.StartsWith(prefix.Text, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            isNoun = true;
            article = prefix.Article;
            value = value[prefix.Text.Length..];
            break;
        }

        var parts = GrammarSeparator().Split(value, 2);
        var word = SingularMarker().Replace(parts[0], "").Trim().TrimEnd('/', '!');
        word = word.Replace("(sich)", "sich", StringComparison.OrdinalIgnoreCase);
        word = BrokenWordSpacing().Replace(word, "$1$2");
        var plural = isNoun && parts.Length > 1
            ? parts[1].Trim().TrimEnd('/')
            : null;
        if (string.IsNullOrWhiteSpace(plural) ||
            plural is "-" or "–" or "(Sg.)" or "(Sing.)" or "(Pl.)")
        {
            plural = null;
        }

        var type = isNoun || char.IsUpper(word.FirstOrDefault())
            ? WordType.Noun
            : IsVerb(word, raw)
                ? WordType.Verb
                : IsAdjective(word)
                    ? WordType.Adjective
                    : Adverbs.Contains(word)
                        ? WordType.Adverb
                        : WordType.Other;
        return new WordMetadata(word, type, article, plural);
    }

    private static bool IsVerb(string word, string raw) =>
        word.EndsWith("en", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith("eln", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith("ern", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith(" sein", StringComparison.OrdinalIgnoreCase) ||
        raw.Contains(" hat ", StringComparison.OrdinalIgnoreCase) ||
        raw.Contains(" ist ", StringComparison.OrdinalIgnoreCase);

    private static bool IsAdjective(string word) =>
        Adjectives.Contains(word) ||
        word.EndsWith("ig", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith("lich", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith("isch", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith("bar", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith("los", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith("sam", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith("voll", StringComparison.OrdinalIgnoreCase);

    private static List<T> ReadResource<T>(string fileName)
    {
        var assembly = typeof(B1BookVocabularySeeder).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(x => x.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Resource {fileName} was not found.");
        return JsonSerializer.Deserialize<List<T>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Resource {fileName} is invalid.");
    }

    private static void ValidateResources(
        IReadOnlyList<SourceEntry> source,
        IReadOnlyDictionary<int, PersianEntry> translations)
    {
        if (source.Count != 3_028 || translations.Count != 3_028)
        {
            throw new InvalidOperationException(
                "The B1 vocabulary resources must contain exactly 3,028 entries.");
        }

        foreach (var entry in source)
        {
            if (!translations.TryGetValue(entry.Id, out var translation) ||
                string.IsNullOrWhiteSpace(translation.Meaning) ||
                string.IsNullOrWhiteSpace(translation.Translation) ||
                string.IsNullOrWhiteSpace(entry.Example))
            {
                throw new InvalidOperationException(
                    $"B1 vocabulary entry {entry.Id} is incomplete.");
            }
        }
    }

    private static readonly NounPrefix[] NounPrefixes =
    [
        new("der/die ", null),
        new("der/das ", null),
        new("das/der ", null),
        new("die/der ", null),
        new("der ", NounArticle.Der),
        new("die ", NounArticle.Die),
        new("das ", NounArticle.Das)
    ];

    private static readonly HashSet<string> Adjectives =
    [
        "aktiv", "aktuell", "ähnlich", "allgemein", "alt", "alternativ",
        "arm", "bekannt", "bequem", "bereit", "besetzt", "besser", "billig",
        "böse", "breit", "dick", "dunkel", "dünn", "echt", "egal", "eilig",
        "einfach", "eng", "entschlossen", "fertig", "fleißig", "frei",
        "fremd", "freundlich", "froh", "gefährlich", "gesund", "glücklich",
        "groß", "gut", "heiß", "hell", "hoch", "hübsch", "hungrig",
        "interessant", "jung", "kalt", "kaputt", "klar", "klein", "komisch",
        "krank", "kurz", "lang", "langsam", "langweilig", "laut", "leer",
        "leicht", "leise", "lieb", "lustig", "möglich", "müde", "nah",
        "nass", "nett", "neu", "normal", "offen", "pünktlich", "reich",
        "richtig", "ruhig", "sauber", "sauer", "schade", "schädlich",
        "schlecht", "schlimm", "schnell", "schön", "schwer", "sicher",
        "spät", "stark", "süß", "sympathisch", "teuer", "toll", "traurig",
        "trocken", "typisch", "unentschieden", "verantwortlich", "verboten",
        "verrückt", "vorsichtig", "wach", "wahr", "warm", "weit", "wichtig",
        "willkommen", "windig", "witzig", "wütend", "wunderbar",
        "zuverlässig", "zufrieden", "zuständig"
    ];

    private static readonly HashSet<string> Adverbs =
    [
        "allerdings", "allein", "also", "anders", "bald", "besonders",
        "bisher", "dort", "draußen", "drinnen", "eigentlich", "endlich",
        "fast", "früher", "gern", "gestern", "gleich", "heute", "hier",
        "hoffentlich", "immer", "jetzt", "leider", "links", "manchmal",
        "mehr", "meistens", "mindestens", "morgen", "nie", "nirgends",
        "noch", "nur", "oft", "rechts", "schon", "sehr", "so", "sofort",
        "später", "überall", "vielleicht", "vorbei", "vorgestern", "vorher",
        "vorwärts", "wahrscheinlich", "weiter", "wieder", "wirklich",
        "zuerst", "zuletzt", "zurück", "zusammen"
    ];

    [GeneratedRegex(@"\s*[,;]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex GrammarSeparator();

    [GeneratedRegex(@"\s*\((?:Sg\.?|Sing\.?|Pl\.?|nur Pl\.?)\)\s*", RegexOptions.CultureInvariant)]
    private static partial Regex SingularMarker();

    [GeneratedRegex(@"(\p{L})-\s+(\p{Ll})", RegexOptions.CultureInvariant)]
    private static partial Regex BrokenWordSpacing();

    [GeneratedRegex(@"^(?:\([A-Z, ]+\)\s*)?(?:D|A|CH)(?:\s*,\s*(?:D|A|CH))*:\s*", RegexOptions.CultureInvariant)]
    private static partial Regex RegionPrefix();

    [GeneratedRegex(@"\((?:D|A|CH)(?:,\s*(?:D|A|CH))*\)", RegexOptions.CultureInvariant)]
    private static partial Regex RegionMarker();

    private sealed record SourceEntry(int Id, int Page, string Raw, string Example);
    private sealed record PersianEntry(int Id, string Meaning, string Translation);
    private sealed record WordMetadata(
        string Word,
        WordType Type,
        NounArticle? Article,
        string? Plural);
    private sealed record ParsedEntry(SourceEntry Entry, WordMetadata Metadata);
    private sealed record NounPrefix(string Text, NounArticle? Article);
    private sealed record GroupDefinition(string Label, string Letters);
    private sealed record VocabularyGroup(
        string Label,
        List<SourceEntry> Entries);
}
