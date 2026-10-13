using System.Drawing;
using System.IO;
using Applitools;
using Applitools.Images;
using NUnit.Framework;

namespace WpfVisualValidationDemo.VisualTests;

[TestFixture]
public class ComponentLabVisualTests
{
    private const string AppName = "Component Lab";
    private const string TestName = "Control gallery";
    private static readonly BatchInfo Batch =
        new(Environment.GetEnvironmentVariable("APPLITOOLS_BATCH_NAME") ?? "Component Lab — WPF");

    /// Figma frame "Control gallery" (1184×781, the app's client area at 100% DPI).
    /// Source of truth for the design; design/control-gallery.figma.png is an export of it.
    private const string ControlGalleryDesignUrl =
        "https://www.figma.com/design/ezwnpJMHvW5YqQNyr5vGRJ/Insuracare-Component-Lab-%E2%80%94-Control-Gallery?node-id=3-2";

    private Eyes _eyes = null!;
    private AppSession? _app;

    [OneTimeSetUp]
    public void LoadCredentials() => DotEnv.Load();

    [SetUp]
    public void SetUp()
    {
        // The design baseline lives in its own environment (the same convention the
        // Applitools Figma plugin uses), so it never collides with baselines captured from
        // the app on the default environment.
        _eyes = new Eyes { Batch = Batch, HostOS = "Figma", HostApp = "Figma" };
    }

    /// Figma Dev Comparison: when a FIGMA_ACCESS_TOKEN (scope file_content:read) is present,
    /// Eyes fetches the design frame itself and always compares against it ("figma-baseline":
    /// accepting a run never replaces the design, so design edits are caught on the next run).
    /// Set APPLITOOLS_FIGMA_MODE=auto-baseline to switch to "design until accepted, then the
    /// accepted capture". Without a token the test compares against the baseline seeded by
    /// SeedBaselineFromFigmaDesign below.
    /// Only the app test opts in — the seed test uploads a local PNG and must not depend on
    /// the Figma API.
    private void EnableFigmaDevComparison()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FIGMA_ACCESS_TOKEN"))) return;

        _eyes.FigmaBaselines = new Dictionary<string, object>
        {
            // One entry per checkpoint; "" leaves that step as a normal regression step.
            [TestName] = new[] { ControlGalleryDesignUrl, "" },
        };
        _eyes.FigmaOptions = new FigmaOptions
        {
            Mode = Environment.GetEnvironmentVariable("APPLITOOLS_FIGMA_MODE") ?? "figma-baseline",
            // Figma's image-render quota is small and its Retry-After can be days, so keep the
            // fetched frame on disk and reuse it: one Figma call per week, not one per run.
            TempPath = Environment.GetEnvironmentVariable("APPLITOOLS_FIGMA_TEMP_PATH")
                       ?? Path.Combine(Path.GetTempPath(), "applitools-figma-cache"),
            NodesCacheTtlSeconds = 7 * 24 * 60 * 60,
        };
    }

    [TearDown]
    public void TearDown()
    {
        _eyes.AbortIfNotClosed();
        _app?.Dispose();
    }

    /// Seeds (or re-seeds) the Applitools baseline for "Control gallery" from the exported
    /// Figma frame, so the app is validated against the design without a Figma token.
    /// Run on demand:  dotnet test --filter Name~SeedBaselineFromFigmaDesign
    /// Then, if a baseline already existed, accept the run in the dashboard.
    [Test, Explicit("Overwrites the baseline with the Figma design; run deliberately.")]
    public void SeedBaselineFromFigmaDesign()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "design", "control-gallery.figma.png");
        using var design = new Bitmap(path);

        _eyes.Open(AppName, TestName, design.Size);
        _eyes.Check(Target.Image(design).WithName("Initial state"));
        _eyes.Check(Target.Image(design).WithName("After clicking Save changes"));

        var results = _eyes.Close(false);
        TestContext.Out.WriteLine($"Seeded from Figma design: {results.Url}");
        Assert.That(results.IsNew || results.IsPassed, Is.True,
            $"Seed did not become the baseline (an older baseline exists) — accept it in the dashboard: {results.Url}");
    }

    [Test]
    public void ControlGallery_MatchesBaseline()
    {
        _app = AppSession.Launch();
        using var initial = _app.Capture();
        EnableFigmaDevComparison();
        _eyes.Open(AppName, TestName, new Size(initial.Width, initial.Height));

        _eyes.Check(Target.Image(initial).WithName("Initial state"));

        _app.Click("Save changes");
        _app.WaitForText("Changes saved");
        using var afterClick = _app.Capture();
        _eyes.Check(Target.Image(afterClick).WithName("After clicking Save changes"));

        var results = _eyes.Close(false);
        TestContext.Out.WriteLine($"Eyes results: {results.Url}");
        Assert.That(results.IsPassed, Is.True,
            $"Visual differences found ({results.Mismatches} mismatching step(s)). Review: {results.Url}");
    }

    /// Walks through every interactive control on the screen, capturing a checkpoint after each
    /// state change. A plain regression test: it compares against its own previous run, not the
    /// Figma design (no design frames exist for these states), so it uses the default environment.
    [Test]
    public void ControlGallery_Walkthrough()
    {
        _eyes = new Eyes { Batch = Batch };
        _app = AppSession.Launch();

        using (var initial = _app.Capture())
        {
            _eyes.Open(AppName, "Control gallery walkthrough", new Size(initial.Width, initial.Height));
            _eyes.Check(Target.Image(initial).WithName("Initial state"));
        }

        // Buttons
        _app.Click("Cancel");
        _app.WaitForText("Changes cancelled");
        Checkpoint("Secondary button clicked");

        _app.Click("Escalate");
        _app.WaitForText("Escalation created");
        Checkpoint("Critical button clicked");

        // Selection and input
        _app.SelectComboItem("PriorityPicker", "Urgent");
        _app.WaitForText("Priority changed to: Urgent");
        Checkpoint("Priority set to Urgent");

        _app.SetText("ClinicalNote", "Patient reviewed — vitals stable");
        _app.WaitForText("Clinical note updated");
        Checkpoint("Clinical note edited");

        _app.Toggle("ConfirmationCheck");
        _app.WaitForText("Review confirmation selected");
        Checkpoint("Review confirmation checked");

        _app.Toggle("AlertToggle");
        _app.WaitForText("Alert notifications enabled");
        Checkpoint("Alert notifications on");

        // Workflow scenario (below the fold)
        _app.ScrollTo(100);
        Checkpoint("Scrolled to workflow scenario");

        _app.SelectComboItem("ScenarioPicker", "Needs attention");
        _app.WaitForText("Attention scenario loaded");
        Checkpoint("Needs-attention scenario");

        _app.ClickById("ScenarioAction");
        _app.WaitForText("Escalate action completed");
        Checkpoint("Scenario action invoked");

        _app.SelectComboItem("ScenarioPicker", "Data unavailable");
        _app.WaitForText("Unavailable scenario loaded");
        Checkpoint("Data-unavailable scenario (action disabled)");

        var results = _eyes.Close(false);
        TestContext.Out.WriteLine($"Eyes results: {results.Url}");
        Assert.That(results.IsPassed, Is.True,
            $"Visual differences found ({results.Mismatches} mismatching step(s)). Review: {results.Url}");
    }

    private void Checkpoint(string name)
    {
        using var image = _app!.Capture();
        _eyes.Check(Target.Image(image).WithName(name));
    }
}
