namespace DeutschLern.Domain;

public enum WordType { Noun, Verb, Adjective, Adverb, Other }
public enum NounArticle { Der, Die, Das }

public sealed class LanguageLevel
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string PersianName { get; set; }
    public int Order { get; set; }
    public List<Lesson> Lessons { get; set; } = [];
}

public sealed class Lesson
{
    public int Id { get; set; }
    public int LanguageLevelId { get; set; }
    public required string GermanTitle { get; set; }
    public required string PersianTitle { get; set; }
    public int Order { get; set; }
    public LanguageLevel? LanguageLevel { get; set; }
    public List<Vocabulary> Vocabularies { get; set; } = [];
    public Quiz? Quiz { get; set; }
}

public sealed class Vocabulary
{
    public int Id { get; set; }
    public int LessonId { get; set; }
    public required string GermanWord { get; set; }
    public required string PersianMeaning { get; set; }
    public WordType WordType { get; set; }
    public NounArticle? Article { get; set; }
    public string? PluralForm { get; set; }
    public Lesson? Lesson { get; set; }
    public List<ExampleSentence> Examples { get; set; } = [];

    public void ValidateForPublishing()
    {
        if (Examples.Count == 0)
        {
            throw new DomainValidationException("At least one example sentence is required.");
        }
    }
}

public sealed class ExampleSentence
{
    public int Id { get; set; }
    public int VocabularyId { get; set; }
    public required string GermanText { get; set; }
    public required string PersianTranslation { get; set; }
    public Vocabulary? Vocabulary { get; set; }
}

public sealed class Quiz
{
    public int Id { get; set; }
    public int LessonId { get; set; }
    public Lesson? Lesson { get; set; }
    public List<QuizQuestion> Questions { get; set; } = [];
}

public sealed class QuizQuestion
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public required string Text { get; set; }
    public int Order { get; set; }
    public Quiz? Quiz { get; set; }
    public List<QuizOption> Options { get; set; } = [];
}

public sealed class QuizOption
{
    public int Id { get; set; }
    public int QuizQuestionId { get; set; }
    public required string Text { get; set; }
    public bool IsCorrect { get; set; }
    public QuizQuestion? Question { get; set; }
}

public sealed class QuizAttempt
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public int QuizId { get; set; }
    public decimal ScorePercent { get; set; }
    public bool Passed { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; }
    public Quiz? Quiz { get; set; }
    public List<QuizAnswer> Answers { get; set; } = [];
}

public sealed class QuizAnswer
{
    public int Id { get; set; }
    public int QuizAttemptId { get; set; }
    public int QuizQuestionId { get; set; }
    public int? SelectedOptionId { get; set; }
    public bool IsCorrect { get; set; }
    public QuizAttempt? Attempt { get; set; }
    public QuizQuestion? Question { get; set; }
    public QuizOption? SelectedOption { get; set; }
}

public sealed class UserLessonProgress
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public int LessonId { get; set; }
    public decimal HighestScorePercent { get; set; }
    public int? LastQuizAttemptId { get; set; }
    public bool IsPassed { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Lesson? Lesson { get; set; }
    public QuizAttempt? LastQuizAttempt { get; set; }
}

public sealed class DomainValidationException(string message) : InvalidOperationException(message);
