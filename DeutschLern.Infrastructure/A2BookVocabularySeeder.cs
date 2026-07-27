using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeutschLern.Domain;
using Microsoft.EntityFrameworkCore;

namespace DeutschLern.Infrastructure;

public static partial class A2BookVocabularySeeder
{
    private const string LessonPrefix = "A2 Wortschatz";

    public static async Task SeedAsync(
        LearningDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var level = await dbContext.LanguageLevels.SingleAsync(
            x => x.Code == "A2",
            cancellationToken);
        var source = ReadResource<SourceEntry>("a2-source.json");
        var translations = ReadResource<PersianEntry>("a2-fa.json")
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

            var words = group.Entries.Select(entry =>
            {
                var metadata = ParseMetadata(entry.Raw);
                var persian = translations[entry.Id];
                var vocabulary = new Vocabulary
                {
                    GermanWord = metadata.Word,
                    PersianMeaning = persian.Meaning,
                    WordType = metadata.Type,
                    Article = metadata.Article,
                    PluralForm = metadata.Plural,
                    Examples =
                    [
                        new ExampleSentence
                        {
                            GermanText = entry.Example,
                            PersianTranslation = persian.Translation
                        }
                    ]
                };
                vocabulary.ValidateForPublishing();
                return vocabulary;
            }).ToList();

            dbContext.Lessons.Add(new Lesson
            {
                LanguageLevelId = level.Id,
                GermanTitle = title,
                PersianTitle = $"واژگان A2 - {group.Label}",
                Order = ++nextOrder,
                Vocabularies = words,
                Quiz = CreateQuiz(words)
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

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
            new GroupDefinition("G-H", "GH"),
            new GroupDefinition("I-J", "IJ"),
            new GroupDefinition("K-L", "KL"),
            new GroupDefinition("M-N", "MN"),
            new GroupDefinition("O-P", "OP"),
            new GroupDefinition("Q-R", "QR"),
            new GroupDefinition("S", "S"),
            new GroupDefinition("T-U", "TU"),
            new GroupDefinition("V-W", "VW"),
            new GroupDefinition("X-Z", "XYZ")
        };

        var uniqueEntries = source
            .GroupBy(x => x.Raw, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToList();

        return definitions.Select(definition => new VocabularyGroup(
            definition.Label,
            uniqueEntries.Where(entry =>
            {
                var word = ParseMetadata(entry.Raw).Word;
                var first = word.TrimStart('(', '-').FirstOrDefault();
                var normalizedFirst = char.ToUpperInvariant(first) switch
                {
                    'Ä' => 'A',
                    'Ö' => 'O',
                    'Ü' => 'U',
                    var value => value
                };
                return definition.Letters.Contains(normalizedFirst);
            }).ToList())).ToList();
    }

    private static WordMetadata ParseMetadata(string raw)
    {
        var value = raw.Replace("de Rentner", "der Rentner", StringComparison.Ordinal)
            .Trim();
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
        var word = SingularMarker().Replace(parts[0], "").Trim().TrimEnd('/');
        word = BrokenWordSpacing().Replace(word, "$1$2");
        var plural = isNoun && parts.Length > 1
            ? parts[1].Trim().TrimEnd('/')
            : null;
        if (string.IsNullOrWhiteSpace(plural) ||
            plural is "-" or "–" or "(Sg.)" or "(Sing.)" or "(Pl.)")
        {
            plural = null;
        }

        word = Disambiguate(raw, word);
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

    private static string Disambiguate(string raw, string word) => raw switch
    {
        "der Arm, -e" => "Arm (Nomen)",
        "die Bank, -en" => "Bank (Finanzinstitut)",
        "die Bank, ¨-e" => "Bank (Sitzmöbel)",
        "die Bitte, -n" => "Bitte (Nomen)",
        "das Essen, -" => "Essen (Nomen)",
        "das Leben, -" => "Leben (Nomen)",
        "der See, -n" => "See (der)",
        "die See (Sg)" => "See (die)",
        _ => word
    };

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
        word.EndsWith("sam", StringComparison.OrdinalIgnoreCase);

    private static List<T> ReadResource<T>(string fileName)
    {
        var assembly = typeof(A2BookVocabularySeeder).Assembly;
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
        if (source.Count != 1_192 || translations.Count != 1_192)
        {
            throw new InvalidOperationException(
                "The A2 vocabulary resources must contain exactly 1,192 entries.");
        }

        foreach (var entry in source)
        {
            if (!translations.TryGetValue(entry.Id, out var translation) ||
                string.IsNullOrWhiteSpace(translation.Meaning) ||
                string.IsNullOrWhiteSpace(translation.Translation) ||
                string.IsNullOrWhiteSpace(entry.Example))
            {
                throw new InvalidOperationException(
                    $"A2 vocabulary entry {entry.Id} is incomplete.");
            }
        }
    }

    private static readonly NounPrefix[] NounPrefixes =
    [
        new("der/die ", null),
        new("der/das ", null),
        new("die/der ", null),
        new("der ", NounArticle.Der),
        new("die ", NounArticle.Die),
        new("das ", NounArticle.Das)
    ];

    private static readonly HashSet<string> Adjectives =
    [
        "aktiv", "aktuell", "alt", "arm", "bekannt", "bequem", "besetzt",
        "besser", "billig", "böse", "breit", "dick", "dunkel", "dünn",
        "echt", "egal", "eigen-", "einfach", "eng", "fertig", "fleißig",
        "frei", "fremd", "freundlich", "froh", "gefährlich", "gesund",
        "gleich", "glücklich", "groß", "gut", "heiß", "hell", "hoch",
        "hübsch", "hungrig", "interessant", "jung", "kalt", "kaputt",
        "klar", "klein", "komisch", "krank", "kurz", "lang", "langsam",
        "langweilig", "laut", "leer", "leicht", "leise", "lieb", "lustig",
        "möglich", "müde", "nah", "nass", "nett", "neu", "normal", "offen",
        "prima", "pünktlich", "reich", "richtig", "ruhig", "sauber",
        "schade", "schlecht", "schlimm", "schnell", "schön", "schwer",
        "sicher", "spät", "stark", "süß", "sympathisch", "teuer", "toll",
        "traurig", "trocken", "verboten", "verrückt", "vorsichtig", "wach",
        "wahr", "warm", "weit", "wichtig", "willkommen", "windig", "witzig",
        "wunderbar", "zufrieden"
    ];

    private static readonly HashSet<string> Adverbs =
    [
        "allein", "also", "anders", "bald", "besonders", "dort", "draußen",
        "drinnen", "eigentlich", "endlich", "fast", "früher", "gern",
        "gestern", "gleich", "heute", "hier", "hoffentlich", "immer", "jetzt",
        "leider", "links", "manchmal", "mehr", "meistens", "mindestens",
        "morgen", "nie", "nirgends", "noch", "nur", "oft", "rechts", "schon",
        "sehr", "so", "sofort", "später", "überall", "vielleicht", "vorbei",
        "vorgestern", "vorher", "vorwärts", "wahrscheinlich", "weiter",
        "wieder", "wirklich", "zuerst", "zuletzt", "zurück", "zusammen"
    ];

    [GeneratedRegex(@"\s*[,;]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex GrammarSeparator();

    [GeneratedRegex(@"\s*\((?:Sg\.?|Sing\.?|Pl\.?)\)\s*", RegexOptions.CultureInvariant)]
    private static partial Regex SingularMarker();

    [GeneratedRegex(@"(\p{L})-\s+(\p{Ll})", RegexOptions.CultureInvariant)]
    private static partial Regex BrokenWordSpacing();

    private sealed record SourceEntry(int Id, int Page, string Raw, string Example);
    private sealed record PersianEntry(int Id, string Meaning, string Translation);
    private sealed record WordMetadata(
        string Word,
        WordType Type,
        NounArticle? Article,
        string? Plural);
    private sealed record NounPrefix(string Text, NounArticle? Article);
    private sealed record GroupDefinition(string Label, string Letters);
    private sealed record VocabularyGroup(
        string Label,
        IReadOnlyList<SourceEntry> Entries);
}
