using System.ComponentModel.DataAnnotations;
using Cronos;
using MSLX.SDK.Models.Instance;
using Xunit;

namespace MSLX.Tests;

public class ScheduleTaskCronTests
{
    [Fact]
    public void Test6FieldCron_DayOfWeekCalculations()
    {
        // 2026-11-20 is a Friday.
        var baseFriday = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

        // 0 is Sunday
        var exprSunday0 = CronExpression.Parse("0 0 3 * * 0", CronFormat.IncludeSeconds);
        var nextSun0 = exprSunday0.GetNextOccurrence(baseFriday, TimeZoneInfo.Utc);
        Assert.NotNull(nextSun0);
        Assert.Equal(DayOfWeek.Sunday, nextSun0.Value.DayOfWeek);
        Assert.Equal(22, nextSun0.Value.Day);

        // 7 is also Sunday (Vixie cron convention)
        var exprSunday7 = CronExpression.Parse("0 0 3 * * 7", CronFormat.IncludeSeconds);
        var nextSun7 = exprSunday7.GetNextOccurrence(baseFriday, TimeZoneInfo.Utc);
        Assert.NotNull(nextSun7);
        Assert.Equal(DayOfWeek.Sunday, nextSun7.Value.DayOfWeek);
        Assert.Equal(22, nextSun7.Value.Day);

        // 1 is Monday
        var exprMonday = CronExpression.Parse("0 0 3 * * 1", CronFormat.IncludeSeconds);
        var nextMon = exprMonday.GetNextOccurrence(baseFriday, TimeZoneInfo.Utc);
        Assert.NotNull(nextMon);
        Assert.Equal(DayOfWeek.Monday, nextMon.Value.DayOfWeek);
        Assert.Equal(23, nextMon.Value.Day);

        // 5 is Friday (next Friday is 2026-11-27 if base is 2026-11-20 00:00:00 -> 2026-11-20 03:00:00)
        var exprFriday = CronExpression.Parse("0 0 3 * * 5", CronFormat.IncludeSeconds);
        var nextFri = exprFriday.GetNextOccurrence(baseFriday, TimeZoneInfo.Utc);
        Assert.NotNull(nextFri);
        Assert.Equal(DayOfWeek.Friday, nextFri.Value.DayOfWeek);
        Assert.Equal(20, nextFri.Value.Day);

        // 6 is Saturday (2026-11-21)
        var exprSat = CronExpression.Parse("0 0 3 * * 6", CronFormat.IncludeSeconds);
        var nextSat = exprSat.GetNextOccurrence(baseFriday, TimeZoneInfo.Utc);
        Assert.NotNull(nextSat);
        Assert.Equal(DayOfWeek.Saturday, nextSat.Value.DayOfWeek);
        Assert.Equal(21, nextSat.Value.Day);

        // Question mark in day field with Sunday (0 0 3 ? * 0)
        var exprQSun = CronExpression.Parse("0 0 3 ? * 0", CronFormat.IncludeSeconds);
        var nextQSun = exprQSun.GetNextOccurrence(baseFriday, TimeZoneInfo.Utc);
        Assert.NotNull(nextQSun);
        Assert.Equal(DayOfWeek.Sunday, nextQSun.Value.DayOfWeek);
        Assert.Equal(22, nextQSun.Value.Day);
    }

    [Fact]
    public void Test5FieldCron_DayOfWeekCalculations()
    {
        var baseFriday = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

        // 5 fields standard: min hour day month dow
        var exprSun5 = CronExpression.Parse("0 3 * * 0", CronFormat.Standard);
        var nextSun5 = exprSun5.GetNextOccurrence(baseFriday, TimeZoneInfo.Utc);
        Assert.NotNull(nextSun5);
        Assert.Equal(DayOfWeek.Sunday, nextSun5.Value.DayOfWeek);
        Assert.Equal(22, nextSun5.Value.Day);

        var exprMon5 = CronExpression.Parse("0 3 * * 1", CronFormat.Standard);
        var nextMon5 = exprMon5.GetNextOccurrence(baseFriday, TimeZoneInfo.Utc);
        Assert.NotNull(nextMon5);
        Assert.Equal(DayOfWeek.Monday, nextMon5.Value.DayOfWeek);
        Assert.Equal(23, nextMon5.Value.Day);
    }

    [Theory]
    [InlineData("0 0 12 * * ?", true)]
    [InlineData("*/10 * * * * *", true)]
    [InlineData("0 0 3 ? * 0", true)]
    [InlineData("0 0 3 ? * 1", true)]
    [InlineData("0 12 * * *", true)]       // 5 fields
    [InlineData("0 3 * * 0", true)]        // 5 fields Sunday
    [InlineData("0 3 * * 7", true)]        // 5 fields Sunday (7)
    [InlineData("*/5 * * * *", true)]      // 5 fields interval
    [InlineData("invalid cron", false)]
    [InlineData("* * * * * * * *", false)]
    [InlineData("60 * * * * *", false)]    // second out of range
    public void TestCronValidationAttribute(string cron, bool expectedValid)
    {
        var model = new CreateTaskRequest
        {
            Name = "Test Task",
            InstanceId = 1,
            Type = "command",
            Cron = cron
        };

        var context = new ValidationContext(model) { MemberName = nameof(CreateTaskRequest.Cron) };
        var results = new List<ValidationResult>();
        bool isValid = Validator.TryValidateProperty(cron, context, results);

        Assert.Equal(expectedValid, isValid);
    }
}
