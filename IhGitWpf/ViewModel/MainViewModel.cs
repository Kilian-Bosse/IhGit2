﻿using AdysTech.CredentialManager;
using CliWrap.Exceptions;
using CliWrap;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Octokit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using Repository = LibGit2Sharp.Repository;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using IhGitWpf.Properties;
using System.Windows.Data;

namespace IhGitWpf.ViewModel;

public sealed partial class MainViewModel : ObservableRecipient
{
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(UpMergeCommand))]
    private int maxMajorVersion = Settings.Default.MaxMajorVersion;

    partial void OnMaxMajorVersionChanged(int value)
    {
        Settings.Default.MaxMajorVersion = value;
    }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(UpMergeCommand))]
    private int maxMinorVersion = Settings.Default.MaxMinorVersion;

    partial void OnMaxMinorVersionChanged(int value)
    {
        Settings.Default.MaxMinorVersion = value;
    }

    [ObservableProperty]
    private bool maxVersionIsDeploy = false;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(LoadPrCommand))]
    private string prNumber = "";

    [ObservableProperty]
    private PullRequest? pr = null;

    [ObservableProperty]
    private ObservableCollectionEx<Reviewer> reviewers = [];

    [ObservableProperty]
    private string reviewerFilter = "";

    partial void OnReviewerFilterChanged(string value)
    {
        this.reviewerView?.Refresh();
    }

    [ObservableProperty]
    private string title = "";

    [ObservableProperty]
    private string body = "";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(UpMergeCommand))]
    private ObservableCollectionEx<Commit> commits = [];

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(UpMergeCommand))]
    private ObservableCollectionEx<BranchVersion> versionsToConsider = [];

    [ObservableProperty]
    private bool showZohoButton;

    [ObservableProperty]
    private ObservableCollection<string> logs = [];

    private string zohoUrl = "";

    [ObservableProperty]
    private string userName = Settings.Default.UserName;

    partial void OnUserNameChanged(string value)
    {
        Settings.Default.UserName = value;
    }

    [ObservableProperty]
    private string password = Settings.Default.Password;

    partial void OnPasswordChanged(string value)
    {
        Settings.Default.Password = value;
    }

    [ObservableProperty]
    private string gitHubToken = Settings.Default.GitHubToken;

    partial void OnGitHubTokenChanged(string value)
    {
        Settings.Default.GitHubToken = value;
    }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(StatusCommand)), NotifyCanExecuteChangedFor(nameof(UpMergeCommand))]
    private string repoPath = Settings.Default.RepoPath;

    partial void OnRepoPathChanged(string value)
    {
        Settings.Default.RepoPath = value;
    }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(UpMergeCommand))]
    private string featureName = "";

    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private ListCollectionView reviewerView;

    [GeneratedRegex("I(\\d*)")]
    private static partial Regex ZohoTicketRegex();

    public MainViewModel()
    {
    }

    [RelayCommand]
    private void Loaded()
    {
    }

    #region Collections Notify Hack
    partial void OnVersionsToConsiderChanging(ObservableCollectionEx<BranchVersion>? oldValue, ObservableCollectionEx<BranchVersion> newValue)
    {
        if (oldValue is not null)
        {
            ((INotifyPropertyChanged)newValue).PropertyChanged -= VersionsToConsiderChanged;
        }

        if (newValue is not null)
        {
            ((INotifyPropertyChanged)newValue).PropertyChanged += VersionsToConsiderChanged;
        }
    }

    partial void OnCommitsChanging(ObservableCollectionEx<Commit>? oldValue, ObservableCollectionEx<Commit> newValue)
    {
        if (oldValue is not null)
        {
            ((INotifyPropertyChanged)newValue).PropertyChanged -= CommitsChanged;
        }

        if (newValue is not null)
        {
            ((INotifyPropertyChanged)newValue).PropertyChanged += CommitsChanged;
        }
    }

    private void CommitsChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpMergeCommand.NotifyCanExecuteChanged();
    }

    private void VersionsToConsiderChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpMergeCommand.NotifyCanExecuteChanged();
    }
    #endregion

    private bool IsPrNumberNumber() => int.TryParse(PrNumber, out _);

    [RelayCommand(CanExecute = nameof(IsPrNumberNumber))]
    private async Task LoadPr()
    {
        if (!int.TryParse(PrNumber, out var prNum))
            return;

        ClearUi();

        var client = new GitHubClient(new ProductHeaderValue("IhGit"));
        var tokenAuth = new Octokit.Credentials(GitHubToken);
        client.Credentials = tokenAuth;

        //var all = await client.Repository.GetAllForOrg("airsphere-gmbh");

        const long repoId = 194316446;
        Pr = await client.PullRequest.Get(repoId, prNum);
        Title = Pr.Title;
        Body = Pr.Body;
        var a = await client.PullRequest.Commits(repoId, prNum);
        var b = a.Select(x => new Commit(x)).ToArray();
        Commits = [.. b];

        var orgaUsers = await client.Organization.Member.GetAll("airsphere-gmbh");

        var requestedReviews = await client.PullRequest.ReviewRequest.Get(repoId, prNum);
        var reviews = await client.PullRequest.Review.GetAll(repoId, prNum);

        var allReviewers = Enumerable.Concat(requestedReviews.Users, reviews.Select(x => x.User)).Select(x => new Reviewer(x)).Distinct();

        Reviewers = [.. orgaUsers.Select(x =>
        {
            var reviewer = new Reviewer(x);
            reviewer.IsSelected = allReviewers.Contains(reviewer);
            return reviewer;
        })];

        reviewerView = (ListCollectionView)CollectionViewSource.GetDefaultView(Reviewers);
        reviewerView.IsLiveSorting = true;
        reviewerView.SortDescriptions.Add(new(nameof(Reviewer.IsSelected), ListSortDirection.Descending));
        reviewerView.Filter = FilterReviewers;

        var index = Pr.Head.Ref.LastIndexOf('/');
        if (index == -1 || index == Pr.Head.Ref.Length - 1)
        {
            FeatureName = Pr.Head.Ref;
        }
        else
        {
            FeatureName = Pr.Head.Ref[(index + 1)..];
        }

        var regex = ZohoTicketRegex().Match(Title);
        ShowZohoButton = regex.Success;

        if (regex.Success)
        {
            zohoUrl = $"https://sprints.zoho.eu/team/airsphere#itemdetails/P9/I{regex.Groups[1].Value}";
        }

        var prBranchVersion = new BranchVersion(Pr.Base.Ref);

        switch (prBranchVersion.BranchType)
        {
            case BranchType.support:
                List<BranchVersion> result = [];

                if (prBranchVersion.Minor < MaxMinorVersion)
                {
                    // Count up all support versions
                    for (int i = prBranchVersion.Minor + 1; i < MaxMinorVersion; i++)
                        result.Add(new(prBranchVersion.Major, i));
                }

                // When we are on 4.19 already and max version is 4.19, then dont add 4.19
                if (prBranchVersion.Minor < MaxMinorVersion)
                {
                    // Add max version
                    result.Add(new(prBranchVersion.Major, MaxMinorVersion, MaxVersionIsDeploy ? BranchType.deploy : BranchType.support));
                }

                // Add stable
                result.Add(new(0, 0, BranchType.stable));

                VersionsToConsider = [.. result];
                break;

            case BranchType.deploy:
                VersionsToConsider = [new(0, 0, BranchType.stable)];
                break;

            case BranchType.stable:
            default:
                // Nothing todo
                break;
        }
    }

    private bool FilterReviewers(object obj)
    {
        if (string.IsNullOrWhiteSpace(ReviewerFilter))
            return true;

        return obj is Reviewer reviewer && reviewer.ToString().Contains(ReviewerFilter, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void ClearUi(bool clearAll = false)
    {
        Pr = null;
        Title = "";
        Body = "";
        Commits = [];
        VersionsToConsider = [];
        FeatureName = "";
        zohoUrl = "";
        Reviewers.Clear();

        if (clearAll)
        {
            PrNumber = "";
        }
    }

    [RelayCommand]
    private void OpenZoho()
    {
        Process.Start(new ProcessStartInfo()
        {
            FileName = zohoUrl,
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    private void ClearLogs()
    {
        dispatcher.Invoke(Logs.Clear);
    }

    private bool CanUpmerge()
    {
        if (string.IsNullOrWhiteSpace(FeatureName))
            return false;

        if (MaxMajorVersion < 4 || MaxMinorVersion < 0)
            return false;

        if (!CanStatus())
            return false;

        if (VersionsToConsider is null || VersionsToConsider.Count == 0 || Commits is null || Commits.Count == 0)
            return false;

        return Commits.Any(x => x.IsSelected) && VersionsToConsider.Any(x => x.IsSelected && !x.IsCherryPicked);
    }

    [RelayCommand(CanExecute = nameof(CanUpmerge))]
    private async Task UpMerge()
    {
        for (int i = 0; i < VersionsToConsider.Count; i++)
        {
            var version = VersionsToConsider[i];

            if (version.IsCherryPicked || version.IsCherryPicked || !version.IsSelected)
                continue;

            var success = await UpmergeOne(version);
            if (success)
            {
                version.IsCherryPicked = true;
            }
            else
            {
                var result = MessageBox.Show($"Upmerge of version {version} failed.\r\nYes: Retry\r\nNo: Skip this version\r\nCancel: Cancel the rest of the upmerge", "Upmerge failed", MessageBoxButton.YesNoCancel);
                switch (result)
                {
                    case MessageBoxResult.Cancel:
                        return;
                    case MessageBoxResult.Yes:
                        i--;
                        break;
                    case MessageBoxResult.No:
                        continue;
                }
            }
        }
    }

    private CredentialsHandler? GetCredentialsHandler()
    {
        try
        {
            var pw = string.IsNullOrWhiteSpace(Password) ? CredentialManager.GetCredentials("git:https://github.com")?.Password : Password;

            if (pw is null)
            {
                MessageBox.Show("Either provide one in the textbox or ensure 'git:https://github.com' is in the windows credential store", "Could not read password");
                return null;
            }

            return new CredentialsHandler((url, usernameFromUrl, types) =>
            new UsernamePasswordCredentials()
            {
                Username = UserName,
                Password = pw,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot read 'git:https://github.com' from windows credential store{Environment.NewLine}{ex.Message}",
                "Error reading git credtentials");
            return null;
        }
    }

    private void Fetch()
    {
        using var repo = new Repository(RepoPath);

        var log = "";
        var remote = repo.Network.Remotes["origin"];
        var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);

        FetchOptions options = new()
        {
            CredentialsProvider = GetCredentialsHandler()
        };

        if (options.CredentialsProvider is null)
            return;

        Commands.Fetch(repo, remote.Name, refSpecs, options, log);
        Log(log);
    }

    private void Log(string msg)
    {
        dispatcher.Invoke(() => Logs.Add($"[{DateTime.Now:T}] {msg}"));
    }

    private async Task<bool> CreateAndSwitchBranch(string newBranchName, string remoteBranchName)
    {
        using var repo = new Repository(RepoPath);
        if (repo.Branches[newBranchName] is null)
        {
            // checkout a new branch from remote branch and switch to the new branch
            return await Git(new("git checkout failed", $"git checkout -b {newBranchName} origin/{remoteBranchName} failed"), "checkout", "-b", newBranchName, $"origin/{remoteBranchName}", "--no-track");
        }
        else
        {
            // switch to new the branch
            return await SwitchBranch(newBranchName);
        }
    }

    private async Task<bool> SwitchBranch(string newBranch)
    {
        try
        {
            await Git(new("checkout failed"), $"checkout", newBranch);
            using var repo = new Repository(RepoPath);
            return repo.Branches[newBranch] is not null;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, $"Error switching to branch '{newBranch}'");
            return false;
        }
    }

    private async Task<bool> UpmergeOne(BranchVersion upmergeToVersion)
    {
        try
        {
            Fetch();
            if (!await CreateAndSwitchBranch(upmergeToVersion.GetBranchNameForChanges(FeatureName), upmergeToVersion.ToString()))
                return false;

            foreach (var commit in Commits.Where(x => x.IsSelected).ToArray())
            {
                var hasConflicts = false;
                if (HasConflicts())
                {
                    hasConflicts = true;
                }
                else
                {
                    using var repo = new Repository(RepoPath);
                    var options = new CherryPickOptions()
                    {
                        CommitOnSuccess = true,
                    };
                    Log("Cherry pick: " + commit);
                    try
                    {
                        var sha = commit.Value.Sha;
                        var c = repo.Lookup<LibGit2Sharp.Commit>(sha);

                        if (c is null)
                        {
                            do
                            {
                                if (Pr is null || Pr.ClosedAt is null)
                                {
                                    MessageBox.Show($"Commit: {sha}", "Commit doesn't exist");
                                }
                                else
                                {
                                    var mbox = MessageBox.Show(
                                        $"It seems you PR was closed at {Pr.ClosedAt.Value.ToLocalTime()}.\r\n" +
                                        $"You need to restore the branch\r\n" +
                                        $"Commit: {sha}\r\n\r\n" +
                                        $"Yes: try to find commit again\r\n" +
                                        $"No: Skip cherry-pick commit\r\n" +
                                        $"Cancel: Abort",
                                        "Commit doesn't exist", MessageBoxButton.YesNoCancel);

                                    if (mbox == MessageBoxResult.Yes)
                                    {
                                        c = repo.Lookup<LibGit2Sharp.Commit>(sha);
                                    }
                                    else if (mbox == MessageBoxResult.No)
                                    {
                                        break;
                                    }
                                    else
                                    {
                                        return false;
                                    }
                                }
                            }
                            while (c is null);
                        }

                        if (c is null)
                            continue;

                        var result = repo.CherryPick(c, c.Author, options);
                        hasConflicts = result.Status == CherryPickStatus.Conflicts;
                    }
                    catch (LibGit2SharpException ex)
                    {
                        Log(ex.Message);
                        MessageBox.Show(ex.Message);
                        return false;
                    }
                }

                if (hasConflicts)
                {
                    do
                    {
                        using var repo = new Repository(RepoPath);
                        var conflicts = repo.Index.Conflicts.Cast<Conflict>();
                        var files = Environment.NewLine + string.Join(Environment.NewLine, conflicts.Select(x => x.Ancestor.Path));
                        var mbox = MessageBox.Show("Waiting for merge conflicts to be solved..\r\n Yes: try-again\r\nNo: cherry-pick --continue\r\nCancel: Abort" + files, "cherry pick failed", MessageBoxButton.YesNoCancel);

                        switch (mbox)
                        {
                            case MessageBoxResult.Yes:
                                // try again
                                break;

                            case MessageBoxResult.No:
                                // cherry pick continue
                                await Git(new("add . failed", ShowDialog: false), "add", ".");
                                await Git(new("cherry pick continue failed"), "cherry-pick", "--continue");
                                break;

                            case MessageBoxResult.Cancel:
                            default:
                                return false;
                        }
                    } while (HasConflicts());
                }
            }
            await Push();
            PullRequest(upmergeToVersion);
        }

        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error during upmerge");
            return false;
        }
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanStatus))]
    private async Task Status()
    {
        await Git(new("git status failed"), "status");
    }

    private bool CanStatus()
    {
        return RepoPath is not null && Directory.Exists(RepoPath);
    }

    private bool HasConflicts()
    {
        using var repo = new Repository(RepoPath);
        return !repo.Index.IsFullyMerged;
    }

    private void PullRequest(BranchVersion newBranchVersion)
    {
        var description = "";

        var title = string.IsNullOrWhiteSpace(Title)
            ? "&title=" + HttpUtility.UrlEncode($"({newBranchVersion})")
            : "&title=" + HttpUtility.UrlEncode($"{Title} ({newBranchVersion})");

        if (!string.IsNullOrWhiteSpace(Body))
        {
            description = "&body=" + HttpUtility.UrlEncode(Body);
        }

        var branchName = newBranchVersion.GetRemoteBranchName();
        var newBranchName = newBranchVersion.GetBranchNameForChanges(FeatureName);
        OpenUrl($"https://github.com/airsphere-gmbh/PaxControl/compare/{branchName}...{newBranchName}?quick_pull=1&labels=upmerge{title}{description}");
    }

    private void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private async Task<string> NewBranch(BranchVersion newBranchVersion)
    {
        var s = newBranchVersion.ToString();
        await CreateNewBranch(s);
        return s;
    }

    private string CurrentBranchName()
    {
        using var repo = new Repository(RepoPath);
        return repo.Head.FriendlyName; // support/v4.17
    }

    private async Task CreateNewBranch(string name)
    {
        await Git(new("git checkout failed", $"git checkout -b {name} failed"), "checkout", "-b", name);
    }

    record ErrorInfo(string ErrorTitle, string? ErrorMessage = null, bool ShowDialog = true);
    private async Task<bool> Git(ErrorInfo errorInfo, params string[] arg)
    {
        var retry = false;
        do
        {
            retry = false;
            var messages = new StringBuilder();
            try
            {
                var pipe = PipeTarget.Merge(PipeTarget.ToDelegate(Log), PipeTarget.ToStringBuilder(messages));
                var cmd = Cli.Wrap("git.exe")
                    .WithArguments(arg)
                    .WithStandardErrorPipe(pipe)
                    .WithStandardOutputPipe(pipe)
                    .WithWorkingDirectory(RepoPath);
                await cmd.ExecuteAsync();
            }
            catch (CommandExecutionException)
            {
                if (!errorInfo.ShowDialog)
                    return false;

                var msg = errorInfo.ErrorMessage is null ? messages.ToString() : errorInfo.ErrorMessage + Environment.NewLine + Environment.NewLine + messages.ToString();
                var result = MessageBox.Show("Ok: Retry\r\nCancel: Abort\r\n\r\n" + msg, errorInfo.ErrorTitle, MessageBoxButton.OKCancel);

                switch (result)
                {
                    case MessageBoxResult.OK:
                        retry = true;
                        break;

                    case MessageBoxResult.Cancel:
                    default:
                        return false;
                }
            }
        } while (retry);

        return true;
    }

    private async Task Push()
    {
        await Git(new("git push failed", "git push -u origin failed"), "push", "-u", "origin", CurrentBranchName().ToString());
    }
}
