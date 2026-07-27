using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeutschLern.Domain;
using Microsoft.EntityFrameworkCore;

namespace DeutschLern.Infrastructure;

public static partial class A1BookVocabularySeeder
{
    private const string LessonPrefix = "A1 Wortschatz";

    public static async Task SeedAsync(
        LearningDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var level = await dbContext.LanguageLevels.SingleAsync(
            x => x.Code == "A1",
            cancellationToken);
        var source = ReadResource<SourceEntry>("a1-source.json");
        var translations = ReadResource<PersianEntry>("a1-fa.json")
            .ToDictionary(x => x.Id);

        ValidateResources(source, translations);

        var existingTitles = await dbContext.Lessons
            .Where(x => x.LanguageLevelId == level.Id && x.GermanTitle.StartsWith(LessonPrefix))
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
                            GermanText = persian.Example ?? entry.Example,
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
                PersianTitle = $"واژگان A1 - {group.Label}",
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

    private static IReadOnlyList<VocabularyGroup> CreateGroups(IReadOnlyList<SourceEntry> source)
    {
        var definitions = new[]
        {
            new GroupDefinition("A-B", "AB"),
            new GroupDefinition("C-E", "CDE"),
            new GroupDefinition("F-G", "FG"),
            new GroupDefinition("H-I", "HI"),
            new GroupDefinition("J-K", "JK"),
            new GroupDefinition("L-M", "LM"),
            new GroupDefinition("N-P", "NOP"),
            new GroupDefinition("Q-R", "QR"),
            new GroupDefinition("S", "S"),
            new GroupDefinition("T-U", "TU"),
            new GroupDefinition("V-W", "VW"),
            new GroupDefinition("Z", "Z")
        };

        var uniqueEntries = source
            .Where(entry => !entry.Raw.StartsWith('('))
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
        if (raw.StartsWith("s Essen", StringComparison.Ordinal))
        {
            return new WordMetadata("Essen (Nomen)", WordType.Noun, NounArticle.Das, null);
        }

        if (raw.StartsWith("s Fernsehen", StringComparison.Ordinal))
        {
            return new WordMetadata("Fernsehen (Nomen)", WordType.Noun, NounArticle.Das, null);
        }

        if (raw == "Sie")
        {
            return new WordMetadata("Sie (formell)", WordType.Other, null, null);
        }

        var value = raw.Trim();
        NounArticle? article = null;
        var isNoun = false;

        if (value.StartsWith("(s ", StringComparison.Ordinal))
        {
            article = NounArticle.Das;
            isNoun = true;
            value = value[3..].TrimEnd(')');
        }
        else if (value.StartsWith("r/e ", StringComparison.Ordinal))
        {
            isNoun = true;
            value = value[4..];
        }
        else if (value.StartsWith("r, e, s ", StringComparison.Ordinal))
        {
            value = value[8..];
        }
        else if (value.Length > 2 && value[1] == ' ')
        {
            article = value[0] switch
            {
                'r' => NounArticle.Der,
                'e' => NounArticle.Die,
                's' => NounArticle.Das,
                _ => null
            };
            if (article is not null)
            {
                isNoun = true;
                value = value[2..];
            }
        }

        if (value == "der, die, das")
        {
            return new WordMetadata(value, WordType.Other, null, null);
        }

        if (value == "sein, -e")
        {
            return new WordMetadata("sein (Possessivartikel)", WordType.Other, null, null);
        }

        var parts = GrammarSeparator().Split(value, 2);
        var word = SingularMarker().Replace(parts[0], "").Trim().TrimEnd('!');
        var plural = isNoun && parts.Length > 1 ? parts[1].Trim() : null;
        if (string.IsNullOrWhiteSpace(plural) || plural is "(Sg.)" or "(Pl.)")
        {
            plural = null;
        }

        var type = isNoun
            ? WordType.Noun
            : Adjectives.Contains(word)
                ? WordType.Adjective
                : IsVerb(word)
                    ? WordType.Verb
                    : Adverbs.Contains(word)
                        ? WordType.Adverb
                        : WordType.Other;
        return new WordMetadata(word, type, article, plural);
    }

    private static bool IsVerb(string word) =>
        word.EndsWith("en", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith("eln", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith("ern", StringComparison.OrdinalIgnoreCase) ||
        word is "weh tun" or "Tennis spielen" or "spazieren gehen";

    private static List<T> ReadResource<T>(string fileName)
    {
        var assembly = typeof(A1BookVocabularySeeder).Assembly;
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
        if (source.Count != 532 || translations.Count != 532)
        {
            throw new InvalidOperationException("The A1 vocabulary resources must contain exactly 532 entries.");
        }

        foreach (var entry in source)
        {
            if (!translations.TryGetValue(entry.Id, out var translation) ||
                string.IsNullOrWhiteSpace(translation.Meaning) ||
                string.IsNullOrWhiteSpace(translation.Translation) ||
                string.IsNullOrWhiteSpace(translation.Example ?? entry.Example))
            {
                throw new InvalidOperationException($"A1 vocabulary entry {entry.Id} is incomplete.");
            }
        }
    }

    private static readonly HashSet<string> Adjectives =
    [
        "alt", "arbeitslos", "besetzt", "besser", "beste", "billig", "blöd",
        "dick", "dumm", "einfach", "frei", "freundlich", "froh", "geboren",
        "gemütlich", "glücklich", "groß", "gut", "hübsch", "interessant",
        "jung", "kalt", "kaputt", "klein", "kurz", "lang", "langsam",
        "langweilig", "leicht", "lieb", "lustig", "möglich", "müde", "nett",
        "neu", "normal", "offen", "pünktlich", "richtig", "ruhig", "schade",
        "schlecht", "schnell", "schön", "schwer", "sicher", "spät", "süß",
        "sympathisch", "teuer", "toll", "traurig", "verrückt", "wahr", "warm",
        "weit", "wichtig", "willkommen", "wunderbar"
    ];

    private static readonly HashSet<string> Adverbs =
    [
        "allein", "also", "bald", "besonders", "dort", "endlich", "früh",
        "gerade", "geradeaus", "gern", "gestern", "gleich", "heute", "hier",
        "hoffentlich", "immer", "jetzt", "lange", "leider", "links", "manchmal",
        "mehr", "mindestens", "morgen", "nie", "nur", "oft", "rechts", "schon",
        "sehr", "so", "sofort", "später", "überall", "vielleicht", "weiter",
        "wieder", "wirklich", "zuerst", "zusammen"
    ];

    [GeneratedRegex(@"\s*[,;]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex GrammarSeparator();

    [GeneratedRegex(@"\s*\((?:Sg\.?|Pl\.?)\)\s*", RegexOptions.CultureInvariant)]
    private static partial Regex SingularMarker();

    private sealed record SourceEntry(int Id, int Page, string Raw, string Example);
    private sealed record PersianEntry(int Id, string Meaning, string Translation, string? Example);
    private sealed record WordMetadata(string Word, WordType Type, NounArticle? Article, string? Plural);
    private sealed record GroupDefinition(string Label, string Letters);
    private sealed record VocabularyGroup(string Label, IReadOnlyList<SourceEntry> Entries);
}
