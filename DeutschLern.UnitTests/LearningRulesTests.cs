using DeutschLern.Application;
using DeutschLern.Domain;
using FluentAssertions;

namespace DeutschLern.UnitTests;

public sealed class LearningRulesTests
{
    [Fact]
    public void Exactly_sixty_percent_is_a_pass()
    {
        QuizScorer.Calculate(3, 5).Should().Be(new QuizScore(60m, true));
    }

    [Fact]
    public void Less_than_sixty_percent_is_a_failure()
    {
        QuizScorer.Calculate(2, 5).Should().Be(new QuizScore(40m, false));
    }

    [Fact]
    public void Percentage_is_calculated_with_decimal_precision()
    {
        QuizScorer.Calculate(2, 3).Percent.Should().Be(66.67m);
    }

    [Fact]
    public void First_lesson_is_always_available()
    {
        LessonAccessPolicy.CanAccess(1, false).Should().BeTrue();
    }

    [Fact]
    public void Next_lesson_opens_after_previous_pass()
    {
        LessonAccessPolicy.CanAccess(2, true).Should().BeTrue();
    }

    [Fact]
    public void Next_lesson_stays_locked_after_previous_failure()
    {
        LessonAccessPolicy.CanAccess(2, false).Should().BeFalse();
    }

    [Fact]
    public void Vocabulary_requires_at_least_one_example()
    {
        var vocabulary = new Vocabulary
        {
            GermanWord = "Haus",
            PersianMeaning = "خانه",
            WordType = WordType.Noun,
            Article = NounArticle.Das
        };

        var action = vocabulary.ValidateForPublishing;

        action.Should().Throw<DomainValidationException>()
            .WithMessage("*example*");
    }
}
