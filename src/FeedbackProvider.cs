using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Subsystem;
using System.Management.Automation.Subsystem.Feedback;
using System.Management.Automation.Subsystem.Prediction;

namespace Microsoft.PowerShell.FeedbackProvider;

public sealed class LinuxCommandNotFound : IFeedbackProvider, ICommandPredictor
{
    private readonly Guid _guid;
    private List<string>? _candidates;

    internal LinuxCommandNotFound(string guid)
    {
        _guid = new Guid(guid);
    }

    Dictionary<string, string>? ISubsystem.FunctionsToDefine => null;

    public Guid Id => _guid;

    public string Name => "cmd-not-found";

    public string Description => "The built-in feedback/prediction source for the Linux command utility.";

    #region IFeedbackProvider

    private static string? GetUtilityPath()
    {
        string cmd_not_found = "/usr/lib/command-not-found";
        bool exist = IsFileExecutable(cmd_not_found);

        if (!exist)
        {
            cmd_not_found = "/usr/share/command-not-found/command-not-found";
            exist = IsFileExecutable(cmd_not_found);
        }

        return exist ? cmd_not_found : null;

        static bool IsFileExecutable(string path)
        {
            var file = new FileInfo(path);
            return file.Exists && file.UnixFileMode.HasFlag(UnixFileMode.OtherExecute);
        }
    }

    public FeedbackItem? GetFeedback(string commandLine, ErrorRecord lastError, CancellationToken token)
    {
        if (Platform.IsWindows || lastError.FullyQualifiedErrorId != "CommandNotFoundException")
        {
            return null;
        }

        var target = (string)lastError.TargetObject;
        if (target is null)
        {
            return null;
        }

        if (target.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? cmd_not_found = GetUtilityPath();
        if (cmd_not_found is not null)
        {
            var startInfo = new ProcessStartInfo(cmd_not_found);
            startInfo.ArgumentList.Add(target);
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;

            using var process = Process.Start(startInfo);
            if (process is not null)
            {
                string? header = null;
                List<string>? actions = null;

                while (true)
                {
                    string? line = process.StandardError.ReadLine();
                    if (line is null)
                    {
                        break;
                    }

                    if (line == string.Empty)
                    {
                        continue;
                    }

                    if (line.StartsWith("sudo ", StringComparison.Ordinal))
                    {
                        actions ??= new List<string>();
                        actions.Add(line.TrimEnd());
                    }
                    else if (actions is null)
                    {
                        header = line;
                    }
                }

                if (actions is not null && header is not null)
                {
                    _candidates = actions;

                    var footer = process.StandardOutput.ReadToEnd().Trim();
                    return string.IsNullOrEmpty(footer)
                        ? new FeedbackItem(header, actions)
                        : new FeedbackItem(header, actions, footer, FeedbackDisplayLayout.Portrait);
                }
            }
        }

        return null;
    }

    #endregion

    #region ICommandPredictor

    public bool CanAcceptFeedback(PredictionClient client, PredictorFeedbackKind feedback)
    {
        return feedback switch
        {
            PredictorFeedbackKind.CommandLineAccepted => true,
            _ => false,
        };
    }

    public SuggestionPackage GetSuggestion(PredictionClient client, PredictionContext context, CancellationToken cancellationToken)
    {
        if (_candidates is not null)
        {
            string input = context.InputAst.Extent.Text;
            List<PredictiveSuggestion>? result = null;

            foreach (string c in _candidates)
            {
                if (c.StartsWith(input, StringComparison.OrdinalIgnoreCase))
                {
                    result ??= new List<PredictiveSuggestion>(_candidates.Count);
                    result.Add(new PredictiveSuggestion(c));
                }
            }

            if (result is not null)
            {
                return new SuggestionPackage(result);
            }
        }

        return default;
    }

    public void OnCommandLineAccepted(PredictionClient client, IReadOnlyList<string> history)
    {
        // Reset the candidate state.
        _candidates = null;
    }

    public void OnSuggestionDisplayed(PredictionClient client, uint session, int countOrIndex) { }

    public void OnSuggestionAccepted(PredictionClient client, uint session, string acceptedSuggestion) { }

    public void OnCommandLineExecuted(PredictionClient client, string commandLine, bool success) { }

    #endregion;
}

public class Init : IModuleAssemblyInitializer, IModuleAssemblyCleanup
{
    private const string Id = "47013747-CB9D-4EBC-9F02-F32B8AB19D48";

    public void OnImport()
    {
        var feedback = new LinuxCommandNotFound(Id);
        SubsystemManager.RegisterSubsystem(SubsystemKind.FeedbackProvider, feedback);
        SubsystemManager.RegisterSubsystem(SubsystemKind.CommandPredictor, feedback);
    }

    public void OnRemove(PSModuleInfo psModuleInfo)
    {
        SubsystemManager.UnregisterSubsystem<ICommandPredictor>(new Guid(Id));
        SubsystemManager.UnregisterSubsystem<IFeedbackProvider>(new Guid(Id));
    }
}
