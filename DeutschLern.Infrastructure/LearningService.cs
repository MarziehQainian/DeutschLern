using DeutschLern.Application;
using DeutschLern.Domain;
using Microsoft.EntityFrameworkCore;

namespace DeutschLern.Infrastructure;

public sealed class LearningService(LearningDbContext dbContext) : ILearningService
{
    public async Task<IReadOnlyList<LevelProgressDto>> GetDashboardAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var levels = await dbContext.LanguageLevels
            .AsNoTracking()
            .Include(x => x.Lessons.OrderBy(l => l.Order))
            .OrderBy(x => x.Order)
            .ToListAsync(cancellationToken);
        var progress = await dbContext.UserLessonProgress
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToDictionaryAsync(x => x.LessonId, cancellationToken);

        return levels.Select(level =>
        {
            var cards = level.Lessons.Select((lesson, index) =>
            {
                progress.TryGetValue(lesson.Id, out var current);
                var previousPassed = index == 0 ||
                    progress.TryGetValue(level.Lessons[index - 1].Id, out var previous) && previous.IsPassed;
                return new LessonCardDto(
                    lesson.Id,
                    lesson.GermanTitle,
                    lesson.PersianTitle,
                    lesson.Order,
                    LessonAccessPolicy.CanAccess(lesson.Order, previousPassed),
                    current?.IsPassed ?? false,
                    current?.HighestScorePercent ?? 0);
            }).ToList();
            var percent = cards.Count == 0 ? 0 : Math.Round(cards.Count(x => x.IsPassed) * 100m / cards.Count, 2);
            return new LevelProgressDto(level.Id, level.Code, level.PersianName, percent, cards);
        }).ToList();
    }

    public async Task<Lesson?> GetLessonAsync(
        int lessonId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var lesson = await dbContext.Lessons
            .AsNoTracking()
            .Include(x => x.Vocabularies).ThenInclude(x => x.Examples)
            .SingleOrDefaultAsync(x => x.Id == lessonId, cancellationToken);
        if (lesson is null)
        {
            return null;
        }

        await EnsureLessonAccessAsync(lesson, userId, cancellationToken);
        return lesson;
    }

    public async Task<QuizDto> GetQuizAsync(
        int lessonId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var lesson = await dbContext.Lessons.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == lessonId, cancellationToken)
            ?? throw new KeyNotFoundException("Lesson was not found.");
        await EnsureLessonAccessAsync(lesson, userId, cancellationToken);

        var quiz = await dbContext.Quizzes.AsNoTracking()
            .Include(x => x.Questions.OrderBy(q => q.Order))
            .ThenInclude(x => x.Options)
            .SingleOrDefaultAsync(x => x.LessonId == lessonId, cancellationToken)
            ?? throw new InvalidOperationException("This lesson does not have a quiz.");

        return new QuizDto(
            quiz.Id,
            quiz.LessonId,
            quiz.Questions.Select(question => new QuizQuestionDto(
                question.Id,
                question.Text,
                question.Options.Select(option => new QuizOptionDto(option.Id, option.Text)).ToList())).ToList());
    }

    public async Task<QuizResultDto> SubmitQuizAsync(
        int lessonId,
        string userId,
        IReadOnlyCollection<QuizAnswerInput> answers,
        CancellationToken cancellationToken = default)
    {
        var lesson = await dbContext.Lessons.SingleOrDefaultAsync(x => x.Id == lessonId, cancellationToken)
            ?? throw new KeyNotFoundException("Lesson was not found.");
        await EnsureLessonAccessAsync(lesson, userId, cancellationToken);

        var quiz = await dbContext.Quizzes
            .Include(x => x.Questions).ThenInclude(x => x.Options)
            .SingleOrDefaultAsync(x => x.LessonId == lessonId, cancellationToken)
            ?? throw new InvalidOperationException("This lesson does not have a quiz.");
        if (quiz.Questions.Count == 0)
        {
            throw new InvalidOperationException("A quiz needs at least one question.");
        }

        var answerLookup = answers.GroupBy(x => x.QuestionId)
            .ToDictionary(x => x.Key, x => x.Last().SelectedOptionId);
        var answerResults = quiz.Questions.Select(question =>
        {
            answerLookup.TryGetValue(question.Id, out var selectedId);
            if (selectedId is not null && question.Options.All(x => x.Id != selectedId))
            {
                throw new InvalidOperationException("An answer contains an option from another question.");
            }

            var correct = question.Options.SingleOrDefault(x => x.IsCorrect)
                ?? throw new InvalidOperationException("Every question must have exactly one correct option.");
            return new QuizAnswerResult(question.Id, selectedId, correct.Id, selectedId == correct.Id);
        }).ToList();

        var score = QuizScorer.Calculate(answerResults.Count(x => x.IsCorrect), quiz.Questions.Count);
        var now = DateTimeOffset.UtcNow;
        var attempt = new QuizAttempt
        {
            UserId = userId,
            QuizId = quiz.Id,
            ScorePercent = score.Percent,
            Passed = score.Passed,
            SubmittedAtUtc = now,
            Answers = answerResults.Select(x => new QuizAnswer
            {
                QuizQuestionId = x.QuestionId,
                SelectedOptionId = x.SelectedOptionId,
                IsCorrect = x.IsCorrect
            }).ToList()
        };
        dbContext.QuizAttempts.Add(attempt);
        await dbContext.SaveChangesAsync(cancellationToken);

        var progress = await dbContext.UserLessonProgress
            .SingleOrDefaultAsync(x => x.UserId == userId && x.LessonId == lessonId, cancellationToken);
        if (progress is null)
        {
            progress = new UserLessonProgress { UserId = userId, LessonId = lessonId };
            dbContext.UserLessonProgress.Add(progress);
        }

        progress.HighestScorePercent = Math.Max(progress.HighestScorePercent, score.Percent);
        progress.IsPassed |= score.Passed;
        progress.LastQuizAttemptId = attempt.Id;
        progress.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new QuizResultDto(attempt.Id, score.Percent, score.Passed, answerResults);
    }

    private async Task EnsureLessonAccessAsync(Lesson lesson, string userId, CancellationToken cancellationToken)
    {
        if (lesson.Order == 1)
        {
            return;
        }

        var previousLessonId = await dbContext.Lessons.AsNoTracking()
            .Where(x => x.LanguageLevelId == lesson.LanguageLevelId && x.Order == lesson.Order - 1)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var previousPassed = previousLessonId is not null &&
            await dbContext.UserLessonProgress.AsNoTracking().AnyAsync(
                x => x.UserId == userId && x.LessonId == previousLessonId && x.IsPassed,
                cancellationToken);
        if (!LessonAccessPolicy.CanAccess(lesson.Order, previousPassed))
        {
            throw new LessonLockedException();
        }
    }
}
