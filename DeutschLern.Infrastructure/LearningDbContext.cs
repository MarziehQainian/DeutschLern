using DeutschLern.Domain;
using Microsoft.EntityFrameworkCore;

namespace DeutschLern.Infrastructure;

public sealed class LearningDbContext(DbContextOptions<LearningDbContext> options) : DbContext(options)
{
    public DbSet<LanguageLevel> LanguageLevels => Set<LanguageLevel>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Vocabulary> Vocabularies => Set<Vocabulary>();
    public DbSet<ExampleSentence> ExampleSentences => Set<ExampleSentence>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizOption> QuizOptions => Set<QuizOption>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<QuizAnswer> QuizAnswers => Set<QuizAnswer>();
    public DbSet<UserLessonProgress> UserLessonProgress => Set<UserLessonProgress>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LanguageLevel>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(2).IsRequired();
            entity.Property(x => x.PersianName).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => x.Order).IsUnique();
            entity.HasData(
                new LanguageLevel { Id = 1, Code = "A1", PersianName = "مقدماتی A1", Order = 1 },
                new LanguageLevel { Id = 2, Code = "A2", PersianName = "مقدماتی A2", Order = 2 },
                new LanguageLevel { Id = 3, Code = "B1", PersianName = "متوسط B1", Order = 3 },
                new LanguageLevel { Id = 4, Code = "B2", PersianName = "متوسط B2", Order = 4 },
                new LanguageLevel { Id = 5, Code = "C1", PersianName = "پیشرفته C1", Order = 5 });
        });

        modelBuilder.Entity<Lesson>(entity =>
        {
            entity.Property(x => x.GermanTitle).HasMaxLength(150).IsRequired();
            entity.Property(x => x.PersianTitle).HasMaxLength(150).IsRequired();
            entity.HasIndex(x => new { x.LanguageLevelId, x.Order }).IsUnique();
            entity.HasOne(x => x.LanguageLevel).WithMany(x => x.Lessons)
                .HasForeignKey(x => x.LanguageLevelId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Vocabulary>(entity =>
        {
            entity.Property(x => x.GermanWord).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PersianMeaning).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PluralForm).HasMaxLength(100);
            entity.HasIndex(x => new { x.LessonId, x.GermanWord }).IsUnique();
            entity.HasOne(x => x.Lesson).WithMany(x => x.Vocabularies)
                .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExampleSentence>(entity =>
        {
            entity.Property(x => x.GermanText).HasMaxLength(500).IsRequired();
            entity.Property(x => x.PersianTranslation).HasMaxLength(500).IsRequired();
            entity.HasOne(x => x.Vocabulary).WithMany(x => x.Examples)
                .HasForeignKey(x => x.VocabularyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Quiz>(entity =>
        {
            entity.HasIndex(x => x.LessonId).IsUnique();
            entity.HasOne(x => x.Lesson).WithOne(x => x.Quiz)
                .HasForeignKey<Quiz>(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuizQuestion>(entity =>
        {
            entity.Property(x => x.Text).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.QuizId, x.Order }).IsUnique();
            entity.HasOne(x => x.Quiz).WithMany(x => x.Questions)
                .HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuizOption>(entity =>
        {
            entity.Property(x => x.Text).HasMaxLength(300).IsRequired();
            entity.HasOne(x => x.Question).WithMany(x => x.Options)
                .HasForeignKey(x => x.QuizQuestionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuizAttempt>(entity =>
        {
            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.ScorePercent).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.UserId, x.QuizId, x.SubmittedAtUtc });
            entity.HasOne(x => x.Quiz).WithMany().HasForeignKey(x => x.QuizId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QuizAnswer>(entity =>
        {
            entity.HasIndex(x => new { x.QuizAttemptId, x.QuizQuestionId }).IsUnique();
            entity.HasOne(x => x.Attempt).WithMany(x => x.Answers)
                .HasForeignKey(x => x.QuizAttemptId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuizQuestionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SelectedOption).WithMany().HasForeignKey(x => x.SelectedOptionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<UserLessonProgress>(entity =>
        {
            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.HighestScorePercent).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.UserId, x.LessonId }).IsUnique();
            entity.HasOne(x => x.Lesson).WithMany().HasForeignKey(x => x.LessonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.LastQuizAttempt).WithMany().HasForeignKey(x => x.LastQuizAttemptId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
