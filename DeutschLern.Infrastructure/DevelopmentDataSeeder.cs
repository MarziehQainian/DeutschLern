using DeutschLern.Domain;
using Microsoft.EntityFrameworkCore;

namespace DeutschLern.Infrastructure;

public static class DevelopmentDataSeeder
{
    public static async Task SeedAsync(
        LearningDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var levels = await dbContext.LanguageLevels
            .ToDictionaryAsync(x => x.Code, cancellationToken);

        foreach (var seed in Lessons)
        {
            var level = levels[seed.LevelCode];
            var exists = await dbContext.Lessons.AnyAsync(
                x => x.LanguageLevelId == level.Id && x.Order == seed.Order,
                cancellationToken);
            if (exists)
            {
                continue;
            }

            dbContext.Lessons.Add(CreateLesson(level.Id, seed));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await A1BookVocabularySeeder.SeedAsync(dbContext, cancellationToken);
        await A2BookVocabularySeeder.SeedAsync(dbContext, cancellationToken);
        await B1BookVocabularySeeder.SeedAsync(dbContext, cancellationToken);
    }

    private static Lesson CreateLesson(int levelId, LessonSeed seed)
    {
        var lesson = new Lesson
        {
            LanguageLevelId = levelId,
            GermanTitle = seed.GermanTitle,
            PersianTitle = seed.PersianTitle,
            Order = seed.Order
        };

        lesson.Vocabularies = seed.Words.Select(word => new Vocabulary
        {
            GermanWord = word.German,
            PersianMeaning = word.Persian,
            WordType = word.Type,
            Article = word.Article,
            PluralForm = word.Plural,
            Examples =
            [
                new ExampleSentence
                {
                    GermanText = word.GermanExample,
                    PersianTranslation = word.PersianExample
                }
            ]
        }).ToList();

        lesson.Quiz = new Quiz
        {
            Questions = seed.Words.Select((word, index) =>
            {
                var meanings = seed.Words
                    .Skip(index)
                    .Concat(seed.Words.Take(index))
                    .Select(x => x.Persian)
                    .Append("هیچ‌کدام")
                    .ToList();
                return new QuizQuestion
                {
                    Text = $"واژه «{word.German}» چه معنایی دارد؟",
                    Order = index + 1,
                    Options = meanings.Select(meaning => new QuizOption
                    {
                        Text = meaning,
                        IsCorrect = meaning == word.Persian
                    }).ToList()
                };
            }).ToList()
        };

        return lesson;
    }

    private static readonly LessonSeed[] Lessons =
    [
        new("A1", 1, "Begrüßung", "سلام و احوال‌پرسی",
        [
            Word("Hallo", "سلام", WordType.Other, "Hallo, wie geht es dir?", "سلام، حالت چطور است؟"),
            Word("Danke", "ممنون", WordType.Other, "Danke für deine Hilfe.", "برای کمکت ممنونم."),
            Word("Bitte", "خواهش می‌کنم", WordType.Other, "Ein Wasser, bitte.", "لطفاً یک آب.")
        ]),
        new("A1", 2, "Familie", "خانواده",
        [
            Word("Mutter", "مادر", WordType.Noun, "Meine Mutter heißt Sara.", "نام مادر من سارا است.", NounArticle.Die, "Mütter"),
            Word("Vater", "پدر", WordType.Noun, "Mein Vater arbeitet heute.", "پدرم امروز کار می‌کند.", NounArticle.Der, "Väter"),
            Word("Kind", "کودک", WordType.Noun, "Das Kind spielt im Garten.", "کودک در باغ بازی می‌کند.", NounArticle.Das, "Kinder")
        ]),
        new("A2", 1, "Reisen", "سفر",
        [
            Word("Bahnhof", "ایستگاه قطار", WordType.Noun, "Der Bahnhof ist in der Innenstadt.", "ایستگاه قطار در مرکز شهر است.", NounArticle.Der, "Bahnhöfe"),
            Word("Fahrkarte", "بلیط", WordType.Noun, "Ich kaufe eine Fahrkarte nach Berlin.", "من یک بلیط به برلین می‌خرم.", NounArticle.Die, "Fahrkarten"),
            Word("reisen", "سفر کردن", WordType.Verb, "Wir reisen im Sommer nach Wien.", "ما تابستان به وین سفر می‌کنیم.")
        ]),
        new("A2", 2, "Einkaufen", "خرید",
        [
            Word("Preis", "قیمت", WordType.Noun, "Der Preis ist zu hoch.", "قیمت خیلی بالا است.", NounArticle.Der, "Preise"),
            Word("kaufen", "خریدن", WordType.Verb, "Sie kauft frisches Brot.", "او نان تازه می‌خرد."),
            Word("günstig", "مقرون‌به‌صرفه", WordType.Adjective, "Diese Jacke ist günstig.", "این کت مقرون‌به‌صرفه است.")
        ]),
        new("B1", 1, "Arbeitswelt", "دنیای کار",
        [
            Word("Bewerbung", "درخواست استخدام", WordType.Noun, "Ich schicke meine Bewerbung per E-Mail.", "درخواست استخدامم را با ایمیل می‌فرستم.", NounArticle.Die, "Bewerbungen"),
            Word("Erfahrung", "تجربه", WordType.Noun, "Sie hat viel berufliche Erfahrung.", "او تجربه کاری زیادی دارد.", NounArticle.Die, "Erfahrungen"),
            Word("kündigen", "استعفا دادن", WordType.Verb, "Er möchte seinen Vertrag kündigen.", "او می‌خواهد قراردادش را فسخ کند.")
        ]),
        new("B1", 2, "Gesundheit", "سلامتی",
        [
            Word("Gesundheit", "سلامتی", WordType.Noun, "Gesundheit ist wichtiger als Geld.", "سلامتی مهم‌تر از پول است.", NounArticle.Die),
            Word("untersuchen", "معاینه کردن", WordType.Verb, "Die Ärztin untersucht den Patienten.", "پزشک بیمار را معاینه می‌کند."),
            Word("gesund", "سالم", WordType.Adjective, "Obst und Gemüse sind gesund.", "میوه و سبزیجات سالم هستند.")
        ]),
        new("B2", 1, "Umwelt", "محیط زیست",
        [
            Word("Umwelt", "محیط زیست", WordType.Noun, "Wir müssen die Umwelt schützen.", "ما باید از محیط زیست محافظت کنیم.", NounArticle.Die),
            Word("nachhaltig", "پایدار", WordType.Adjective, "Das Unternehmen produziert nachhaltig.", "شرکت به‌صورت پایدار تولید می‌کند."),
            Word("vermeiden", "اجتناب کردن", WordType.Verb, "Wir sollten Plastik vermeiden.", "ما باید از پلاستیک اجتناب کنیم.")
        ]),
        new("B2", 2, "Medien", "رسانه‌ها",
        [
            Word("Nachricht", "خبر", WordType.Noun, "Diese Nachricht verbreitet sich schnell.", "این خبر سریع پخش می‌شود.", NounArticle.Die, "Nachrichten"),
            Word("berichten", "گزارش دادن", WordType.Verb, "Die Zeitung berichtet über die Wahl.", "روزنامه درباره انتخابات گزارش می‌دهد."),
            Word("zuverlässig", "قابل اعتماد", WordType.Adjective, "Diese Quelle ist zuverlässig.", "این منبع قابل اعتماد است.")
        ]),
        new("C1", 1, "Gesellschaft", "جامعه",
        [
            Word("Gesellschaft", "جامعه", WordType.Noun, "Die Gesellschaft verändert sich ständig.", "جامعه دائماً در حال تغییر است.", NounArticle.Die, "Gesellschaften"),
            Word("Gerechtigkeit", "عدالت", WordType.Noun, "Soziale Gerechtigkeit bleibt ein wichtiges Ziel.", "عدالت اجتماعی همچنان هدف مهمی است.", NounArticle.Die),
            Word("fördern", "تقویت کردن", WordType.Verb, "Bildung fördert die persönliche Entwicklung.", "آموزش رشد فردی را تقویت می‌کند.")
        ]),
        new("C1", 2, "Wissenschaft", "علم و پژوهش",
        [
            Word("Forschung", "پژوهش", WordType.Noun, "Die Forschung liefert neue Erkenntnisse.", "پژوهش یافته‌های تازه‌ای ارائه می‌دهد.", NounArticle.Die),
            Word("Erkenntnis", "یافته", WordType.Noun, "Diese Erkenntnis verändert unsere Sichtweise.", "این یافته دیدگاه ما را تغییر می‌دهد.", NounArticle.Die, "Erkenntnisse"),
            Word("belegen", "اثبات کردن", WordType.Verb, "Mehrere Studien belegen diese These.", "چندین مطالعه این فرضیه را اثبات می‌کنند.")
        ])
    ];

    private static WordSeed Word(
        string german,
        string persian,
        WordType type,
        string germanExample,
        string persianExample,
        NounArticle? article = null,
        string? plural = null) =>
        new(german, persian, type, germanExample, persianExample, article, plural);

    private sealed record LessonSeed(
        string LevelCode,
        int Order,
        string GermanTitle,
        string PersianTitle,
        IReadOnlyList<WordSeed> Words);

    private sealed record WordSeed(
        string German,
        string Persian,
        WordType Type,
        string GermanExample,
        string PersianExample,
        NounArticle? Article,
        string? Plural);
}
