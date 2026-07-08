namespace ElBruno.CopilotHarness.Router.Core;

/// <summary>
/// Canonical intent vocabulary used by the classifier and by <c>IntentEquals</c> routing rules.
/// Defined in Router.Core so both the persistence/seed layer and the Router.Api classifier share
/// the exact same intent labels.
/// </summary>
public static class ClassifierIntentNames
{
    public const string SimpleChat = "simple-chat";
    public const string GithubActions = "github-actions";
    public const string LaunchApp = "launch-app";
    public const string CodeTask = "code-task";
    public const string LongForm = "long-form";

    public static readonly IReadOnlyList<string> All =
    [
        SimpleChat, GithubActions, LaunchApp, CodeTask, LongForm
    ];
}
