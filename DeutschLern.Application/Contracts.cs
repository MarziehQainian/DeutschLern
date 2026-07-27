using DeutschLern.Domain;

namespace DeutschLern.Application;

public sealed record QuizOptionDto(int Id, string Text);
public sealed record QuizQuestionDto(int Id, string Text, IReadOnlyList<QuizOptionDto> Options);
public sealed record QuizDto(int Id, int LessonId, IReadOnlyList<QuizQuestionDto> Questions);
public sealed record QuizAnswerInput(int QuestionId, int? SelectedOptionId);
public sealed record QuizAnswerResult(int QuestionId, int? SelectedOptionId, int CorrectOptionId, bool IsCorrect);
public sealed record QuizResultDto(int AttemptId, decimal ScorePercent, bool Passed, IReadOnlyList<QuizAnswerResult> Answers);
public sealed record LessonCardDto(int Id, string GermanTitle, string PersianTitle, int Order, bool IsAvailable, bool IsPassed, decimal HighestScore);
public sealed record LevelProgressDto(int Id, string Code, string PersianName, decimal ProgressPercent, IReadOnlyList<LessonCardDto> Lessons);

public interface ILearningService
{
    Task<IReadOnlyList<LevelProgressDto>> GetDashboardAsync(string userId, CancellationToken cancellationToken = default);
    Task<Lesson?> GetLessonAsync(int lessonId, string userId, CancellationToken cancellationToken = default);
    Task<QuizDto> GetQuizAsync(int lessonId, string userId, CancellationToken cancellationToken = default);
    Task<QuizResultDto> SubmitQuizAsync(int lessonId, string userId, IReadOnlyCollection<QuizAnswerInput> answers, CancellationToken cancellationToken = default);
}

public sealed class LessonLockedException() : InvalidOperationException("Complete the previous lesson before opening this lesson.");
