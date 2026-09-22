using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The rules a batch rename is checked against before anything is renamed.
/// </summary>
/// <remarks>
/// These were a private method on 1.x's BatchRenameDialog and had no test; a Form had to be typed
/// into to reach them.
/// </remarks>
public sealed class BatchRenamePlanTests
{
    [Fact]
    public void ReplacingPreviewsEveryNameAndCountsTheChanges()
    {
        var plan = BatchRenamePlan.Build(["IMG_1.jpg", "IMG_2.jpg", "notes.txt"], [], "img", "photo");

        Assert.True(plan.CanApply);
        Assert.Equal(["photo_1.jpg", "photo_2.jpg", "notes.txt"], plan.Lines.Select(line => line.Target));
        Assert.Equal(2, plan.Changes.Count);
        Assert.Equal(Ui.Format(Ui.Shell.BatchRenameSummaryFormat, 2), plan.Summary);
        Assert.Equal(Ui.Format(Ui.Shell.BatchRenameUnchangedFormat, "notes.txt"), plan.Lines[2].Text);
    }

    [Fact]
    public void NothingToFindIsNotAPlan()
    {
        Assert.Equal(
            Ui.Validation.EnterTextToFindInTheSelected,
            BatchRenamePlan.Build(["a", "b"], [], "", "x").Problem);
    }

    [Fact]
    public void AFindThatMatchesNothingIsNotAPlan()
    {
        Assert.Equal(
            Ui.Validation.NoneOfTheSelectedNamesContain,
            BatchRenamePlan.Build(["a", "b"], [], "zzz", "x").Problem);
    }

    [Fact]
    public void TwoNamesCannotBecomeOne()
    {
        var plan = BatchRenamePlan.Build(["draft-a", "final-a"], [], "draft-", "final-");

        Assert.False(plan.CanApply);
        Assert.Equal(Ui.Validation.ATargetNameCollidesWithAnother, plan.Problem);
    }

    [Fact]
    public void ANameCannotLandOnSomethingElseInTheFolder()
    {
        var plan = BatchRenamePlan.Build(["a-1", "a-2"], ["a-1", "a-2", "b-1"], "a-", "b-");

        Assert.Equal(Ui.Format(Ui.Validation.AnItemNamedAlreadyExistsFormat, "b-1"), plan.Problem);
    }

    /// <summary>
    /// Two selected names both becoming the same third name is refused, whatever case they differ in.
    /// </summary>
    [Fact]
    public void TwoTargetsThatDifferOnlyInCaseCollide()
    {
        var plan = BatchRenamePlan.Build(["x1", "X1"], [], "1", "2");

        Assert.Equal(Ui.Validation.TwoSelectedItemsWouldReceiveTheSame, plan.Problem);
    }

    /// <summary>Changing only an item's case is a rename of that item, not a collision with it.</summary>
    [Fact]
    public void ACaseOnlyRenameIsAllowed()
    {
        var plan = BatchRenamePlan.Build(["report.pdf", "Summary.pdf"], ["report.pdf", "Summary.pdf"], "report", "Report");

        Assert.True(plan.CanApply);
        Assert.Equal("Report.pdf", plan.Changes.Single().Target);
    }

    [Fact]
    public void ANewNameMustBeAValidName()
    {
        var plan = BatchRenamePlan.Build(["a.txt", "b.txt"], [], ".txt", "/x");

        Assert.False(plan.CanApply);
        Assert.Equal(PaneItemNameRules.Validate("a/x"), plan.Problem);
    }
}
