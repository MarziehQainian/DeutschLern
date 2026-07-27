using System.Text.Json;
using DeutschLern.Application;
using DeutschLern.Domain;
using DeutschLern.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DeutschLern.IntegrationTests;

public sealed class LearningServiceTests
{
    [Fact]
    public async Task Development_seed_is_complete_and_idempotent()
    {
        await using var db = CreateDatabase();
        await db.Database.EnsureCreatedAsync();

        await DevelopmentDataSeeder.SeedAsync(db);
        await DevelopmentDataSeeder.SeedAsync(db);

        (await db.Lessons.CountAsync()).Should().Be(10);
        (await db.Vocabularies.CountAsync()).Should().Be(30);
        (await db.ExampleSentences.CountAsync()).Should().Be(30);
        (await db.Quizzes.CountAsync()).Should().Be(10);
        (await db.QuizQuestions.CountAsync()).Should().Be(30);
        (await db.QuizOptions.CountAsync()).Should().Be(120);
        (await db.Vocabularies.AllAsync(x => x.Examples.Count > 0)).Should().BeTrue();
        (await db.Lessons.AllAsync(x => x.Quiz != null)).Should().BeTrue();
    }

    [Fact]
    public async Task Passing_attempt_is_stored_and_updates_progress()
    {
        await using var db = CreateDatabase();
        var lesson = CreateLessonWithQuiz();
        db.Add(lesson);
        await db.SaveChangesAsync();
        var service = new LearningService(db);
        var question = lesson.Quiz!.Questions.Single();
        var correctOption = question.Options.Single(x => x.IsCorrect);

        var result = await service.SubmitQuizAsync(
            lesson.Id,
            "student-1",
            [new QuizAnswerInput(question.Id, correctOption.Id)]);

        result.Passed.Should().BeTrue();
        result.ScorePercent.Should().Be(100m);
        (await db.QuizAttempts.CountAsync()).Should().Be(1);
        var progress = await db.UserLessonProgress.SingleAsync();
        progress.IsPassed.Should().BeTrue();
        progress.HighestScorePercent.Should().Be(100m);
        progress.LastQuizAttemptId.Should().Be(result.AttemptId);
    }

    [Fact]
    public async Task Public_quiz_contract_does_not_disclose_correct_answers()
    {
        await using var db = CreateDatabase();
        var lesson = CreateLessonWithQuiz();
        db.Add(lesson);
        await db.SaveChangesAsync();
        var service = new LearningService(db);

        var quiz = await service.GetQuizAsync(lesson.Id, "student-1");
        var json = JsonSerializer.Serialize(quiz);

        json.Should().NotContain("IsCorrect");
        json.Should().NotContain("CorrectOptionId");
    }

    [Fact]
    public async Task Latest_attempt_is_recorded_without_lowering_highest_score()
    {
        await using var db = CreateDatabase();
        var lesson = CreateLessonWithQuiz();
        db.Add(lesson);
        await db.SaveChangesAsync();
        var service = new LearningService(db);
        var question = lesson.Quiz!.Questions.Single();
        var correct = question.Options.Single(x => x.IsCorrect);
        var wrong = question.Options.Single(x => !x.IsCorrect);

        var first = await service.SubmitQuizAsync(lesson.Id, "student-1", [new(question.Id, correct.Id)]);
        var latest = await service.SubmitQuizAsync(lesson.Id, "student-1", [new(question.Id, wrong.Id)]);

        var progress = await db.UserLessonProgress.SingleAsync();
        progress.HighestScorePercent.Should().Be(100m);
        progress.LastQuizAttemptId.Should().Be(latest.AttemptId);
        progress.LastQuizAttemptId.Should().NotBe(first.AttemptId);
        progress.IsPassed.Should().BeTrue();
    }

    private static LearningDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<LearningDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LearningDbContext(options);
    }

    private static Lesson CreateLessonWithQuiz()
    {
        return new Lesson
        {
            LanguageLevelId = 1,
            GermanTitle = "Begrüßung",
            PersianTitle = "سلام و احوال‌پرسی",
            Order = 1,
            Quiz = new Quiz
            {
                Questions =
                [
                    new QuizQuestion
                    {
                        Text = "Was bedeutet Haus?",
                        Order = 1,
                        Options =
                        [
                            new QuizOption { Text = "خانه", IsCorrect = true },
                            new QuizOption { Text = "کتاب", IsCorrect = false }
                        ]
                    }
                ]
            }
        };
    }
}
