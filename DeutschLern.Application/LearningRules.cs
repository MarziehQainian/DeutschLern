namespace DeutschLern.Application;

public sealed record QuizScore(decimal Percent, bool Passed);

public static class QuizScorer
{
    public const decimal PassingPercent = 60m;

    public static QuizScore Calculate(int correctAnswers, int totalQuestions) =>
        throw new NotImplementedException();
}

public static class LessonAccessPolicy
{
    public static bool CanAccess(int lessonOrder, bool previousLessonPassed) =>
        throw new NotImplementedException();
}
