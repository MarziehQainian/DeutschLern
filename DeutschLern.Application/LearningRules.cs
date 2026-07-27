namespace DeutschLern.Application;

public sealed record QuizScore(decimal Percent, bool Passed);

public static class QuizScorer
{
    public const decimal PassingPercent = 60m;

    public static QuizScore Calculate(int correctAnswers, int totalQuestions)
    {
        if (totalQuestions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalQuestions), "A quiz needs at least one question.");
        }

        if (correctAnswers < 0 || correctAnswers > totalQuestions)
        {
            throw new ArgumentOutOfRangeException(nameof(correctAnswers));
        }

        var percent = Math.Round(correctAnswers * 100m / totalQuestions, 2, MidpointRounding.AwayFromZero);
        return new QuizScore(percent, percent >= PassingPercent);
    }
}

public static class LessonAccessPolicy
{
    public static bool CanAccess(int lessonOrder, bool previousLessonPassed)
    {
        if (lessonOrder < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(lessonOrder));
        }

        return lessonOrder == 1 || previousLessonPassed;
    }
}
