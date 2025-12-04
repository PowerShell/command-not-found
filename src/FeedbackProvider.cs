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

    public string Name => "command-not-found";

    public string Description => "The built-in feedback/prediction source for the Linux command utility.";

    #region IFeedbackProvider

    private string? _commandNotFoundTool;

    private string? GetUtilityPath()
    {
        if (_commandNotFoundTool is null)
        {
            string cmd_not_found = "/usr/lib/command-not-found";
            bool exist = IsFileExecutable(cmd_not_found);

            if (!exist)
            {
                cmd_not_found = "/usr/share/command-not-found/command-not-found";
                exist = IsFileExecutable(cmd_not_found);
            }

            _commandNotFoundTool = exist ? cmd_not_found : null;
        }

        return _commandNotFoundTool;

        static bool IsFileExecutable(string path)
        {
            var file = new FileInfo(path);
            return file.Exists && file.UnixFileMode.HasFlag(UnixFileMode.OtherExecute);
        }
    }

    public FeedbackItem? GetFeedback(FeedbackContext context, CancellationToken token)
    {
        if (Platform.IsWindows)
        {
            return null;
        }

        // Use the default trigger 'CommandNotFound', so 'LastError' won't be null.
        var target = (string)context.LastError!.TargetObject;
        if (target is null || target.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? cmd_not_found = GetUtilityPath();
        if (cmd_not_found is null)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(cmd_not_found)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        startInfo.ArgumentList.Add("--no-failure-msg");
        startInfo.ArgumentList.Add(target);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is not null)
            {
                string? header = null, footer = null;
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
                        /*
                         * ----- Example Output (1) -----
                         * Command 'cargo' not found, but can be installed with:
                         * sudo snap install rustup  # version 1.28.2, or
                         * sudo apt  install cargo   # version 1.75.0+dfsg0ubuntu1-0ubuntu7.1
                         * sudo apt  install rustup  # version 1.26.0-3
                         * See 'snap info rustup' for additional versions.
                         *
                         * ----- Example Output (2) -----
                         * Command 'sshd' not found, but can be installed with:
                         * sudo apt install openssh-server
                        */
                        actions ??= [];

                        // Remove the ending comment about the package version.
                        line = line.Split('#', StringSplitOptions.TrimEntries)[0];

                        // 5 = "sudo ".Length
                        var rest = line.AsSpan(5);
                        if (rest.StartsWith("snap install ", StringComparison.Ordinal))
                        {
                            // 13 = "snap install ".Length
                            string package = GetPackageName(rest[13..]);
                            actions.Add($"snap info {package}");
                        }
                        else if (rest.StartsWith("apt  install", StringComparison.Ordinal))
                        {
                            line = line.Replace("apt  install", "apt install");
                        }

                        actions.Add(line);
                    }
                    else if (line.StartsWith("  "))
                    {
                        /*
                         * ----- Example Output -----
                         * Command 'dor' not found, did you mean:
                         *   command 'dog' from snap dog (v0.1.0)
                         *   command 'oor' from deb openoverlayrouter (1.3.0+ds1-3)
                         *   command 'vor' from deb vor (0.5.8-1)
                         *   command 'dir' from deb coreutils (9.4-3ubuntu6.1)
                         *   command 'dar' from deb dar (2.7.13-2)
                         *   command 'tor' from deb tor (0.4.8.10-1)
                         * See 'snap info <snapname>' for additional versions.
                        */
                        int index = line.IndexOf(" deb ", StringComparison.Ordinal);
                        if (index > 0)
                        {
                            // 5 = " deb ".Length
                            string package = GetPackageName(line.AsSpan(index + 5));
                            (actions ??= []).Add(line.Trim());
                            (_candidates ??= []).Add($"sudo apt install {package}");

                            continue;
                        }

                        index = line.IndexOf(" snap ", StringComparison.Ordinal);
                        if (index > 0)
                        {
                            // 6 = " snap ".Length
                            string package = GetPackageName(line.AsSpan(index + 6));
                            (actions ??= []).Add(line.Trim());

                            _candidates ??= [];
                            _candidates.Add($"snap info {package}");
                            _candidates.Add($"sudo snap install {package}");
                        }
                    }
                    else
                    {
                        if (actions is null)
                        {
                            header = line.Trim();
                        }
                        else
                        {
                            footer = line.Trim();
                        }
                    }
                }

                if (actions is not null && header is not null)
                {
                    _candidates ??= actions;
                    return string.IsNullOrEmpty(footer)
                        ? new FeedbackItem(header, actions)
                        : new FeedbackItem(header, actions, footer, FeedbackDisplayLayout.Portrait);
                }
            }
        }
        catch (Exception)
        {
            // Ignore any exceptions.
        }

        return null;
    }

    private static string GetPackageName(ReadOnlySpan<char> line)
    {
        int nextSpace = line.IndexOf(' ');
        var ret = nextSpace > 0 ? line[..nextSpace] : line;
        return ret.ToString();
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
